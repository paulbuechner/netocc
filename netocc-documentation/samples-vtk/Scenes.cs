// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Linq;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BRepAlgoAPI;
using OCC.Core.BRepPrimAPI;
using OCC.Core.gp;
using OCC.Core.IVtk;
using OCC.Core.IVtkOCC;
using OCC.Core.IVtkTools;
using OCC.Core.TopAbs;
using OCC.Core.TopoDS;
using Vtk;

namespace NetOcc.Vtk.Samples;

/// <summary>
/// OCCT shapes in VTK scenes (articles/vtk.md): the shape as VTK data, a field on it, picking. The
/// one that renders runs off the screen and needs OpenGL 3.2 (category Rendering).
/// </summary>
[TestFixture]
public class Scenes
{
  [OneTimeSetUp]
  public void RouteVtkOutput() => VtkOutput.Install();

  [Test]
  public void ShapeSource()
  {
    #region shape-source
    // a 60 x 40 x 20 block with a hole through it
    var block = new BRepPrimAPI_MakeBox(60, 40, 20).Shape();
    var axis = new gp_Ax2(new gp_Pnt(30, 20, -1), new gp_Dir(0, 0, 1));
    var hole = new BRepPrimAPI_MakeCylinder(axis, 8, 22).Shape();
    TopoDS_Shape part = new BRepAlgoAPI_Cut(block, hole).Shape();

    // TKIVtk meshes it into VTK's polygonal data, as a VTK algorithm
    using var source = new IVtkTools_ShapeDataSource();
    source.SetShape(new IVtkOCC_Shape(part));

    // which plugs into any pipeline: here straight into a mapper and an actor
    using var mapper = new vtkPolyDataMapper();
    using (var port = source.GetOutputPort())
    {
      mapper.SetInputConnection(port);
    }

    using var actor = new vtkActor();
    actor.SetMapper(mapper);
    #endregion

    source.Update();
    using var output = source.GetOutput();
    using (Assert.EnterMultipleScope())
    {
      Assert.That(output.GetBounds(),
                  Is.EqualTo(new double[] { 0, 60, 0, 40, 0, 20 }).Within(1e-6));
      Assert.That(output.GetNumberOfPolys(), Is.Positive, "faces");
      Assert.That(output.GetNumberOfLines(), Is.Positive, "edges");
    }
  }

  [Test]
  public void Field()
  {
    using var source = new IVtkTools_ShapeDataSource();
    source.SetShape(new IVtkOCC_Shape(Part()));

    #region field
    // the shape's faces only (TKIVtk's output holds its edges as lines too)
    using var faces = new IVtkTools_DisplayModeFilter();
    using (var port = source.GetOutputPort())
    {
      faces.SetInputConnection(port);
    }

    faces.SetDisplayMode(IVtk_DisplayMode.DM_Shading);
    faces.Update();

    // a copy of the mesh to carry the result
    using var result = new vtkPolyData();
    using (var mesh = faces.GetOutput())
    {
      result.ShallowCopy(mesh);
    }

    // a temperature per point, computed over all coordinates in one go: 20 at x = 0, 2 more per mm
    using var points = result.GetPoints();
    using var coordinates = points.GetData();
    var xyz = coordinates.ToDoubleArray();
    var values = new double[xyz.Length / 3];
    for (var i = 0; i < values.Length; i++)
    {
      values[i] = 20 + 2 * xyz[3 * i];
    }

    using var temperature = vtkDoubleArray.FromArray(values);
    temperature.SetName("temperature");
    using (var pointData = result.GetPointData())
    {
      pointData.SetScalars(temperature);
    }

    // coloured blue to red over its range, with a scalar bar
    var range = temperature.GetRange(0);
    using var colors = new vtkLookupTable();
    colors.SetHueRange(0.667, 0.0);
    colors.SetRange(range[0], range[1]);
    colors.Build();
    using var mapper = new vtkPolyDataMapper();
    mapper.SetInputData(result);
    mapper.SetLookupTable(colors);
    mapper.UseLookupTableScalarRangeOn();
    using var bar = new vtkScalarBarActor();
    bar.SetLookupTable(colors);
    bar.SetTitle("temperature");
    #endregion

    using (Assert.EnterMultipleScope())
    {
      Assert.That(range, Is.EqualTo(new double[] { 20, 140 }).Within(1e-6));
      Assert.That(result.GetNumberOfPolys(), Is.Positive, "faces");
      Assert.That(result.GetNumberOfLines(), Is.Zero, "no edges");
    }
  }

  [Test]
  [Category("Rendering")]
  public void Picking()
  {
    using var window = new vtkRenderWindow();
    window.SetOffScreenRendering(1);
    window.SetSize(200, 200);

    #region picking
    var shape = new IVtkOCC_Shape(Part());
    using var source = new IVtkTools_ShapeDataSource();
    source.SetShape(shape);
    using var mapper = new vtkPolyDataMapper();
    using (var port = source.GetOutputPort())
    {
      mapper.SetInputConnection(port);
    }

    // TKIVtk's colours per sub-shape type, and the actor registered as the shape's
    IVtkTools.InitShapeMapper(mapper);
    using var actor = new vtkActor();
    actor.SetMapper(mapper);
    IVtkTools_ShapeObject.SetShapeSource(source, actor);

    using var renderer = new vtkRenderer();
    renderer.AddActor(actor);
    window.AddRenderer(renderer);
    renderer.ResetCamera();
    window.Render();

    // faces at a display position (pixels, y up): the shapes hit, then their sub-shapes
    using var picker = new IVtkTools_ShapePicker();
    picker.SetRenderer(renderer);
    picker.SetSelectionMode(IVtk_SelectionMode.SM_Face);
    picker.Pick(50, 100, 0);
    using var shapes = picker.GetPickedShapesIds();
    using var faces = picker.GetPickedSubShapesIds(shapes.First());
    TopoDS_Shape face = shape.GetSubShape(faces.First());
    #endregion

    Assert.That(face.ShapeType(), Is.EqualTo(TopAbs_ShapeEnum.TopAbs_FACE));
  }

  // the block with a hole of ShapeSource
  private static TopoDS_Shape Part()
  {
    var block = new BRepPrimAPI_MakeBox(60, 40, 20).Shape();
    var axis = new gp_Ax2(new gp_Pnt(30, 20, -1), new gp_Dir(0, 0, 1));
    return new BRepAlgoAPI_Cut(block, new BRepPrimAPI_MakeCylinder(axis, 8, 22).Shape()).Shape();
  }
}
