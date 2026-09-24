// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

namespace NetOcc.Tests;

/// <summary>
/// Full garbage collections for the lifetime tests: what C# no longer holds must be gone, or kept
/// alive.
/// </summary>
internal static class Garbage
{
  /// <summary>Collects, runs the finalizers, then collects what they released.</summary>
  public static void Collect()
  {
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
  }
}
