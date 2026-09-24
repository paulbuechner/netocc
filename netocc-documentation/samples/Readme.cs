// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using OCC.Core.BRepAlgoAPI;
using OCC.Core.BRepGProp;
using OCC.Core.BRepPrimAPI;
using OCC.Core.GProp;
using OCC.Core.gp;
using OCC.Core.STEPControl;

namespace FusedPart;

internal static class Program
{
  private static void Main()
  {
    // a 10 x 20 x 30 box, and a cylinder of radius 3 standing in it
    var box = new BRepPrimAPI_MakeBox(10, 20, 30).Shape();
    var axis = new gp_Ax2(new gp_Pnt(5, 5, 0), new gp_Dir(0, 0, 1));
    var cylinder = new BRepPrimAPI_MakeCylinder(axis, 3, 40).Shape();

    // fused into one solid
    var fused = new BRepAlgoAPI_Fuse(box, cylinder).Shape();

    // its volume
    var properties = new GProp_GProps();
    BRepGProp.VolumeProperties(fused, properties);
    Console.WriteLine($"volume: {properties.Mass():F1}");

    // written as STEP
    var writer = new STEPControl_Writer();
    writer.Transfer(fused, STEPControl_StepModelType.STEPControl_AsIs);
    writer.Write("fused.step");
  }
}
