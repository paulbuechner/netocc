// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// NUnit
using NUnit.Framework;

//
using OCC.Core.BRepPrimAPI;
using OCC.Core.IVtkOCC;
using OCC.Core.IVtkTools;
using Vtk;

namespace NetOcc.Vtk.PackageTests;

/// <summary>
/// The packed NetOcc.Vtk as an app consumes it: its natives, NetOcc's and NetVtk's from their
/// runtime packages, loading together.
/// </summary>
[TestFixture]
public class PackageTests
{
  [OneTimeSetUp]
  public void RouteVtkOutput() => VtkOutput.Install();

  [Test]
  public void ShapeDataSource_MeshesAShape()
  {
    // Arrange
    using var source = new IVtkTools_ShapeDataSource();
    source.SetShape(new IVtkOCC_Shape(new BRepPrimAPI_MakeBox(1, 2, 3).Shape()));

    // Act
    source.Update();

    // Assert
    using var output = source.GetOutput();
    Assert.That(output.GetNumberOfCells(), Is.Positive);
  }

  [Test]
  public void Vtk_IsNetVtksBuild()
  {
    // Act
    var version = vtkVersion.GetVTKVersion();

    // Assert
    Assert.That(version, Is.EqualTo("9.7.1"));
  }
}
