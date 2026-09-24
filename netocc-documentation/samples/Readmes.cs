// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

// NUnit
using NUnit.Framework;

namespace NetOcc.Samples;

/// <summary>
/// The READMEs show tested examples, which a README can't include: the repository's (README.md)
/// shows Readme.cs, a whole Program.cs, the NetOcc package's (netocc-core/src/NetOcc/README.md, on
/// nuget.org) the getting-started sample. The csproj copies the files next to the tests.
/// </summary>
[TestFixture]
public class Readmes : Files
{
  private static readonly string Copies =
    Path.Combine(TestContext.CurrentContext.TestDirectory, "Readmes");

  [Test]
  public void RepositoryReadme_ShowsReadmeCs()
  {
    // Arrange
    var readme = Read("Repository.md");
    var program = Read("Readme.cs");

    // Act
    var example = Example(readme);

    // Assert (the file without its license header)
    Assert.That(example, Is.EqualTo(Regex.Replace(program, @"\A(//[^\n]*\n)+\n", "").TrimEnd('\n')),
                "README.md's example differs from Readme.cs: copy the file into it, without the license header");
  }

  [Test]
  public void RepositoryReadme_ProgramPrintsTheVolumeAndWritesTheStepFile()
  {
    // Arrange
    var main =
      typeof(FusedPart.Program).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static)!;
    var console = Console.Out;
    using var output = new StringWriter();
    Console.SetOut(output);

    // Act
    try
    {
      main.Invoke(null, null);
    }
    finally
    {
      Console.SetOut(console);
    }

    // Assert (the box, and the 10 of the cylinder above it)
    using (Assert.EnterMultipleScope())
    {
      Assert.That(output.ToString().Trim(),
                  Is.EqualTo($"volume: {10 * 20 * 30 + Math.PI * 3 * 3 * 10:F1}"));
      Assert.That(File.Exists("fused.step"), Is.True);
    }
  }

  [Test]
  public void PackageReadme_ShowsTheGettingStartedSample()
  {
    // Arrange
    var readme = Read("Package.md");
    var sample = Read("GettingStarted.cs");

    // Act
    var example = Example(readme);

    // Assert
    Assert.That(
      example, Is.EqualTo($"{Region(sample, "usings")}\n\n{Region(sample, "first-part")}"),
      "the package README's example differs from GettingStarted.cs: copy the regions usings and first-part into it");
  }

  private static string Read(string name) =>
    File.ReadAllText(Path.Combine(Copies, name)).Replace("\r\n", "\n");

  // the first C# code block
  private static string Example(string readme) => Regex
    .Match(readme, "```csharp\n(.*?)\n```", RegexOptions.Singleline)
    .Groups[1].Value;

  // a region's lines without their common indentation, as the documentation shows them
  private static string Region(string source, string name)
  {
    var lines = Regex
      .Match(source, $@"#region {Regex.Escape(name)}\n(.*?)\n[ \t]*#endregion",
             RegexOptions.Singleline)
      .Groups[1]
      .Value.Split('\n');
    var indent = lines.Where(line => line.Trim().Length > 0)
      .Min(line => line.Length - line.TrimStart().Length);
    return string.Join(
      "\n",
      lines.Select(line => line.Length >= indent ? line.Substring(indent) : line.TrimStart()));
  }
}
