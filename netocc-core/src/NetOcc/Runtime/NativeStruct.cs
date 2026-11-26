// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

namespace OCC.Core;

/// <summary>
/// Reads a value-type result the native wrapper left in its thread-local return buffer
/// (see src/SWIG_files/common/ValueTypes.i for why structs are not returned by value), and moves
/// numbers and structs through director callbacks (Directors.i).
/// </summary>
internal static class NativeStruct
{
  // the largest struct a C# override returns (gp_GTrsf, gp_Mat: fewer bytes)
  private const int StageSize = 256;

  [ThreadStatic] private static IntPtr _stage;

  public static unsafe T Read<T>(IntPtr ptr) where T : unmanaged => *(T*)ptr;

  /// <summary>Writes a callback's in/out parameter back into the native one.</summary>
  public static unsafe void Write<T>(IntPtr ptr, T value) where T : unmanaged => *(T*)ptr = value;

  /// <summary>
  /// A struct an override returns, in this thread's buffer (allocated once per thread), which the
  /// callback's C++ side copies right after the callback.
  /// </summary>
  public static unsafe IntPtr Stage<T>(T value) where T : unmanaged
  {
    if (sizeof(T) > StageSize)
    {
      throw new ArgumentException(
        $"{typeof(T).Name} exceeds the {StageSize} bytes of a callback's result");
    }

    if (_stage == IntPtr.Zero)
    {
      _stage = global::System.Runtime.InteropServices.Marshal.AllocHGlobal(StageSize);
    }

    *(T*)_stage = value;
    return _stage;
  }
}
