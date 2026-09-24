// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

namespace OCC.Core;

/// <summary>size_t at the P/Invoke layer (Types.i): pointer-sized, four bytes on x86.</summary>
internal static class NativeSize
{
  /// <summary>
  /// The value as a size_t. One a 32-bit size_t can't hold throws, as for a C long
  /// (Standard_OutOfRange).
  /// </summary>
  public static UIntPtr Of(ulong value) =>
    UIntPtr.Size == 4 && value > uint.MaxValue
      ? throw new OcctException("Standard_OutOfRange", "the value doesn't fit a size_t")
      : new UIntPtr(value);
}
