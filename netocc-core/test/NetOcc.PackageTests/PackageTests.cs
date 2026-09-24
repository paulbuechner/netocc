// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BRepGProp;
using OCC.Core.BRepPrimAPI;
using OCC.Core.GProp;
using OCC.Core.IFSelect;
using OCC.Core.STEPControl;

namespace NetOcc.PackageTests;

/// <summary>
/// The installed package works: the natives land where NetOcc's loader looks (runtimes\ on .NET,
/// x64\ or x86\ on .NET Framework) and resolve their OCCT dependencies there.
/// </summary>
[TestFixture]
public class PackageTests
{
  [Test]
  public void Box_HasItsVolume()
  {
    // Arrange
    var box = new BRepPrimAPI_MakeBox(10.0, 20.0, 30.0).Shape();
    var properties = new GProp_GProps();

    // Act
    BRepGProp.VolumeProperties(box, properties);

    // Assert
    Assert.That(properties.Mass(), Is.EqualTo(6000.0).Within(1e-6));
  }

  [Test]
  public void StepWriter_WritesTheFile()
  {
    // Arrange
    var directory = Path.Combine(Path.GetTempPath(), $"netocc-package-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    var file = Path.Combine(directory, "box.step");
    var writer = new STEPControl_Writer();

    try
    {
      // Act
      var transfer = writer.Transfer(new BRepPrimAPI_MakeBox(1.0, 2.0, 3.0).Shape(),
                                     STEPControl_StepModelType.STEPControl_AsIs);
      var write = writer.Write(file);

      // Assert
      using (Assert.EnterMultipleScope())
      {
        Assert.That(transfer, Is.EqualTo(IFSelect_ReturnStatus.IFSelect_RetDone));
        Assert.That(write, Is.EqualTo(IFSelect_ReturnStatus.IFSelect_RetDone));
        Assert.That(File.Exists(file), Is.True);
      }
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }
}
