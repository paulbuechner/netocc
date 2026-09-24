// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;

// NUnit
using NUnit.Framework;

namespace NetOcc.Samples;

/// <summary>A temporary directory per test, which the examples write their files into.</summary>
public abstract class Files
{
  private string _previous = null!;

  protected string Directory { get; private set; } = null!;

  [SetUp]
  public void EnterDirectory()
  {
    Directory = Path.Combine(Path.GetTempPath(), $"netocc-samples-{Guid.NewGuid():N}");
    System.IO.Directory.CreateDirectory(Directory);
    _previous = Environment.CurrentDirectory;
    Environment.CurrentDirectory = Directory;
  }

  [TearDown]
  public void LeaveDirectory()
  {
    Environment.CurrentDirectory = _previous;
    System.IO.Directory.Delete(Directory, true);
  }
}
