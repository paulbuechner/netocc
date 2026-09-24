// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

// NUnit
using NUnit.Framework;

namespace NetOcc.Tests;

/// <summary>
/// Logs the process architecture and runtime once per run (x86/x64, CLR 2/4, .NET 6+).
/// </summary>
[SetUpFixture]
public sealed class ProcessInfo
{
  [OneTimeSetUp]
  public void LogProcess() =>
    TestContext.Progress.WriteLine(
      $"NetOcc tests: {IntPtr.Size * 8}-bit process, CLR {Environment.Version}");
}
