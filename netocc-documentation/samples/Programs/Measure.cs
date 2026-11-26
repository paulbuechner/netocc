// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

#region program
using System;
using OCC.Core.Bnd;
using OCC.Core.BRepAlgoAPI;
using OCC.Core.BRepBndLib;
using OCC.Core.BRepGProp;
using OCC.Core.BRepPrimAPI;
using OCC.Core.GProp;
using OCC.Core.gp;

namespace Measure;

// a flanged shaft's volume, weight in steel, surface area, centre of mass and height
internal static class Program
{
  private static void Main()
  {
    // a shaft of radius 10, 80 long, on a flange of radius 25, 10 thick (mm)
    var axis = new gp_Ax2(new gp_Pnt(0, 0, 0), new gp_Dir(0, 0, 1));
    var shaft = new BRepPrimAPI_MakeCylinder(axis, 10, 80).Shape();
    var flange = new BRepPrimAPI_MakeCylinder(axis, 25, 10).Shape();
    var part = new BRepAlgoAPI_Fuse(shaft, flange).Shape();

    // volume and surface properties: Mass() is the volume, the area
    var volume = new GProp_GProps();
    BRepGProp.VolumeProperties(part, volume);
    var surface = new GProp_GProps();
    BRepGProp.SurfaceProperties(part, surface);

    // the exact bounding box, a struct filled through a ref
    var box = new Bnd_Box();
    BRepBndLib.AddOptimal(part, ref box, false, false);

    var cm3 = volume.Mass() / 1000;
    Console.WriteLine($"volume: {cm3:F1} cm³, {cm3 * 7.85:F0} g in steel");
    Console.WriteLine($"surface: {surface.Mass() / 100:F1} cm²");
    Console.WriteLine($"centre of mass: {volume.CentreOfMass().Z():F2} mm up the axis");
    Console.WriteLine($"height: {box.CornerMax().Z() - box.CornerMin().Z():F1} mm");
  }
}
#endregion
