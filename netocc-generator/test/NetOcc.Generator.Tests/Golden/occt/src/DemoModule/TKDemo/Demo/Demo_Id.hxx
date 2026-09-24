// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: class template instances named by an alias in the template's own header, each a class of that name
// (OCCT 8's BRepGraph ids). The id is plain data, a C# struct; the headers never instantiate the iterator, so the parse
// does. An instance without an alias is a class of its own, named after its arguments (Demo_TypedId_3).
#pragma once

template <int TheKind>
struct Demo_TypedId
{
  unsigned int Index;

  Demo_TypedId() : Index(0) {}
  explicit Demo_TypedId(unsigned int theIndex) : Index(theIndex) {}
  bool IsValid() const { return Index != 0; }
};

using Demo_FaceId = Demo_TypedId<2>;

template <typename TheId>
class Demo_IdIterator
{
public:
  Demo_IdIterator(TheId theFirst, TheId theLast) : myCurrent(theFirst), myLast(theLast) {}
  bool  More() const { return myCurrent.Index <= myLast.Index; }
  void  Next() { ++myCurrent.Index; }
  TheId Current() const { return myCurrent; }

private:
  TheId myCurrent;
  TheId myLast;
};

using Demo_FaceIterator = Demo_IdIterator<Demo_FaceId>;

class Demo_Faces
{
public:
  Demo_Faces() {}
  Demo_FaceId     First() const { return Demo_FaceId(1); }
  Demo_TypedId<3> Other() const { return Demo_TypedId<3>(1); }
};
