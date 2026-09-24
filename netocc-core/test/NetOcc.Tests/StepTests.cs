// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.IO;

// NUnit
using NUnit.Framework;

//
using OCC.Core.IFSelect;
using OCC.Core.STEPControl;

namespace NetOcc.Tests;

/// <summary>
/// STEP I/O through a non-ASCII path: default ANSI char* marshaling would break it on Windows.
/// </summary>
[TestFixture]
public class StepTests
{
  private string _directory = null!;

  [SetUp]
  public void CreateNonAsciiDirectory()
  {
    _directory = NonAscii.CreateTempDirectory();
  }

  [TearDown]
  public void DeleteDirectory() => Directory.Delete(_directory, true);

  [Test]
  public void Write_CreatesTheFileAtTheUtf8Path()
  {
    // Arrange
    var file = Path.Combine(_directory, $"part {NonAscii.Text}.step");
    var writer = new STEPControl_Writer();

    // Act
    var transfer = writer.Transfer(Shapes.Fused(), STEPControl_StepModelType.STEPControl_AsIs);
    var write = writer.Write(file);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(transfer, Is.EqualTo(IFSelect_ReturnStatus.IFSelect_RetDone));
      Assert.That(write, Is.EqualTo(IFSelect_ReturnStatus.IFSelect_RetDone));
      Assert.That(File.Exists(file), Is.True, "STEP file not found at the UTF-8 path");
    }
  }

  [Test]
  public void RoundTrip_PreservesTheVolume()
  {
    // Arrange
    var file = Path.Combine(_directory, $"part {NonAscii.Text}.step");
    var writer = new STEPControl_Writer();
    writer.Transfer(Shapes.Fused(), STEPControl_StepModelType.STEPControl_AsIs);
    writer.Write(file);
    var reader = new STEPControl_Reader();

    // Act
    var read = reader.ReadFile(file);
    var roots = reader.TransferRoots();
    var shape = reader.OneShape();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(read, Is.EqualTo(IFSelect_ReturnStatus.IFSelect_RetDone));
      Assert.That(roots, Is.GreaterThan(0));
      Assert.That(shape.IsNull(), Is.False);
      Assert.That(Shapes.Volume(shape), Is.EqualTo(Shapes.FusedVolume).Within(1e-2));
    }
  }

  [Test]
  public void RoundTrip_WorksAtAPathBeyondLatin1()
  {
    // Arrange (3- and 4-byte UTF-8 sequences in the file name)
    var file = Path.Combine(_directory, $"part {NonAscii.WideText}.step");
    var writer = new STEPControl_Writer();
    writer.Transfer(Shapes.Box(), STEPControl_StepModelType.STEPControl_AsIs);
    var reader = new STEPControl_Reader();

    // Act
    var write = writer.Write(file);
    var read = reader.ReadFile(file);
    reader.TransferRoots();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(write, Is.EqualTo(IFSelect_ReturnStatus.IFSelect_RetDone));
      Assert.That(File.Exists(file), Is.True, "STEP file not found at the UTF-8 path");
      Assert.That(read, Is.EqualTo(IFSelect_ReturnStatus.IFSelect_RetDone));
      Assert.That(Shapes.Volume(reader.OneShape()), Is.EqualTo(Shapes.BoxVolume).Within(1e-6));
    }
  }
}
