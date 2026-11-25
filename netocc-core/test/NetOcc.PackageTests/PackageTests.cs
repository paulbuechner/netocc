// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

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
/// x64\ or x86\ on .NET Framework) and resolve their OCCT dependencies there. build.py
/// test-package runs them for one architecture (a RuntimeIdentifier), then again with
/// NetOccModules.
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

  [Test]
  public void StepReader_ReadsTheWrittenBox()
  {
    // Arrange
    var directory = Path.Combine(Path.GetTempPath(), $"netocc-package-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    var file = Path.Combine(directory, "box.step");
    var writer = new STEPControl_Writer();
    writer.Transfer(new BRepPrimAPI_MakeBox(10.0, 20.0, 30.0).Shape(),
                    STEPControl_StepModelType.STEPControl_AsIs);
    writer.Write(file);
    var reader = new STEPControl_Reader();
    var properties = new GProp_GProps();

    try
    {
      // Act
      var read = reader.ReadFile(file);
      reader.TransferRoots();
      BRepGProp.VolumeProperties(reader.OneShape(), properties);

      // Assert
      using (Assert.EnterMultipleScope())
      {
        Assert.That(read, Is.EqualTo(IFSelect_ReturnStatus.IFSelect_RetDone));
        Assert.That(properties.Mass(), Is.EqualTo(6000.0).Within(1e-6));
      }
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

#if NETFRAMEWORK
  [Test]
  public void Natives_LieInTheProcessArchitecturesFolderOnly()
  {
    // Arrange
    var directory = AppDomain.CurrentDomain.BaseDirectory;

    // Act
    var copies = Directory.GetFiles(directory, "NetOccRuntime.dll", SearchOption.AllDirectories);

    // Assert
    Assert.That(copies, Is.EqualTo(new[] { Path.Combine(NativeDirectory(), "NetOccRuntime.dll") }));
  }
#endif

#if NETOCC_MODULES
  [Test]
  public void Natives_HoldTheSelectedModulesLibrariesOnly()
  {
    // Arrange
    var modules = typeof(PackageTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
      .Single(attribute => attribute.Key == "NetOccModules").Value!.Split(';');
    // a module's library on any system, not the managed NetOcc.dll
    var library = new Regex(@"^(?:lib)?NetOcc([A-Za-z0-9]+)\.(?:dll|so|dylib)$");

    // Act
    var libraries = Directory.GetFiles(NativeDirectory())
      .Select(file => library.Match(Path.GetFileName(file)))
      .Where(match => match.Success)
      .Select(match => match.Groups[1].Value);

    // Assert
    Assert.That(libraries, Is.EquivalentTo(modules.Append("Runtime")));
  }

  [Test]
  public void Natives_LeaveOutWhatOnlyModulesLeftOutLoad()
  {
    // Arrange: Visualization's OpenGL driver and mesh presentations, which build.py's selection
    // leaves out and none of its modules loads
    var visualization = new Regex(@"^(?:lib)?TK(?:OpenGl|MeshVS)\.");

    // Act
    var files = Directory.GetFiles(NativeDirectory()).Select(Path.GetFileName).ToList();

    // Assert
    Assert.That(files, Has.None.Matches(visualization));
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
