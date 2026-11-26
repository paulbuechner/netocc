// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

#if NET5_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;

// NUnit
using NUnit;
using NUnit.Framework.Api;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;

namespace NetOcc.Vtk.Tests;

/// <summary>
/// A test on macOS's main thread: this assembly, run by the runtime's host in a process of its own
/// (<c>main-thread &lt;test&gt;</c>), runs the test through NUnit on its main thread
/// (RunOnMainThread), and the test's result is that run's. VSTest's host runs no test on its own.
/// </summary>
internal static class MainThreadRun
{
  public const string Command = "main-thread";
  private const int Timeout = 120000;

  // in the process of its own: its tests run on the main thread already
  private static bool _running;

  /// <summary>Whether a test needs a process of its own: on macOS, but in that process.</summary>
  public static bool Needed => OperatingSystem.IsMacOS() && !_running;

  /// <summary>The test, its set-up and tear-down in a process of its own.</summary>
  public static TestCommand Wrap(TestCommand command) => new RunCommand(command);

  /// <summary>
  /// The process's side: the test through NUnit on the main thread. Prints its output, its message
  /// and stack trace unless it passed; exits with 0 (passed), 1 (failed), 3 (skipped) or 4
  /// (inconclusive).
  /// </summary>
  public static int Run(string test)
  {
    _running = true;
    var runner = new NUnitTestAssemblyRunner(new DefaultTestAssemblyBuilder());
    runner.Load(typeof(MainThreadRun).Assembly,
                new Dictionary<string, object>
                  { [FrameworkPackageSettings.RunOnMainThread] = true });
    var filter =
      TestFilter.FromXml($"<filter><test>{SecurityElement.Escape(test)}</test></filter>");
    var cases = Cases(runner.Run(TestListener.NULL, filter)).ToList();
    if (cases.Count != 1)
    {
      Console.WriteLine($"{cases.Count} tests named {test}");
      return 2;
    }

    var result = cases[0];
    Console.Write(result.Output);
    if (result.ResultState.Status != TestStatus.Passed)
    {
      Console.WriteLine(result.Message);
      Console.WriteLine(result.StackTrace);
    }

    return result.ResultState.Status switch
    {
      TestStatus.Passed => 0,
      TestStatus.Skipped => 3,
      TestStatus.Inconclusive => 4,
      _ => 1,
    };
  }

  // the test cases a run's result holds
  private static IEnumerable<ITestResult> Cases(ITestResult result) =>
    result.HasChildren ? result.Children.SelectMany(Cases) : [result];

  // the test in this assembly, run by the runtime's host: its exit code, what it printed
  private static (int ExitCode, string Output) Start(string test)
  {
    // the runtime's directory: <root>/shared/Microsoft.NETCore.App/<version>
    var root = Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..");
    var start = new ProcessStartInfo(Path.GetFullPath(Path.Combine(root, "dotnet")))
    {
      UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
    };
    start.ArgumentList.Add(typeof(MainThreadRun).Assembly.Location);
    start.ArgumentList.Add(Command);
    start.ArgumentList.Add(test);
    using var process = Process.Start(start)!;
    var output = process.StandardOutput.ReadToEndAsync();
    var errors = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(Timeout))
    {
      process.Kill();
      return (-1, $"the process didn't end in {Timeout} ms");
    }

    return (process.ExitCode, output.Result + errors.Result);
  }

  private sealed class RunCommand(TestCommand command) : DelegatingTestCommand(command)
  {
    public override TestResult Execute(TestExecutionContext context)
    {
      var (exitCode, output) = Start(Test.FullName);
      var result = context.CurrentResult;
      result.OutWriter.Write(output);
      switch (exitCode)
      {
        case 0:
          result.SetResult(ResultState.Success);
          break;
        case 3:
          result.SetResult(ResultState.Ignored, output);
          break;
        case 4:
          result.SetResult(ResultState.Inconclusive, output);
          break;
        default:
          result.SetResult(ResultState.Failure,
                           $"on the main thread, in a process of its own (exit code {exitCode}): {output}");
          break;
      }

      return result;
    }
  }
}
#endif
