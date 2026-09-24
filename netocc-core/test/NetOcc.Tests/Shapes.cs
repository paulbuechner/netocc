// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

//
using OCC.Core.BRepAlgoAPI;
using OCC.Core.BRepGProp;
using OCC.Core.BRepGraph;
using OCC.Core.BRepPrimAPI;
using OCC.Core.gp;
using OCC.Core.GProp;
using OCC.Core.TopAbs;
using OCC.Core.TopExp;
using OCC.Core.TopoDS;

namespace NetOcc.Tests;

/// <summary>
/// Box 10x20x30 at the origin (also as a BRepGraph) and a cylinder R3 H40 standing at (5, 5, 0).
/// </summary>
internal static class Shapes
{
  public const double BoxVolume = 10.0 * 20.0 * 30.0;
  public static readonly double FusedVolume = BoxVolume + Math.PI * 3.0 * 3.0 * 10.0;
  public static readonly double CutVolume = BoxVolume - Math.PI * 3.0 * 3.0 * 30.0;

  public static TopoDS_Shape Box() => new BRepPrimAPI_MakeBox(10.0, 20.0, 30.0).Shape();

  public static BRepGraph BoxGraph()
  {
    var graph = new BRepGraph();
    graph.Shapes().Add(Box());
    return graph;
  }

  public static TopoDS_Shape Cylinder() =>
    new BRepPrimAPI_MakeCylinder(new gp_Ax2(new gp_Pnt(5, 5, 0), new gp_Dir(0, 0, 1)), 3.0, 40.0)
      .Shape();

  public static TopoDS_Shape Fused()
  {
    var fuse = new BRepAlgoAPI_Fuse(Box(), Cylinder());
    return fuse.HasErrors() ? throw new InvalidOperationException("fuse failed") : fuse.Shape();
  }

  public static List<TopoDS_Shape> SubShapes(TopoDS_Shape shape, TopAbs_ShapeEnum type)
  {
    List<TopoDS_Shape> result = [];
    for (var explorer = new TopExp_Explorer(shape, type); explorer.More(); explorer.Next())
    {
      result.Add(explorer.Current());
    }

    return result;
  }

  public static double Volume(TopoDS_Shape shape)
  {
    var props = new GProp_GProps();
    BRepGProp.VolumeProperties(shape, props);
    return props.Mass();
  }
}
