// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;

// NUnit
using NUnit.Framework;

//
#region usings
using OCC.Core.BRepAlgoAPI;
using OCC.Core.BRepFilletAPI;
using OCC.Core.BRepGProp;
using OCC.Core.BRepMesh;
using OCC.Core.BRepPrimAPI;
using OCC.Core.GProp;
using OCC.Core.gp;
using OCC.Core.STEPControl;
using OCC.Core.StlAPI;
using OCC.Core.TopAbs;
using OCC.Core.TopExp;
using OCC.Core.TopoDS;
using OCC.Core.TopTools;
#endregion

namespace NetOcc.Samples;

/// <summary>
/// Getting started: a part from primitives, measured and written as STEP and STL.
/// </summary>
[TestFixture]
public class GettingStarted : Files
{
  [Test]
  public void FirstPart()
  {
    // Act
    #region first-part
    // a 60 x 40 x 20 block with a hole through it
    var block = new BRepPrimAPI_MakeBox(60, 40, 20).Shape();
    var axis = new gp_Ax2(new gp_Pnt(30, 20, -1), new gp_Dir(0, 0, 1));
    var hole = new BRepPrimAPI_MakeCylinder(axis, 8, 22).Shape();
    var part = new BRepAlgoAPI_Cut(block, hole).Shape();

    // round every edge by 1.5, each once
    var edges = new TopTools_IndexedMapOfShape();
    TopExp.MapShapes(part, TopAbs_ShapeEnum.TopAbs_EDGE, edges);
    var fillet = new BRepFilletAPI_MakeFillet(part);
    foreach (var edge in edges)
    {
      fillet.Add(1.5, TopoDS.Edge(edge));
    }

    var rounded = fillet.Shape();

    // its volume, then the part as STEP and, triangulated, as STL
    var properties = new GProp_GProps();
    BRepGProp.VolumeProperties(rounded, properties);
    Console.WriteLine($"volume: {properties.Mass():F1} mm³");

    var step = new STEPControl_Writer();
    step.Transfer(rounded, STEPControl_StepModelType.STEPControl_AsIs);
    step.Write("part.step");

    new BRepMesh_IncrementalMesh(rounded, 0.1);
    new StlAPI_Writer().Write(rounded, "part.stl");
    #endregion

    // Assert (the block less the hole, less what the rounds take off)
    using (Assert.EnterMultipleScope())
    {
      Assert.That(properties.Mass(),
                  Is.LessThan(60 * 40 * 20 - Math.PI * 8 * 8 * 20).And.GreaterThan(40_000));
      Assert.That(File.Exists("part.step") && File.Exists("part.stl"), Is.True);
    }
  }
}
