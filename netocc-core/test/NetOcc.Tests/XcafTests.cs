// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Linq;

// NUnit
using NUnit.Framework;

//
using OCC.Core.gp;
using OCC.Core.IFSelect;
using OCC.Core.Quantity;
using OCC.Core.STEPCAFControl;
using OCC.Core.TDataStd;
using OCC.Core.TDF;
using OCC.Core.TDocStd;
using OCC.Core.TopLoc;
using OCC.Core.XCAFApp;
using OCC.Core.XCAFDoc;

// static usings
using static OCC.Core.Quantity.Quantity_TypeOfColor;
using static OCC.Core.XCAFDoc.XCAFDoc_ColorType;

namespace NetOcc.Tests;

/// <summary>
/// XDE on OCAF: shape tool, assemblies, colors, and STEP with structure, names and colors.
/// </summary>
[TestFixture]
public class XcafTests
{
  private XCAFApp_Application _application = null!;
  private TDocStd_Document _document = null!;
  private TDocStd_Document? _imported;
  private XCAFDoc_ShapeTool _shapes = null!;
  private XCAFDoc_ColorTool _colors = null!;
  private string _directory = null!;

  [SetUp]
  public void CreateDocument()
  {
    _application = XCAFApp_Application.GetApplication();
    _document = NewDocument();
    _shapes = XCAFDoc_DocumentTool.ShapeTool(_document.Main());
    _colors = XCAFDoc_DocumentTool.ColorTool(_document.Main());
    _directory = NonAscii.CreateTempDirectory();
  }

  // The XCAF application is a process-wide singleton: close only this test's documents.
  [TearDown]
  public void CloseDocuments()
  {
    _colors.Dispose();
    _shapes.Dispose();
    if (_imported is not null)
    {
      _application.Close(_imported);
      _imported.Dispose();
      _imported = null;
    }

    _application.Close(_document);
    _document.Dispose();
    _application.Dispose();
    Directory.Delete(_directory, true);
  }

  [Test]
  public void NewDocument_IsAnXcafDocument()
  {
    // Arrange
    var main = _document.Main();

    // Act
    var isXcaf = XCAFDoc_DocumentTool.IsXCAFDocument(_document);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(isXcaf, Is.True);
      Assert.That(_shapes.BaseLabel(), Is.EqualTo(XCAFDoc_DocumentTool.ShapesLabel(main)));
    }
  }

  [Test]
  public void AddShape_RegistersAFreeSimpleShape()
  {
    // Arrange
    var box = Shapes.Box();

    // Act
    var label = _shapes.AddShape(box, false);
    var free = Ocaf.Labels(_shapes.GetFreeShapes);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(XCAFDoc_ShapeTool.IsSimpleShape(label), Is.True);
      Assert.That(XCAFDoc_ShapeTool.GetShape(label).IsSame(box), Is.True);
      Assert.That(_shapes.FindShape(box), Is.EqualTo(label));
      Assert.That(free, Is.EqualTo(new[] { label }));
    }
  }

  [Test]
  public void AddComponent_BuildsAnAssemblyOfLocatedInstances()
  {
    // Arrange
    var part = _shapes.AddShape(Shapes.Box(), false);
    var assembly = _shapes.NewShape();

    // Act
    var first = _shapes.AddComponent(assembly, part, Translation(0, 0, 0));
    var second = _shapes.AddComponent(assembly, part, Translation(50, 0, 0));
    _shapes.UpdateAssemblies();
    var components = Ocaf.Labels(sequence => XCAFDoc_ShapeTool.GetComponents(assembly, sequence));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(XCAFDoc_ShapeTool.IsAssembly(assembly), Is.True);
      Assert.That(components, Is.EqualTo(new[] { first, second }));
      Assert.That(Referred(second), Is.EqualTo(part));
      Assert.That(XCAFDoc_ShapeTool.GetLocation(second).Transformation().TranslationPart(),
                  Is.EqualTo(new gp_XYZ(50, 0, 0)));
      Assert.That(Shapes.Volume(XCAFDoc_ShapeTool.GetShape(assembly)),
                  Is.EqualTo(2 * Shapes.BoxVolume).Within(1e-6));
    }
  }

  [Test]
  public void SetColor_IsReadBackForItsType()
  {
    // Arrange
    var label = _shapes.AddShape(Shapes.Box(), false);

    // Act
    _colors.SetColor(label, new Quantity_Color(1, 0, 0, Quantity_TOC_RGB), XCAFDoc_ColorSurf);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(Color(label, XCAFDoc_ColorSurf), Is.EqualTo(new[] { 1.0, 0.0, 0.0 }));
      Assert.That(_colors.IsSet(label, XCAFDoc_ColorGen), Is.False);
    }
  }

  [Test]
  public void Step_RoundTripKeepsStructureNamesAndColors()
  {
    // Arrange
    var path = Path.Combine(_directory, $"assembly {NonAscii.Text}.step");
    WriteSampleAssembly(path);
    _imported = NewDocument();
    var reader = new STEPCAFControl_Reader();
    reader.SetColorMode(true);
    reader.SetNameMode(true);

    // Act
    var read = reader.ReadFile(path);
    var transferred = reader.Transfer(_imported);
    var roots = Ocaf.Labels(XCAFDoc_DocumentTool.ShapeTool(_imported.Main()).GetFreeShapes);

    // Assert
    Assert.That(roots, Has.Count.EqualTo(1));
    var parts = Ocaf.Labels(sequence => XCAFDoc_ShapeTool.GetComponents(roots[0], sequence))
      .Select(Referred)
      .ToList();
    var colors = parts.ToDictionary(part => Ocaf.Name(part) ?? "",
                                    part => Color(part, XCAFDoc_ColorSurf));
    using (Assert.EnterMultipleScope())
    {
      Assert.That(read, Is.EqualTo(IFSelect_ReturnStatus.IFSelect_RetDone));
      Assert.That(transferred, Is.True);
      Assert.That(XCAFDoc_ShapeTool.IsAssembly(roots[0]), Is.True);
      Assert.That(Ocaf.Name(roots[0]), Is.EqualTo("assembly"));
      Assert.That(colors.Keys, Is.EquivalentTo(new[] { "plate", "bolt" }));
      Assert.That(colors, Does.ContainKey("plate").WithValue(new[] { 0.0, 0.0, 1.0 }));
      Assert.That(colors, Does.ContainKey("bolt").WithValue(new[] { 1.0, 0.0, 0.0 }));
    }
  }

  private TDocStd_Document NewDocument()
  {
    TDocStd_Document? document = null;
    _application.NewDocument("MDTV-XCAF", ref document);
    return document!;
  }

  private void WriteSampleAssembly(string path)
  {
    var plate = _shapes.AddShape(Shapes.Box(), false);
    var bolt = _shapes.AddShape(Shapes.Cylinder(), false);
    var assembly = _shapes.NewShape();
    _shapes.AddComponent(assembly, plate, Translation(0, 0, 0));
    _shapes.AddComponent(assembly, bolt, Translation(0, 0, 30));
    _shapes.UpdateAssemblies();
    TDataStd_Name.Set(assembly, "assembly");
    TDataStd_Name.Set(plate, "plate");
    TDataStd_Name.Set(bolt, "bolt");
    _colors.SetColor(plate, new Quantity_Color(0, 0, 1, Quantity_TOC_RGB), XCAFDoc_ColorSurf);
    _colors.SetColor(bolt, new Quantity_Color(1, 0, 0, Quantity_TOC_RGB), XCAFDoc_ColorSurf);

    var writer = new STEPCAFControl_Writer();
    writer.SetColorMode(true);
    writer.SetNameMode(true);
    if (!writer.Transfer(_document) || writer.Write(path) != IFSelect_ReturnStatus.IFSelect_RetDone)
    {
      throw new InvalidOperationException("STEP export failed");
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
