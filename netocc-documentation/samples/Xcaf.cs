// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.IO;
using System.Linq;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BinXCAFDrivers;
using OCC.Core.BRep;
using OCC.Core.BRepGProp;
using OCC.Core.BRepPrimAPI;
using OCC.Core.gp;
using OCC.Core.GProp;
using OCC.Core.IFSelect;
using OCC.Core.PCDM;
using OCC.Core.Quantity;
using OCC.Core.STEPCAFControl;
using OCC.Core.STEPControl;
using OCC.Core.TDataStd;
using OCC.Core.TDF;
using OCC.Core.TDocStd;
using OCC.Core.TNaming;
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

  #region own-application
  /// <summary>
  /// An XDE document on an application of its own, with undo, saved to a stream as BinXCAF: the
  /// format that keeps the assembly structure, names and colors.
  /// </summary>
  public static byte[] SaveOwnDocument()
  {
    var application = new TDocStd_Application();
    BinXCAFDrivers.DefineFormat(application);
    TDocStd_Document? document = null;
    application.NewDocument("BinXCAF", ref document);
    try
    {
      XCAFDoc_DocumentTool.Set(document!.Main(), false);
      document.SetUndoLimit(100);

      document.OpenCommand();
      var shapes = XCAFDoc_DocumentTool.ShapeTool(document.Main());
      TDataStd_Name.Set(shapes.AddShape(new BRepPrimAPI_MakeBox(100, 60, 4).Shape(), false),
                        "plate");
      document.CommitCommand();

      using var stream = new MemoryStream();
      if (application.SaveAs(document, stream) != PCDM_StoreStatus.PCDM_SS_OK)
      {
        throw new IOException("saving the document failed");
      }

      return stream.ToArray();
    }
    finally
    {
      application.Close(document);
    }
  }
  #endregion

  #region update-assembly
  /// <summary>
  /// A part's new shape in the compounds of the assemblies that place it, and of those above them:
  /// <c>UpdateAssemblies</c> does it for every assembly of the document.
  /// </summary>
  public static void UpdateAssembliesAbove(TDF_Label shape)
  {
    // the assemblies whose components place the shape, each once
    var users = new TDF_LabelSequence();
    XCAFDoc_ShapeTool.GetUsers(shape, users);
    var assemblies = new List<TDF_Label>();
    foreach (var component in users)
    {
      var assembly = component.Father();
      if (!assemblies.Contains(assembly))
      {
        assemblies.Add(assembly);
      }
    }

    foreach (var assembly in assemblies)
    {
      // an assembly's shape is the compound of its components' shapes, placed
      var compound = new TopoDS_Compound();
      var builder = new BRep_Builder();
      builder.MakeCompound(compound);
      var components = new TDF_LabelSequence();
      XCAFDoc_ShapeTool.GetComponents(assembly, components);
      foreach (var component in components)
      {
        builder.Add(compound, XCAFDoc_ShapeTool.GetShape(component));
      }

      new TNaming_Builder(assembly).Generated(compound);
      UpdateAssembliesAbove(assembly);
    }
  }
  #endregion

  [Test]
  public void SaveOwnDocument_OpensWithItsPart()
  {
    // Arrange
    var application = new TDocStd_Application();
    BinXCAFDrivers.DefineFormat(application);
    using var stream = new MemoryStream(SaveOwnDocument());
    TDocStd_Document? document = null;

    // Act
    var status = application.Open(stream, ref document);

    // Assert
    try
    {
      var roots = new TDF_LabelSequence();
      XCAFDoc_DocumentTool.ShapeTool(document!.Main()).GetFreeShapes(roots);
      using (Assert.EnterMultipleScope())
      {
        Assert.That(status, Is.EqualTo(PCDM_ReaderStatus.PCDM_RS_OK));
        Assert.That(roots.Select(NameOf), Is.EqualTo(new[] { "plate" }));
      }
    }
    finally
    {
      application.Close(document);
    }
  }

  [Test]
  public void UpdateAssembliesAbove_PutsANewPartShapeIntoItsAssembly()
  {
    // Arrange: a plate on a post, in an assembly, then a post twice as tall
    var application = new TDocStd_Application();
    BinXCAFDrivers.DefineFormat(application);
    TDocStd_Document? document = null;
    application.NewDocument("BinXCAF", ref document);
    try
    {
      XCAFDoc_DocumentTool.Set(document!.Main(), false);
      var shapes = XCAFDoc_DocumentTool.ShapeTool(document.Main());
      var plate = shapes.AddShape(new BRepPrimAPI_MakeBox(100, 60, 4).Shape(), false);
      var post = shapes.AddShape(new BRepPrimAPI_MakeBox(6, 6, 40).Shape(), false);
      var table = shapes.NewShape();
      shapes.AddComponent(table, plate, new TopLoc_Location());
      shapes.AddComponent(table, post, new TopLoc_Location());
      shapes.UpdateAssemblies();
      shapes.SetShape(post, new BRepPrimAPI_MakeBox(6, 6, 80).Shape());

      // Act
      UpdateAssembliesAbove(post);

      // Assert
      Assert.That(Volume(XCAFDoc_ShapeTool.GetShape(table)),
                  Is.EqualTo(100 * 60 * 4 + 6 * 6 * 80).Within(1e-6));
    }
    finally
    {
      application.Close(document);
    }
  }

  private static double Volume(TopoDS_Shape shape)
  {
    var properties = new GProp_GProps();
    BRepGProp.VolumeProperties(shape, properties);
    return properties.Mass();
  }

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
