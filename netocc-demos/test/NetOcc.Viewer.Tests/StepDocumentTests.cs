// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;

// NUnit
using NUnit.Framework;

//
using OCC.Core.XCAFDoc;

namespace NetOcc.Viewer.Tests;

/// <summary>
/// What the demos show: the sample written as STEP, read back into an XCAF document.
/// </summary>
[TestFixture]
public class StepDocumentTests
{
  private string _directory = null!;

  [SetUp]
  public void CreateDirectory()
  {
    _directory = Path.Combine(Path.GetTempPath(), $"netocc-viewer Ünïcödé {Guid.NewGuid():N}");
    Directory.CreateDirectory(_directory);
  }

  [TearDown]
  public void DeleteDirectory() => Directory.Delete(_directory, true);

  [Test]
  public void Read_FindsTheSampleAssembly()
  {
    // Arrange
    var path = Path.Combine(_directory, "sample Ünïcödé.step");
    SampleModel.Write(path);

    // Act
    using var document = StepDocument.Read(path);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(document.Roots, Has.Count.EqualTo(1));
      Assert.That(XCAFDoc_ShapeTool.IsAssembly(document.Roots[0]), Is.True);
    }
  }

  [Test]
  public void Read_RejectsAFileThatIsntStep()
  {
    // Arrange
    var path = Path.Combine(_directory, "notes.step");
    File.WriteAllText(path, "not a STEP file");

    // Act
    Action read = () => StepDocument.Read(path).Dispose();

    // Assert
    Assert.That(read, Throws.TypeOf<InvalidDataException>());
  }
}
