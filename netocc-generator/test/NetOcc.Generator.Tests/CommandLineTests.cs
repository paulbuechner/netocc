// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

// NUnit
using NUnit.Framework;

namespace NetOcc.Generator.Tests;

/// <summary>
/// A command's options, flags and names, strictly: an option it doesn't take is an error.
/// </summary>
[TestFixture]
public class CommandLineTests
{
  [Test]
  public void Parse_TellsOptionsFlagsAndNamesApart()
  {
    // Arrange
    string[] arguments = ["--occt-src", "occt", "TopoDS", "--check", "BRep"];

    // Act
    var line = CommandLine.Parse("generate", arguments, ["--occt-src", "--core"], ["--check"]);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(line.Option("--occt-src"), Is.EqualTo("occt"));
      Assert.That(line.Option("--core"), Is.Null);
      Assert.That(line.Flag("--check"), Is.True);
      Assert.That(line.Names, Is.EqualTo(new[] { "TopoDS", "BRep" }));
    }
  }

  [Test]
  public void Parse_UnknownOption_Fails()
  {
    // Act (a typo mustn't fall back to a default unnoticed)
    Action parse = () => CommandLine.Parse("generate", ["--occt-source", "occt"], ["--occt-src"]);

    // Assert
    Assert.That(
      parse,
      Throws.ArgumentException.With.Message.EqualTo(
        "netocc-gen generate: unknown option --occt-source"));
  }

  [Test]
  public void Parse_OptionFollowedByAnOption_Fails()
  {
    // Act (--check is a flag, not --occt-src's value)
    Action parse = () =>
      CommandLine.Parse("generate", ["--occt-src", "--check"], ["--occt-src"], ["--check"]);

    // Assert
    Assert.That(
      parse,
      Throws.ArgumentException.With.Message.EqualTo(
        "netocc-gen generate: --occt-src needs a value"));
  }

  [Test]
  public void Required_MissingOption_Fails()
  {
    // Arrange
    var line = CommandLine.Parse("docs", [], ["--out"]);

    // Act
    Action required = () => line.Required("--out");

    // Assert
    Assert.That(
      required,
      Throws.ArgumentException.With.Message.EqualTo("netocc-gen docs: --out is required"));
  }
}
