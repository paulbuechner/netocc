// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

#region program
using System;
using OCC.Core.BRepMesh;
using OCC.Core.BRepPrimAPI;
using OCC.Core.IFSelect;
using OCC.Core.STEPControl;
using OCC.Core.StlAPI;

namespace Exchange;

// a part written as STEP, read back, and written as binary STL
internal static class Program
{
  private static void Main()
  {
    // a cylinder of radius 10, 30 high (mm)
    var part = new BRepPrimAPI_MakeCylinder(10, 30).Shape();

    var writer = new STEPControl_Writer();
    writer.Transfer(part, STEPControl_StepModelType.STEPControl_AsIs);
    writer.Write("part.step");
    Console.WriteLine("wrote part.step");

    // every root entity of the file: one shape, or a compound of several
    var reader = new STEPControl_Reader();
    if (reader.ReadFile("part.step") != IFSelect_ReturnStatus.IFSelect_RetDone)
    {
      Console.Error.WriteLine("can't read part.step");
      return;
    }

    reader.TransferRoots();
    var read = reader.OneShape();

    // STL holds triangles: mesh to 0.1 mm first
    new BRepMesh_IncrementalMesh(read, 0.1);
    var stl = new StlAPI_Writer();
    stl.ASCIIMode() = false;
    stl.Write(read, "part.stl");
    Console.WriteLine("wrote part.stl");
  }
}
#endregion
