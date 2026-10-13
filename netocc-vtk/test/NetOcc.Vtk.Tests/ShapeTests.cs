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
using OCC.Core.TopoDS;
using Vtk;

namespace NetOcc.Vtk.Tests;

/// <summary>
/// An OCCT shape in VTK: IVtkTools_ShapeDataSource is a vtkPolyDataAlgorithm NetVtk's pipelines
/// take, IVtkTools_ShapePicker picks its faces.
/// </summary>
[TestFixture]
public class ShapeTests
{
  [OneTimeSetUp]
  public void RouteVtkOutput() => VtkOutput.Install();

  [Test]
  public void ShapeDataSource_IsNetVtksAlgorithm()
  {
    // Arrange (a NetVtk algorithm for comparison: VTK's executive holds a reference too)
    using var source = new IVtkTools_ShapeDataSource();
    using var cone = new vtkConeSource();

    // Act
    vtkAlgorithm algorithm = source;

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(algorithm.GetClassName(), Is.EqualTo("IVtkTools_ShapeDataSource"));
      Assert.That(source.GetReferenceCount(), Is.EqualTo(cone.GetReferenceCount()),
                  "the proxy's reference only, as NetVtk's own");
    }
  }

  [Test]
  public void ShapeDataSource_MeshesTheShape()
  {
    // Arrange
    using var source = new IVtkTools_ShapeDataSource();
    source.SetShape(new IVtkOCC_Shape(Part()));

    // Act
    source.Update();

    // Assert
    using var output = source.GetOutput();
    var bounds = output.GetBounds();
    using (Assert.EnterMultipleScope())
    {
      Assert.That(output.GetNumberOfCells(), Is.Positive);
      Assert.That(bounds, Is.EqualTo(new double[] { 0, 60, 0, 40, 0, 20 }).Within(1e-6));
    }
  }

  [Test]
  public void ShapeDataSource_FeedsANetVtkMapper()
  {
    // Arrange
    using var source = new IVtkTools_ShapeDataSource();
    source.SetShape(new IVtkOCC_Shape(Part()));
    using var mapper = new vtkPolyDataMapper();

    // Act
    using (var port = source.GetOutputPort())
    {
      mapper.SetInputConnection(port);
    }

    mapper.Update();

    // Assert
    using var input = mapper.GetInput();
    Assert.That(input.GetNumberOfCells(), Is.Positive);
  }

  [Test]
  [Category("Rendering")]
  public void ShapePicker_PicksAFaceOfTheShape()
  {
    // Arrange (the part from above, filling the view: the window's centre is on its top face)
    var occShape = new IVtkOCC_Shape(Part());
    using var source = new IVtkTools_ShapeDataSource();
    source.SetShape(occShape);
    using var mapper = new vtkPolyDataMapper();
    using (var port = source.GetOutputPort())
    {
      mapper.SetInputConnection(port);
    }

    IVtkTools.InitShapeMapper(mapper);
    using var actor = new vtkActor();
    actor.SetMapper(mapper);
    IVtkTools_ShapeObject.SetShapeSource(source, actor);
    using var renderer = new vtkRenderer();
    renderer.AddActor(actor);
    using var window = new vtkRenderWindow();
    window.SetOffScreenRendering(1);
    window.SetSize(200, 200);
    window.AddRenderer(renderer);
    renderer.ResetCamera();
    window.Render();
    using var picker = new IVtkTools_ShapePicker();
    picker.SetRenderer(renderer);
    picker.SetSelectionMode(IVtk_SelectionMode.SM_Face);

    // Act (off the hole: a quarter in from the left)
    picker.Pick(50, 100, 0);

    // Assert
    using var shapes = picker.GetPickedShapesIds();
    Assert.That(shapes.Count, Is.EqualTo(1));
    using var faces = picker.GetPickedSubShapesIds(shapes.First());
    Assert.That(faces.Count, Is.EqualTo(1));
    var face = occShape.GetSubShape(faces.First());
    Assert.That(face.ShapeType(), Is.EqualTo(OCC.Core.TopAbs.TopAbs_ShapeEnum.TopAbs_FACE));
  }

  // a 60 x 40 x 20 block with a hole through it
  private static TopoDS_Shape Part()
  {
    var block = new BRepPrimAPI_MakeBox(60, 40, 20).Shape();
    var axis = new gp_Ax2(new gp_Pnt(30, 20, -1), new gp_Dir(0, 0, 1));
    var hole = new BRepPrimAPI_MakeCylinder(axis, 8, 22).Shape();
    return new BRepAlgoAPI_Cut(block, hole).Shape();
  }
}
