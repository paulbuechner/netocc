// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

#if NET5_0_OR_GREATER
namespace NetOcc.Vtk.Tests;

/// <summary>
/// The assembly's entry point on .NET, in place of the test SDK's empty one, for a rendering test
/// on macOS's main thread: <c>main-thread &lt;test&gt;</c> (<see cref="MainThreadRun"/>).
/// </summary>
internal static class Program
{
  private static int Main(string[] args) => args switch
  {
    [MainThreadRun.Command, var test] => MainThreadRun.Run(test),
    _ => 2,
  };
}
#endif
