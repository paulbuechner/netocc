// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BRep;
using OCC.Core.BRepAlgoAPI;
using OCC.Core.BRepPrimAPI;
using OCC.Core.Geom;
using OCC.Core.gp;
using OCC.Core.TopoDS;
using OCC.Core.TopTools;

// static usings
using static OCC.Core.TopAbs.TopAbs_ShapeEnum;

namespace NetOcc.Tests;

/// <summary>
/// Forces collections while native objects are shared, to flush out lifetime bugs.
/// </summary>
[TestFixture]
public class StressTests
{
  private static TopoDS_Shape OverlappingCubes() => new BRepAlgoAPI_Fuse(
    new BRepPrimAPI_MakeBox(1, 1, 1).Shape(),
    new BRepPrimAPI_MakeBox(new gp_Pnt(0.5, 0.5, 0.5), 1, 1, 1).Shape()).Shape();

  [Test]
  public void GcPressure_TemporariesAndFinalizersKeepShapesValid()
  {
    // Arrange
    const int iterations = 60;
    List<int> planeCounts = [];

    // Act
    for (var i = 0; i < iterations; i++)
    {
      // the MakeBox/Fuse proxies die right away; Shape() returned owned copies
      var fused = OverlappingCubes();
      Garbage.Collect();
      planeCounts.Add(Shapes.SubShapes(fused, TopAbs_FACE)
                        .Count(face =>
                                 Geom_Plane.DownCast(BRep_Tool.Surface(TopoDS.Face(face))) is not
                                   null));
    }

    var volume = Shapes.Volume(OverlappingCubes());

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(planeCounts, Has.Count.EqualTo(iterations));
      Assert.That(planeCounts, Is.All.GreaterThanOrEqualTo(12));
      Assert.That(volume, Is.EqualTo(1.0 + 1.0 - 0.125).Within(1e-9));
    }
  }

  [Test]
  public void List_OwnsCopiesOfAppendedShapes()
  {
    // Arrange
    var list = new TopTools_ListOfShape();
    for (var i = 0; i < 20; i++)
    {
      list.Append(new BRepPrimAPI_MakeBox(1 + i, 1, 1).Shape());
    }

    Garbage.Collect();

    // Act
    var volumes = list.Select(Shapes.Volume).ToList();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(list.Count, Is.EqualTo(20));
      Assert.That(volumes, Is.EqualTo(Enumerable.Range(1, 20).Select(v => (double)v)).Within(1e-9));
    }
  }
}
