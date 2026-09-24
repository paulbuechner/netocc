// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.IO;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BRepPrimAPI;
using OCC.Core.gp;
using OCC.Core.IFSelect;
using OCC.Core.Quantity;
using OCC.Core.STEPCAFControl;
using OCC.Core.STEPControl;
using OCC.Core.TDataStd;
using OCC.Core.TDF;
using OCC.Core.TDocStd;
using OCC.Core.TopLoc;
using OCC.Core.TopoDS;
using OCC.Core.XCAFApp;
using OCC.Core.XCAFDoc;

namespace NetOcc.Samples;

/// <summary>XCAF: assemblies with names and colors, written to and read from STEP.</summary>
[TestFixture]
public class Xcaf : Files
{
  #region write-assembly
  /// <summary>A table: a plate on four posts, one post shape placed four times.</summary>
  public static void WriteTable(string path)
  {
    var application = XCAFApp_Application.GetApplication();
    TDocStd_Document? document = null;
    application.NewDocument("MDTV-XCAF", ref document);
    try
    {
      var shapes = XCAFDoc_DocumentTool.ShapeTool(document!.Main());
      var colors = XCAFDoc_DocumentTool.ColorTool(document.Main());

      // the parts, each defined once
      var plate = shapes.AddShape(new BRepPrimAPI_MakeBox(100, 60, 4).Shape(), false);
      var post = shapes.AddShape(new BRepPrimAPI_MakeCylinder(3, 40).Shape(), false);
      TDataStd_Name.Set(plate, "plate");
      TDataStd_Name.Set(post, "post");
      colors.SetColor(plate, new Quantity_Color(Quantity_NameOfColor.Quantity_NOC_BURLYWOOD),
                      XCAFDoc_ColorType.XCAFDoc_ColorGen);
      colors.SetColor(post, new Quantity_Color(Quantity_NameOfColor.Quantity_NOC_GRAY40),
                      XCAFDoc_ColorType.XCAFDoc_ColorGen);

      // the assembly places them: components refer to the parts, with a location each
      var table = shapes.NewShape();
      TDataStd_Name.Set(table, "table");
      shapes.AddComponent(table, plate, new TopLoc_Location());
      foreach (var (x, y) in new[] { (8, 8), (92, 8), (8, 52), (92, 52) })
      {
        var below = new gp_Trsf();
        below.SetTranslation(new gp_Vec(x, y, -40));
        shapes.AddComponent(table, post, new TopLoc_Location(below));
      }

      shapes.UpdateAssemblies();

      var writer = new STEPCAFControl_Writer();
      writer.SetNameMode(true);
      writer.SetColorMode(true);
      if (!writer.Transfer(document, STEPControl_StepModelType.STEPControl_AsIs)
          || writer.Write(path) != IFSelect_ReturnStatus.IFSelect_RetDone)
      {
        throw new IOException($"writing {path} failed");
      }
    }
    finally
    {
      application.Close(document);
    }
  }
  #endregion

  #region read-assembly
  /// <summary>
  /// A part as the assembly places it: its name, its shape where it sits, its color.
  /// </summary>
  public record Part(string Name, TopoDS_Shape Shape, Quantity_Color? Color);

  public static List<Part> ReadParts(string path)
  {
    var application = XCAFApp_Application.GetApplication();
    TDocStd_Document? document = null;
    application.NewDocument("MDTV-XCAF", ref document);
    try
    {
      var reader = new STEPCAFControl_Reader();
      reader.SetNameMode(true);
      reader.SetColorMode(true);
      if (reader.ReadFile(path) != IFSelect_ReturnStatus.IFSelect_RetDone
          || !reader.Transfer(document))
      {
        throw new InvalidDataException($"{path} isn't a readable STEP file");
      }

      var parts = new List<Part>();
      var roots = new TDF_LabelSequence();
      XCAFDoc_DocumentTool.ShapeTool(document!.Main()).GetFreeShapes(roots);
      foreach (var root in roots)
      {
        Collect(root, new TopLoc_Location(), parts);
      }

      return parts;
    }
    finally
    {
      application.Close(document);
    }
  }

  private static void Collect(TDF_Label label, TopLoc_Location placement, List<Part> parts)
  {
    // a component refers to the shape it places, and adds its location
    var shape = label;
    if (XCAFDoc_ShapeTool.IsReference(label))
    {
      shape = new TDF_Label();
      XCAFDoc_ShapeTool.GetReferredShape(label, shape);
      placement = placement.Multiplied(XCAFDoc_ShapeTool.GetLocation(label));
    }

    if (XCAFDoc_ShapeTool.IsAssembly(shape))
    {
      var components = new TDF_LabelSequence();
      XCAFDoc_ShapeTool.GetComponents(shape, components);
      foreach (var component in components)
      {
        Collect(component, placement, parts);
      }

      return;
    }

    var color = new Quantity_Color();
    var colored = XCAFDoc_ColorTool.GetColor(shape, XCAFDoc_ColorType.XCAFDoc_ColorGen, ref color)
                  || XCAFDoc_ColorTool.GetColor(shape, XCAFDoc_ColorType.XCAFDoc_ColorSurf,
                                                ref color);
    parts.Add(new Part(NameOf(shape), XCAFDoc_ShapeTool.GetShape(shape).Moved(placement),
                       colored ? color : null));
  }

  private static string NameOf(TDF_Label label)
  {
    TDF_Attribute? attribute = null;
    return label.FindAttribute(TDataStd_Name.GetID(), ref attribute)
      ? TDataStd_Name.DownCast(attribute)!.Get()
      : "";
  }
  #endregion

  [Test]
  public void RoundTrip()
  {
    // Act
    WriteTable("table.step");
    var parts = ReadParts("table.step");

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(parts, Has.Count.EqualTo(5));
      Assert.That(parts.FindAll(p => p.Name == "post"), Has.Count.EqualTo(4));
      Assert.That(parts.Find(p => p.Name == "plate")?.Color?.Name(),
                  Is.EqualTo(Quantity_NameOfColor.Quantity_NOC_BURLYWOOD));
      Assert.That(
        parts.FindAll(p => p.Name == "post")
          .ConvertAll(p => p.Shape.Location().Transformation().TranslationPart().X()),
        Is.EquivalentTo(new[] { 8.0, 92, 8, 92 })
          .Using<double>((a, b) => System.Math.Abs(a - b) < 1e-9));
    }
  }
}
