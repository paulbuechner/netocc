// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Linq;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BinXCAFDrivers;
using OCC.Core.gp;
using OCC.Core.IFSelect;
using OCC.Core.Quantity;
using OCC.Core.STEPCAFControl;
using OCC.Core.TDataStd;
using OCC.Core.TDF;
using OCC.Core.TDocStd;
using OCC.Core.TopLoc;
using OCC.Core.XCAFDoc;
using OCC.Core.XmlXCAFDrivers;

// static usings
using static OCC.Core.PCDM.PCDM_ReaderStatus;
using static OCC.Core.PCDM.PCDM_StoreStatus;
using static OCC.Core.Quantity.Quantity_TypeOfColor;
using static OCC.Core.XCAFDoc.XCAFDoc_ColorType;

namespace NetOcc.Tests;

/// <summary>
/// An XDE document on an application of its own instead of XCAFApp's process-wide one: the labels
/// XDE takes, BinXCAF and XmlXCAF through streams, XDE edits under undo, STEP through streams.
/// </summary>
[TestFixture]
public class XcafDocumentTests
{
  private static readonly double CylinderVolume = Math.PI * 3.0 * 3.0 * 40.0;

  private TDocStd_Application _application = null!;
  private TDocStd_Document _document = null!;
  private XCAFDoc_ShapeTool _shapes = null!;

  [SetUp]
  public void CreateDocument()
  {
    _application = new TDocStd_Application();
    BinXCAFDrivers.DefineFormat(_application);
    XmlXCAFDrivers.DefineFormat(_application);
    _document = NewDocument("BinXCAF");
    _shapes = XCAFDoc_DocumentTool.ShapeTool(_document.Main());
  }

  [TearDown]
  public void CloseDocuments()
  {
    _shapes.Dispose();
    while (_application.NbDocuments() > 0)
    {
      using var document = _application.GetDocument(1);
      _application.Close(document);
    }

    _document.Dispose();
    _application.Dispose();
  }

  [Test]
  public void Set_KeepsXdeUnderTheMainLabel()
  {
    // Arrange
    WriteSampleAssembly(_document);
    var root = _document.GetData().Root();

    // Act
    var topLevel = root.NbChildren();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(XCAFDoc_DocumentTool.IsXCAFDocument(_document), Is.True);
      Assert.That(topLevel, Is.EqualTo(1), "labels beside 0:1");
      Assert.That(Ocaf.Entry(XCAFDoc_DocumentTool.ShapesLabel(_document.Main())),
                  Is.EqualTo("0:1:1"));
      Assert.That(Ocaf.Entry(XCAFDoc_DocumentTool.ColorsLabel(_document.Main())),
                  Is.EqualTo("0:1:2"));
    }
  }

  [TestCase("BinXCAF")]
  [TestCase("XmlXCAF")]
  public void Open_FromAStream_RestoresPartsAssembliesNamesAndColors(string format)
  {
    // Arrange
    var saved = format == "BinXCAF" ? _document : NewDocument(format);
    WriteSampleAssembly(saved);
    using var stream = new MemoryStream();
    if (_application.SaveAs(saved, stream) != PCDM_SS_OK)
    {
      throw new InvalidOperationException($"SaveAs {format} failed");
    }

    stream.Position = 0;
    TDocStd_Document? document = null;

    // Act
    var status = _application.Open(stream, ref document);

    // Assert
    Assert.That(status, Is.EqualTo(PCDM_RS_OK));
    AssertSampleAssembly(document!);
  }

  [Test]
  public void Undo_OfAReplacedPartShape_RestoresThePartAndItsAssembly()
  {
    // Arrange
    _document.SetUndoLimit(10);
    _document.OpenCommand();
    var part = _shapes.NewShape();
    _shapes.SetShape(part, Shapes.Box());
    var assembly = _shapes.NewShape();
    _shapes.AddComponent(assembly, part, Translation(0, 0, 0));
    _shapes.UpdateAssemblies();
    _document.CommitCommand();
    _document.OpenCommand();
    _shapes.SetShape(part, Shapes.Cylinder());
    _shapes.UpdateAssemblies();
    _document.CommitCommand();

    // Act
    _document.Undo();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(Shapes.Volume(XCAFDoc_ShapeTool.GetShape(part)),
                  Is.EqualTo(Shapes.BoxVolume).Within(1e-6));
      Assert.That(Shapes.Volume(XCAFDoc_ShapeTool.GetShape(assembly)),
                  Is.EqualTo(Shapes.BoxVolume).Within(1e-6));
    }
  }

  [Test]
  public void Undo_OfAddedParts_LeavesNoShapeBehind()
  {
    // Arrange
    var box = Shapes.Box();
    _document.SetUndoLimit(10);
    _document.OpenCommand();
    _shapes.AddShape(box, false);
    _document.CommitCommand();

    // Act
    _document.Undo();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(Ocaf.Labels(_shapes.GetFreeShapes), Is.Empty);
      Assert.That(_shapes.FindShape(box).IsNull(), Is.True);
    }
  }

  [Test]
  public void Redo_OfAddedParts_RegistersThemAgain()
  {
    // Arrange
    var box = Shapes.Box();
    _document.SetUndoLimit(10);
    _document.OpenCommand();
    var label = _shapes.AddShape(box, false);
    _document.CommitCommand();
    _document.Undo();

    // Act
    _document.Redo();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(Ocaf.Labels(_shapes.GetFreeShapes), Is.EqualTo(new[] { label }));
      Assert.That(_shapes.FindShape(box), Is.EqualTo(label));
    }
  }

  [Test]
  public void ReadStream_OfWhatWriteStreamWrote_KeepsStructureNamesAndColors()
  {
    // Arrange
    WriteSampleAssembly(_document);
    using var stream = new MemoryStream();
    var writer = new STEPCAFControl_Writer();
    writer.SetColorMode(true);
    writer.SetNameMode(true);
    if (!writer.Transfer(_document)
        || writer.WriteStream(stream) != IFSelect_ReturnStatus.IFSelect_RetDone)
    {
      throw new InvalidOperationException("STEP export failed");
    }

    stream.Position = 0;
    var imported = NewDocument("BinXCAF");
    var reader = new STEPCAFControl_Reader();
    reader.SetColorMode(true);
    reader.SetNameMode(true);

    // Act
    var read = reader.ReadStream("assembly.step", stream);
    var transferred = reader.Transfer(imported);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(read, Is.EqualTo(IFSelect_ReturnStatus.IFSelect_RetDone));
      Assert.That(transferred, Is.True);
    }

    AssertSampleAssembly(imported);
  }

  private TDocStd_Document NewDocument(string format)
  {
    TDocStd_Document? document = null;
    _application.NewDocument(format, ref document);
    XCAFDoc_DocumentTool.Set(document!.Main(), false);
    return document;
  }

  // an assembly of a blue box "plate" and a red cylinder "bolt" on top of it
  private static void WriteSampleAssembly(TDocStd_Document document)
  {
    using var shapes = XCAFDoc_DocumentTool.ShapeTool(document.Main());
    using var colors = XCAFDoc_DocumentTool.ColorTool(document.Main());
    var plate = shapes.AddShape(Shapes.Box(), false);
    var bolt = shapes.AddShape(Shapes.Cylinder(), false);
    var assembly = shapes.NewShape();
    shapes.AddComponent(assembly, plate, Translation(0, 0, 0));
    shapes.AddComponent(assembly, bolt, Translation(0, 0, 30));
    shapes.UpdateAssemblies();
    TDataStd_Name.Set(assembly, "assembly");
    TDataStd_Name.Set(plate, "plate");
    TDataStd_Name.Set(bolt, "bolt");
    colors.SetColor(plate, new Quantity_Color(0, 0, 1, Quantity_TOC_RGB), XCAFDoc_ColorSurf);
    colors.SetColor(bolt, new Quantity_Color(1, 0, 0, Quantity_TOC_RGB), XCAFDoc_ColorSurf);
  }

  private static void AssertSampleAssembly(TDocStd_Document document)
  {
    using var shapes = XCAFDoc_DocumentTool.ShapeTool(document.Main());
    var roots = Ocaf.Labels(shapes.GetFreeShapes);
    Assert.That(roots, Has.Count.EqualTo(1));
    var parts = Ocaf.Labels(sequence => XCAFDoc_ShapeTool.GetComponents(roots[0], sequence))
      .Select(Referred)
      .ToList();
    var colors = parts.ToDictionary(part => Ocaf.Name(part) ?? "",
                                    part => Color(part, XCAFDoc_ColorSurf));
    using (Assert.EnterMultipleScope())
    {
      Assert.That(XCAFDoc_ShapeTool.IsAssembly(roots[0]), Is.True);
      Assert.That(Ocaf.Name(roots[0]), Is.EqualTo("assembly"));
      Assert.That(colors, Does.ContainKey("plate").WithValue(new[] { 0.0, 0.0, 1.0 }));
      Assert.That(colors, Does.ContainKey("bolt").WithValue(new[] { 1.0, 0.0, 0.0 }));
      Assert.That(Shapes.Volume(XCAFDoc_ShapeTool.GetShape(roots[0])),
                  Is.EqualTo(Shapes.BoxVolume + CylinderVolume).Within(1e-3));
    }
  }

  private static TopLoc_Location Translation(double x, double y, double z)
  {
    var trsf = new gp_Trsf();
    trsf.SetTranslation(new gp_Vec(x, y, z));
    return new TopLoc_Location(trsf);
  }

  private static TDF_Label Referred(TDF_Label component)
  {
    var referred = new TDF_Label();
    XCAFDoc_ShapeTool.GetReferredShape(component, referred);
    return referred;
  }

  // red, green, blue; null without a color
  private static double[]? Color(TDF_Label label, XCAFDoc_ColorType type)
  {
    var color = new Quantity_Color();
    return XCAFDoc_ColorTool.GetColor(label, type, ref color)
      ? [color.Red(), color.Green(), color.Blue()]
      : null;
  }
}
