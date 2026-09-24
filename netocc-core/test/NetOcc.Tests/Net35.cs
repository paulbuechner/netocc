// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

#if NET35
using System;

// NUnit
using NUnit.Framework;
using NUnitLite;

namespace NetOcc.Tests;

/// <summary>
/// The net35 target runs itself on CLR 2: NUnit 4 needs .NET Framework 4.6.2 and VSTest can't host
/// CLR 2, so it is an exe on NUnit 3 whose Main hands the assembly to NUnitLite. Exit code: the
/// number of failures.
/// </summary>
internal static class Program
{
  private static int Main(string[] args) => new AutoRun().Execute(args);
}

/// <summary>
/// NUnit 3 has no <c>Assert.EnterMultipleScope</c>. Here it is a plain scope: a group of asserts
/// stops at its first failure instead of reporting all of them.
/// </summary>
internal static class Net35Assert
{
  extension(Assert)
  {
    public static IDisposable EnterMultipleScope() => new Scope();
  }

  private sealed class Scope : IDisposable
  {
    public void Dispose()
    {
    }
  }
}
#endif
