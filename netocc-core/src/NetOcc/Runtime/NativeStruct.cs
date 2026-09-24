// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

namespace OCC.Core;

/// <summary>
/// Reads a value-type result the native wrapper left in its thread-local return buffer
/// (see src/SWIG_files/common/ValueTypes.i for why structs are not returned by value).
/// </summary>
internal static class NativeStruct
{
  public static unsafe T Read<T>(IntPtr ptr) where T : unmanaged => *(T*)ptr;
}
