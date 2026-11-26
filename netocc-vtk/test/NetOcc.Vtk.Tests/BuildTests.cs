// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// NUnit
using NUnit.Framework;

//
using OCC.Core.IVtk;
using Vtk;

namespace NetOcc.Vtk.Tests;

/// <summary>
/// TKIVtk and NetOccVtk run on NetVtk's VTK libraries: compiled against the same VTK build
/// (version, NetVtk's port-version, id width) as the one that loaded.
/// </summary>
[TestFixture]
public class BuildTests
{
  [Test]
  public void Vtk_IsTheBuildTKIVtkWasCompiledAgainst()
  {
    // Act
    var compiled = IVtkModule.NetOccVtk_VtkBuild();

    // Assert
    Assert.That(
      compiled, Is.EqualTo(VtkBuild.Current).And.StartWith(vtkVersion.GetVTKVersion() + "#"));
  }

  [Test]
  public void IdType_IsSixtyFourBitsWide()
  {
    // Act (NetVtk maps vtkIdType to long everywhere: VTK_USE_64BIT_IDS on every architecture)
    var size = IVtkModule.NetOccVtk_IdTypeSize();

    // Assert
    Assert.That(size, Is.EqualTo(8));
  }
}
