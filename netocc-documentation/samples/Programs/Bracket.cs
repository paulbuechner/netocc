// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

#region program
using System;
using OCC.Core.BRepAlgoAPI;
using OCC.Core.BRepBuilderAPI;
using OCC.Core.BRepCheck;
using OCC.Core.BRepPrimAPI;
using OCC.Core.gp;
using OCC.Core.TopAbs;
using OCC.Core.TopExp;
using OCC.Core.TopTools;

namespace Bracket;

// an L-shaped bracket: a profile extruded, a hole drilled through each leg
internal static class Program
{
  private static void Main()
  {
    // the L in the XZ plane (mm): a 60 long foot and a 40 high upright, both 8 thick
    var outline = new BRepBuilderAPI_MakePolygon();
    foreach (var (x, z) in new[] { (0, 0), (60, 0), (60, 8), (8, 8), (8, 40), (0, 40) })
    {
      outline.Add(new gp_Pnt(x, 0, z));
    }

    outline.Close();
    var profile = new BRepBuilderAPI_MakeFace(outline.Wire()).Face();

    // extruded 30 along Y
    var bracket = new BRepPrimAPI_MakePrism(profile, new gp_Vec(0, 30, 0)).Shape();

    // a hole of radius 4 down through the foot, and one across through the upright
    var down = new gp_Ax2(new gp_Pnt(40, 15, -1), new gp_Dir(0, 0, 1));
    var across = new gp_Ax2(new gp_Pnt(-1, 15, 26), new gp_Dir(1, 0, 0));
    foreach (var axis in new[] { down, across })
    {
      var hole = new BRepPrimAPI_MakeCylinder(axis, 4, 10).Shape();
      bracket = new BRepAlgoAPI_Cut(bracket, hole).Shape();
    }

    // its faces, and whether OCCT's checker finds the solid valid
    var faces = new TopTools_IndexedMapOfShape();
    TopExp.MapShapes(bracket, TopAbs_ShapeEnum.TopAbs_FACE, faces);
    var valid = new BRepCheck_Analyzer(bracket).IsValid();
    Console.WriteLine($"bracket: {faces.Extent()} faces, valid: {valid}");
  }
}
#endregion
