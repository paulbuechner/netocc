// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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
/// runtime packages, loading together. build.py test-package runs them for one architecture (a
/// RuntimeIdentifier), then again with NetOccModules and NetVtkModules without the modules
/// TKIVtk's imports belong to.
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

#if NETFRAMEWORK
  [Test]
  public void Natives_LieInTheProcessArchitecturesFolderOnly()
  {
    // Arrange
    var directory = AppDomain.CurrentDomain.BaseDirectory;

    // Act
    var copies = Directory.GetFiles(directory, "NetOccVtk.dll", SearchOption.AllDirectories);

    // Assert
    Assert.That(copies, Is.EqualTo(new[] { Path.Combine(NativeDirectory(), "NetOccVtk.dll") }));
  }
#endif

#if NETOCC_MODULES
  [Test]
  public void Natives_KeepWhatTheyLoadOfModulesLeftOut()
  {
    // Arrange
    var visualization = new Regex(@"^(?:lib)?NetOccVisualization\.");
    var v3d = new Regex(@"^(?:lib)?TKV3d\.");

    // Act
    var files = Directory.GetFiles(NativeDirectory()).Select(Path.GetFileName).ToList();

    // Assert: TKIVtk imports Visualization's TKV3d, which NetOccModules leaves out
    using (Assert.EnterMultipleScope())
    {
      Assert.That(files, Has.Some.Matches(v3d));
      Assert.That(files, Has.None.Matches(visualization));
    }
  }
#endif

  // where the natives are: x64\ or x86\ on .NET Framework, next to the app on .NET (a RID)
  private static string NativeDirectory()
  {
#if NETFRAMEWORK
    var architecture = IntPtr.Size == 8 ? "x64" : "x86";
    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, architecture);
#else
    return AppContext.BaseDirectory;
#endif
  }
}
