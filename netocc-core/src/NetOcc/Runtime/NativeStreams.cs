// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace OCC.Core;

/// <summary>
/// The native stream an OCCT call writes into (src/SWIG_files/common/Streams.i); afterwards its
/// bytes go to the caller's <see cref="Stream"/>.
/// </summary>
internal sealed class NativeOutput
{
  private NativeOutput(IntPtr handle) => Handle = handle;

  /// <summary>The <c>std::ostream*</c> the wrapper passes to OCCT.</summary>
  public IntPtr Handle { get; private set; }

  public static NativeOutput For(Stream target)
  {
    if (target is null)
    {
      throw new ArgumentNullException(nameof(target));
    }

    if (!target.CanWrite)
    {
      throw new ArgumentException("The stream isn't writable.", nameof(target));
    }

    var handle = NativeStreams.NetOcc_OStreamNew();
    return handle != IntPtr.Zero
      ? new NativeOutput(handle)
      : throw new OutOfMemoryException("No memory for a native stream.");
  }

  /// <summary>
  /// Writes what OCCT wrote to <paramref name="target"/>, even after a failed call, and frees the
  /// native stream.
  /// </summary>
  public unsafe void CopyToAndFree(Stream target)
  {
    try
    {
      var size = NativeStreams.NetOcc_OStreamSize(Handle);
      var data = (byte*)NativeStreams.NetOcc_OStreamData(Handle).ToPointer();
      var buffer = new byte[Math.Min(size, NativeStreams.ChunkSize)];
      for (long done = 0; done < size;)
      {
        // pointer arithmetic: a 32-bit process's buffer may cross 2 GB, which new IntPtr(long)
        // rejects
        var chunk = (int)Math.Min(buffer.Length, size - done);
        Marshal.Copy(new IntPtr(data + done), buffer, 0, chunk);
        target.Write(buffer, 0, chunk);
        done += chunk;
      }
    }
    finally
    {
      NativeStreams.NetOcc_OStreamDelete(Handle);
      Handle = IntPtr.Zero;
    }
  }
}

/// <summary>
/// The native stream an OCCT call reads from: the remaining bytes of the caller's
/// <see cref="Stream"/>, pinned for the call and read in place.
/// </summary>
internal sealed class NativeInput
{
  private readonly long _start;
  private GCHandle _pin;

  private NativeInput(byte[] bytes, long length, long start)
  {
    _start = start;
    _pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
    Handle = NativeStreams.NetOcc_IStreamNew(_pin.AddrOfPinnedObject(), length);
    if (Handle == IntPtr.Zero)
    {
      _pin.Free();
      throw new OutOfMemoryException("No memory for a native stream.");
    }
  }

  /// <summary>The <c>std::istream*</c> the wrapper passes to OCCT.</summary>
  public IntPtr Handle { get; private set; }

  public static NativeInput For(Stream source)
  {
    if (source is null)
    {
      throw new ArgumentNullException(nameof(source));
    }

    if (!source.CanRead)
    {
      throw new ArgumentException("The stream isn't readable.", nameof(source));
    }

    // one buffer: the remaining bytes of a seekable stream, else what the stream gives until its
    // end
    if (source.CanSeek)
    {
      var start = source.Position;
      var bytes = new byte[Math.Max(0, source.Length - start)];
      var filled = 0;
      int read;
      while (filled < bytes.Length
             && (read = source.Read(bytes, filled, bytes.Length - filled)) > 0)
      {
        filled += read;
      }

      return new NativeInput(bytes, filled, start);
    }

    using var copy = new MemoryStream();
    var chunk = new byte[NativeStreams.ChunkSize];
    int got;
    while ((got = source.Read(chunk, 0, chunk.Length)) > 0)
    {
      copy.Write(chunk, 0, got);
    }

    return new NativeInput(copy.GetBuffer(), copy.Length, -1);
  }

  /// <summary>
  /// Leaves a seekable <paramref name="source"/> where OCCT stopped reading and frees the native
  /// stream.
  /// </summary>
  public void Free(Stream source)
  {
    try
    {
      if (_start >= 0 && source.CanSeek)
      {
        source.Position = _start + NativeStreams.NetOcc_IStreamPosition(Handle);
      }
    }
    finally
    {
      NativeStreams.NetOcc_IStreamDelete(Handle);
      Handle = IntPtr.Zero;
      _pin.Free();
    }
  }
}

// src/Native/NetOccStreams.cxx, in NetOccRuntime
internal static class NativeStreams
{
  /// <summary>The bytes copied at a time between a native and a C# stream.</summary>
  internal const int ChunkSize = 81920;

  [DllImport(NativeLibraryLoader.RuntimeLibrary, CallingConvention = CallingConvention.Cdecl)]
  public static extern IntPtr NetOcc_OStreamNew();

  [DllImport(NativeLibraryLoader.RuntimeLibrary, CallingConvention = CallingConvention.Cdecl)]
  public static extern IntPtr NetOcc_OStreamData(IntPtr stream);

  [DllImport(NativeLibraryLoader.RuntimeLibrary, CallingConvention = CallingConvention.Cdecl)]
  public static extern long NetOcc_OStreamSize(IntPtr stream);

  [DllImport(NativeLibraryLoader.RuntimeLibrary, CallingConvention = CallingConvention.Cdecl)]
  public static extern void NetOcc_OStreamDelete(IntPtr stream);

  [DllImport(NativeLibraryLoader.RuntimeLibrary, CallingConvention = CallingConvention.Cdecl)]
  public static extern IntPtr NetOcc_IStreamNew(IntPtr data, long size);

  [DllImport(NativeLibraryLoader.RuntimeLibrary, CallingConvention = CallingConvention.Cdecl)]
  public static extern long NetOcc_IStreamPosition(IntPtr stream);

  [DllImport(NativeLibraryLoader.RuntimeLibrary, CallingConvention = CallingConvention.Cdecl)]
  public static extern void NetOcc_IStreamDelete(IntPtr stream);
}
