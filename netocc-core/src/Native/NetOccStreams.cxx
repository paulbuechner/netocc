// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Memory streams behind the C# Stream parameters (src/SWIG_files/common/Streams.i). OCCT writes
// into or reads from memory, and C# copies the bytes (NativeOutput, NativeInput in
// src/NetOcc/Runtime): no calls back into C# during the call, and seeking works, which binary
// formats use.

#include <algorithm>
#include <cstring>
#include <exception>
#include <istream>
#include <ostream>
#include <streambuf>
#include <vector>

#if defined(_WIN32)
  #define NETOCC_EXPORT extern "C" __declspec(dllexport)
#else
  #define NETOCC_EXPORT extern "C" __attribute__((visibility("default")))
#endif

namespace
{

// the bytes written so far, and a put position a writer may move back (to patch a header)
class OutBuffer : public std::streambuf
{
public:
  const char* Data() const { return myData.data(); }

  long long Size() const { return static_cast<long long>(myEnd); }

protected:
  int_type overflow(int_type theChar) override
  {
    if (traits_type::eq_int_type(theChar, traits_type::eof()))
    {
      return traits_type::not_eof(theChar);
    }

    const char aChar = traits_type::to_char_type(theChar);
    xsputn(&aChar, 1);
    return theChar;
  }

  std::streamsize xsputn(const char* theData, std::streamsize theCount) override
  {
    const size_t anEnd = myPos + static_cast<size_t>(theCount);
    if (anEnd > myData.size())
    {
      myData.resize(std::max(anEnd, myData.size() * 2));
    }

    std::memcpy(myData.data() + myPos, theData, static_cast<size_t>(theCount));
    myPos = anEnd;
    myEnd = std::max(myEnd, myPos);
    return theCount;
  }

  pos_type seekoff(off_type                theOffset,
                   std::ios_base::seekdir  theDir,
                   std::ios_base::openmode theWhich) override
  {
    const off_type aBase = theDir == std::ios_base::beg   ? 0
                           : theDir == std::ios_base::cur ? off_type(myPos)
                                                          : off_type(myEnd);
    return seekpos(pos_type(aBase + theOffset), theWhich);
  }

  pos_type seekpos(pos_type thePos, std::ios_base::openmode theWhich) override
  {
    if ((theWhich & std::ios_base::out) == 0 || off_type(thePos) < 0
        || off_type(thePos) > off_type(myEnd))
    {
      return pos_type(off_type(-1));
    }

    myPos = static_cast<size_t>(off_type(thePos));
    return thePos;
  }

private:
  std::vector<char> myData;
  size_t            myPos = 0;
  size_t            myEnd = 0;
};

// the buffer is a base, so it exists before the stream that uses it
class OutStream : private OutBuffer, public std::ostream
{
public:
  OutStream()
      : std::ostream(static_cast<OutBuffer*>(this))
  {
  }

  using OutBuffer::Data;
  using OutBuffer::Size;
};

// C#'s bytes, read in place (the array is pinned for the call), seekable
class InBuffer : public std::streambuf
{
public:
  InBuffer(const char* theData, long long theSize)
  {
    char* aData = const_cast<char*>(theData);
    setg(aData, aData, aData + theSize);
  }

  long long Position() const { return static_cast<long long>(gptr() - eback()); }

protected:
  pos_type seekoff(off_type                theOffset,
                   std::ios_base::seekdir  theDir,
                   std::ios_base::openmode theWhich) override
  {
    const off_type aBase = theDir == std::ios_base::beg   ? 0
                           : theDir == std::ios_base::cur ? off_type(gptr() - eback())
                                                          : off_type(egptr() - eback());
    return seekpos(pos_type(aBase + theOffset), theWhich);
  }

  pos_type seekpos(pos_type thePos, std::ios_base::openmode theWhich) override
  {
    if ((theWhich & std::ios_base::in) == 0 || off_type(thePos) < 0
        || off_type(thePos) > off_type(egptr() - eback()))
    {
      return pos_type(off_type(-1));
    }

    setg(eback(), eback() + off_type(thePos), egptr());
    return thePos;
  }
};

class InStream : private InBuffer, public std::istream
{
public:
  InStream(const char* theData, long long theSize)
      : InBuffer(theData, theSize),
        std::istream(static_cast<InBuffer*>(this))
  {
  }

  using InBuffer::Position;
};

OutStream* Out(void* theStream)
{
  return static_cast<OutStream*>(static_cast<std::ostream*>(theStream));
}

InStream* In(void* theStream)
{
  return static_cast<InStream*>(static_cast<std::istream*>(theStream));
}

} // namespace

// the handles are the std::ostream* / std::istream* the wrappers pass to OCCT. No exception leaves
// them: a C++ exception that reaches .NET aborts the process off Windows. A failed allocation
// returns null, which C# reports.

NETOCC_EXPORT void* NetOcc_OStreamNew() noexcept
{
  try
  {
    return static_cast<std::ostream*>(new OutStream());
  }
  catch (const std::exception&)
  {
    return nullptr;
  }
}

NETOCC_EXPORT const void* NetOcc_OStreamData(void* theStream) noexcept
{
  return Out(theStream)->Data();
}

NETOCC_EXPORT long long NetOcc_OStreamSize(void* theStream) noexcept
{
  return Out(theStream)->Size();
}

NETOCC_EXPORT void NetOcc_OStreamDelete(void* theStream) noexcept
{
  delete Out(theStream);
}

NETOCC_EXPORT void* NetOcc_IStreamNew(const void* theData, long long theSize) noexcept
{
  try
  {
    return static_cast<std::istream*>(new InStream(static_cast<const char*>(theData), theSize));
  }
  catch (const std::exception&)
  {
    return nullptr;
  }
}

NETOCC_EXPORT long long NetOcc_IStreamPosition(void* theStream) noexcept
{
  return In(theStream)->Position();
}

NETOCC_EXPORT void NetOcc_IStreamDelete(void* theStream) noexcept
{
  delete In(theStream);
}
