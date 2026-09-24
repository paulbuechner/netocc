// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace OCC.Core;

/// <summary>
/// UTF-8 string marshaling for the generated code. OCCT reads paths as UTF-8, while
/// default P/Invoke char* marshaling is ANSI on Windows; netstandard2.0 has no LPUTF8Str.
/// </summary>
internal static class Utf8
{
  /// <summary>Null-terminated UTF-8 bytes, or null.</summary>
  public static byte[] Encode(string value)
  {
    if (value is null)
    {
      return null;
    }

    var bytes = new byte[Encoding.UTF8.GetByteCount(value) + 1];
    Encoding.UTF8.GetBytes(value, 0, value.Length, bytes, 0);
    return bytes;
  }

  /// <summary>
  /// Null-terminated UTF-8 copy in unmanaged memory (free with <see cref="Marshal.FreeHGlobal"/>),
  /// or zero.
  /// </summary>
  public static IntPtr Alloc(string value)
  {
    if (Encode(value) is not { } bytes)
    {
      return IntPtr.Zero;
    }

    var buffer = Marshal.AllocHGlobal(bytes.Length);
    Marshal.Copy(bytes, 0, buffer, bytes.Length);
    return buffer;
  }

  /// <summary>Decodes a null-terminated UTF-8 string owned by native code.</summary>
  public static unsafe string Decode(IntPtr value)
  {
    if (value == IntPtr.Zero)
    {
      return null;
    }

    var bytes = (byte*)value;
    var length = 0;
    while (bytes[length] != 0)
    {
      length++;
    }

    // not Encoding.GetString(byte*, int): that overload arrived in .NET Framework 4.6
    return new string((sbyte*)bytes, 0, length, Encoding.UTF8);
  }
}
