// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

//
using OCC.Core.BRepAlgoAPI;
using OCC.Core.BRepPrimAPI;
using OCC.Core.gp;
using OCC.Core.Quantity;
using OCC.Core.STEPCAFControl;
using OCC.Core.TDataStd;
using OCC.Core.TDocStd;
using OCC.Core.TopLoc;
using OCC.Core.TopoDS;
using OCC.Core.XCAFApp;
using OCC.Core.XCAFDoc;

// static usings
using static OCC.Core.IFSelect.IFSelect_ReturnStatus;
using static OCC.Core.Quantity.Quantity_TypeOfColor;
using static OCC.Core.XCAFDoc.XCAFDoc_ColorType;

namespace NetOcc.Viewer;

/// <summary>
/// A small colored assembly, written as STEP: a plate with four holes and a bolt in each.
/// </summary>
public static class SampleModel
{
  private const double Width = 120, Depth = 80, Thickness = 10, HoleRadius = 5, Inset = 15;

  /// <summary>Writes the assembly to <paramref name="path"/> with its colors and names.</summary>
  public static void Write(string path)
  {
    var application = XCAFApp_Application.GetApplication();
    TDocStd_Document? document = null;
    application.NewDocument("MDTV-XCAF", ref document);
    try
    {
      var shapes = XCAFDoc_DocumentTool.ShapeTool(document!.Main());
      var colors = XCAFDoc_DocumentTool.ColorTool(document.Main());
      var plate = shapes.AddShape(Plate(), false);
      var bolt = shapes.AddShape(Bolt(), false);
      var assembly = shapes.NewShape();
      shapes.AddComponent(assembly, plate, new TopLoc_Location());
      foreach (var (x, y) in HoleCenters())
      {
        shapes.AddComponent(assembly, bolt, Translation(x, y, 0));
      }

      shapes.UpdateAssemblies();
      TDataStd_Name.Set(assembly, "bolted plate");
      TDataStd_Name.Set(plate, "plate");
      TDataStd_Name.Set(bolt, "bolt");
      colors.SetColor(plate, new Quantity_Color(0.27, 0.51, 0.71, Quantity_TOC_sRGB),
                      XCAFDoc_ColorSurf);
      colors.SetColor(bolt, new Quantity_Color(0.85, 0.65, 0.13, Quantity_TOC_sRGB),
                      XCAFDoc_ColorSurf);

      using var writer = new STEPCAFControl_Writer();
      writer.SetColorMode(true);
      writer.SetNameMode(true);
      if (!writer.Transfer(document) || writer.Write(path) != IFSelect_RetDone)
      {
        throw new InvalidOperationException($"OCCT couldn't write {path}.");
      }
    }
    finally
    {
      application.Close(document);
    }
  }

  private static (double X, double Y)[] HoleCenters() =>
  [
    (Inset, Inset), (Width - Inset, Inset), (Width - Inset, Depth - Inset), (Inset, Depth - Inset)
  ];

  private static TopoDS_Shape Plate()
  {
    var plate = new BRepPrimAPI_MakeBox(Width, Depth, Thickness).Shape();
    foreach (var (x, y) in HoleCenters())
    {
      var hole = new BRepPrimAPI_MakeCylinder(new gp_Ax2(new gp_Pnt(x, y, -1), new gp_Dir(0, 0, 1)),
                                              HoleRadius, Thickness + 2).Shape();
      plate = new BRepAlgoAPI_Cut(plate, hole).Shape();
    }

    return plate;
  }

  // at the origin, its head on top of the plate
  private static TopoDS_Shape Bolt()
  {
    var shank = new BRepPrimAPI_MakeCylinder(new gp_Ax2(new gp_Pnt(0, 0, -8), new gp_Dir(0, 0, 1)),
                                             HoleRadius - 0.5, Thickness + 8).Shape();
    var head =
      new BRepPrimAPI_MakeCylinder(new gp_Ax2(new gp_Pnt(0, 0, Thickness), new gp_Dir(0, 0, 1)),
                                   HoleRadius + 3, 5).Shape();
    return new BRepAlgoAPI_Fuse(shank, head).Shape();
  }

  private static TopLoc_Location Translation(double x, double y, double z)
  {
    var translation = new gp_Trsf();
    translation.SetTranslation(new gp_Vec(x, y, z));
    return new TopLoc_Location(translation);
  }
}
