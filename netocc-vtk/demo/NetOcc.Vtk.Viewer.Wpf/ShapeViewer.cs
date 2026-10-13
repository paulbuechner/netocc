// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Threading;

//
using OCC.Core.BRepAlgoAPI;
using OCC.Core.BRepPrimAPI;
using OCC.Core.gp;
using OCC.Core.IVtk;
using OCC.Core.IVtkOCC;
using OCC.Core.IVtkTools;
using OCC.Core.NCollection;
using OCC.Core.TopoDS;
using Vtk;

namespace NetOcc.Vtk.Viewer;

/// <summary>
/// An OCCT part and a result field on it in one VTK scene: the part's faces coloured by a
/// temperature with a scalar bar, its edges drawn by TKIVtk, and a click picks the face under the
/// mouse (IVtkTools_ShapePicker), which then shows highlighted. <see cref="ViewerWindow"/>'s
/// messages drive it.
/// </summary>
public sealed class ShapeViewer : IDisposable
{
  private readonly vtkRenderWindow _window = new();
  private readonly vtkRenderer _renderer = new();
  private readonly vtkGenericRenderWindowInteractor _interactor = new();
  private readonly vtkInteractorStyleTrackballCamera _style = new();

  private readonly IVtkOCC_Shape _part = new(Part());
  private readonly IVtkTools_ShapeDataSource _source = new();
  private readonly IVtkTools_DisplayModeFilter _faces = new();
  private readonly IVtkTools_DisplayModeFilter _edges = new();
  private readonly vtkPolyData _field = new();
  private readonly vtkPolyDataMapper _fieldMapper = new();
  private readonly vtkActor _fieldActor = new();
  private readonly vtkPolyDataMapper _edgesMapper = new();
  private readonly vtkActor _edgesActor = new();
  private readonly vtkLookupTable _colors = new();
  private readonly vtkScalarBarActor _scalarBar = new();
  private readonly IVtkTools_ShapePicker _picker = new();
  private readonly IVtkTools_SubPolyDataFilter _picked = new();
  private readonly vtkPolyDataMapper _pickedMapper = new();
  private readonly vtkActor _pickedActor = new();

  // where the left button went down: a click picks, a drag rotates
  private int _downX;
  private int _downY;
  private int _height;

  public ShapeViewer()
  {
    // the part, meshed by TKIVtk: its faces for the field, its edges as lines
    _source.SetShape(_part);
    using (var shape = _source.GetOutputPort())
    {
      _faces.SetInputConnection(shape);
      _edges.SetInputConnection(shape);
    }

    _faces.SetDisplayMode(IVtk_DisplayMode.DM_Shading);
    _edges.SetDisplayMode(IVtk_DisplayMode.DM_Wireframe);
    _faces.Update();

    // the result: a temperature on the part's mesh, hot around the left hole
    using (var faces = _faces.GetOutput())
    {
      _field.ShallowCopy(faces);
    }

    Temperature(_field);
    _colors.SetHueRange(0.667, 0.0);
    _colors.Build();
    _fieldMapper.SetInputData(_field);
    _fieldMapper.SetScalarModeToUsePointFieldData();
    _fieldMapper.SelectColorArray(Field);
    _fieldMapper.SetLookupTable(_colors);
    _fieldMapper.UseLookupTableScalarRangeOn();
    using (var temperature = _field.GetPointData())
    using (var values = temperature.GetArray(Field))
    {
      var range = values.GetRange(0);
      _colors.SetRange(range[0], range[1]);
    }

    _fieldActor.SetMapper(_fieldMapper);
    _scalarBar.SetLookupTable(_colors);
    _scalarBar.SetTitle("temperature");
    _scalarBar.SetNumberOfLabels(5);
    _scalarBar.SetMaximumWidthInPixels(110);

    // the edges: TKIVtk's actor, which the picker knows the shape by
    using (var edges = _edges.GetOutputPort())
    {
      _edgesMapper.SetInputConnection(edges);
    }

    IVtkTools.InitShapeMapper(_edgesMapper);
    _edgesActor.SetMapper(_edgesMapper);
    IVtkTools_ShapeObject.SetShapeSource(_source, _edgesActor);

    // the picked face: its cells of the part's mesh, highlighted
    using (var shape = _source.GetOutputPort())
    {
      _picked.SetInputConnection(shape);
    }

    _picked.SetDoFiltering(true);
    _picked.SetData(new NCollection_List_long_long());
    using (var picked = _picked.GetOutputPort())
    {
      _pickedMapper.SetInputConnection(picked);
    }

    _pickedMapper.ScalarVisibilityOff();
    _pickedActor.SetMapper(_pickedMapper);
    using (var look = _pickedActor.GetProperty())
    {
      look.SetColor(1.0, 0.85, 0.1);
      look.SetLineWidth(4);
    }

    _renderer.SetBackground(0.1, 0.12, 0.16);
    // the highlight lies on the faces it marks, drawn by another shader: before each render it
    // moves a ten-thousandth of the camera's distance toward the camera, in front of them rather
    // than in a fight over the depth buffer. VTK's coincident-topology offsets would do it too, but
    // they write a pixel's depth, which multisampling compares per sample, and without
    // multisampling the edges break up.
    _renderer.AddObserver(vtkCommand_EventIds.StartEvent, (_, _) => PullHighlight());
    _renderer.AddActor(_fieldActor);
    _renderer.AddActor(_edgesActor);
    _renderer.AddActor(_pickedActor);
    _renderer.AddViewProp(_scalarBar);
    _picker.SetRenderer(_renderer);
    _picker.SetSelectionMode(IVtk_SelectionMode.SM_Face);
  }

  /// <summary>The field the part is coloured by.</summary>
  public const string Field = "temperature";

  /// <summary>What the status line says: the part, then what a click picked.</summary>
  public event EventHandler<string>? StatusChanged;

  /// <summary>
  /// Once, on the UI thread, before the first viewer: VTK's messages go to C# (Trace) instead of a
  /// window of VTK's own, and the finalizer releases VTK objects on this thread.
  /// </summary>
  public static void InitializeOnUiThread()
  {
    VtkOutput.Install();
    VtkFinalizer.Context = SynchronizationContext.Current;
  }

  /// <summary>Draws into <paramref name="handle"/> (an HWND) from now on.</summary>
  public void Attach(IntPtr handle)
  {
    _window.SetWindowId(handle);
    _window.AddRenderer(_renderer);
    _interactor.SetRenderWindow(_window);
    _interactor.SetInteractorStyle(_style);
    _interactor.Initialize();
    _renderer.ResetCamera();
    StatusChanged?.Invoke(
      this,
      "A part and a temperature on it. Click a face to pick it; left button: rotate, middle: pan, right or wheel: zoom.");
  }

  public void Resize(int width, int height)
  {
    _height = height;
    _window.SetSize(width, height);
    _interactor.UpdateSize(width, height);
  }

  public void Redraw() => _window.Render();

  public void MouseMove(int x, int y, ViewerModifiers modifiers)
  {
    Position(x, y, modifiers);
    _interactor.MouseMoveEvent();
  }

  public void ButtonDown(ViewerButton button, int x, int y, ViewerModifiers modifiers)
  {
    Position(x, y, modifiers);
    switch (button)
    {
      case ViewerButton.Left:
        (_downX, _downY) = (x, y);
        _interactor.LeftButtonPressEvent();
        break;
      case ViewerButton.Middle:
        _interactor.MiddleButtonPressEvent();
        break;
      case ViewerButton.Right:
        _interactor.RightButtonPressEvent();
        break;
    }
  }

  public void ButtonUp(ViewerButton button, int x, int y, ViewerModifiers modifiers)
  {
    Position(x, y, modifiers);
    switch (button)
    {
      case ViewerButton.Left:
        _interactor.LeftButtonReleaseEvent();
        if (Math.Abs(x - _downX) <= 3 && Math.Abs(y - _downY) <= 3)
        {
          Pick(x, y);
        }

        break;
      case ViewerButton.Middle:
        _interactor.MiddleButtonReleaseEvent();
        break;
      case ViewerButton.Right:
        _interactor.RightButtonReleaseEvent();
        break;
    }
  }

  public void MouseWheel(int x, int y, double notches, ViewerModifiers modifiers)
  {
    Position(x, y, modifiers);
    if (notches > 0)
    {
      _interactor.MouseWheelForwardEvent();
    }
    else if (notches < 0)
    {
      _interactor.MouseWheelBackwardEvent();
    }
  }

  /// <summary>
  /// Picks the face at the window's pixel (<paramref name="x"/>, <paramref name="y"/>, y down) and
  /// highlights it; the status says which.
  /// </summary>
  public void Pick(int x, int y)
  {
    _picker.Pick(x, _height - 1 - y, 0);
    using var shapes = _picker.GetPickedShapesIds();
    using var faces = shapes.Count > 0
      ? _picker.GetPickedSubShapesIds(shapes.First())
      : new NCollection_List_long_long();
    _picked.SetData(faces);
    _picked.Modified();
    Redraw();
    StatusChanged?.Invoke(
      this,
      faces.Count > 0
        ? $"Picked {Describe(faces.First())} {faces.First()} of the part."
        : "Nothing picked: click a face of the part.");
  }

  // the render window last: its OpenGL context goes with it, on this thread
  public void Dispose()
  {
    foreach (IDisposable item in new IDisposable[]
             {
               _interactor, _style, _picker, _pickedActor, _pickedMapper, _picked, _scalarBar,
               _colors, _edgesActor, _edgesMapper, _fieldActor, _fieldMapper, _field, _edges,
               _faces, _source, _renderer, _window,
             })
    {
      item.Dispose();
    }
  }

  // the highlight toward the camera (StartEvent: the camera is where this render looks from)
  private void PullHighlight()
  {
    using var camera = _renderer.GetActiveCamera();
    var toward = camera.GetDirectionOfProjection();
    var distance = camera.GetDistance() * 1e-4;
    _pickedActor.SetPosition(-toward[0] * distance, -toward[1] * distance, -toward[2] * distance);
  }

  // the window's pixels: y down, as VTK's FlipY takes them
  private void Position(int x, int y, ViewerModifiers modifiers) =>
    _interactor.SetEventInformationFlipY(x, y, (modifiers & ViewerModifiers.Control) != 0 ? 1 : 0,
                                         (modifiers & ViewerModifiers.Shift) != 0 ? 1 : 0);

  // what the picked sub-shape is: OCCT's shape type (a face)
  private string Describe(long subShape) =>
    _part.GetSubShape(subShape).ShapeType().ToString().Replace("TopAbs_", "").ToLowerInvariant();

  // a 100 x 40 x 20 bracket with two holes through it
  private static TopoDS_Shape Part()
  {
    var part = new BRepPrimAPI_MakeBox(100, 40, 20).Shape();
    foreach (var x in new[] { 25.0, 75.0 })
    {
      var axis = new gp_Ax2(new gp_Pnt(x, 20, -1), new gp_Dir(0, 0, 1));
      part = new BRepAlgoAPI_Cut(part, new BRepPrimAPI_MakeCylinder(axis, 8, 22).Shape()).Shape();
    }

    return part;
  }

  // T = 20 + 80 exp(-d^2 / 400), d the distance from the left hole's axis: the field C# computes
  // over the mesh's points in one go
  private static void Temperature(vtkPolyData mesh)
  {
    using var points = mesh.GetPoints();
    using var coordinates = points.GetData();
    var xyz = coordinates.ToDoubleArray();
    var values = new double[xyz.Length / 3];
    for (var i = 0; i < values.Length; i++)
    {
      var dx = xyz[3 * i] - 25;
      var dy = xyz[3 * i + 1] - 20;
      values[i] = 20 + 80 * Math.Exp(-(dx * dx + dy * dy) / 400);
    }

    using var field = vtkDoubleArray.FromArray(values);
    field.SetName(Field);
    using var pointData = mesh.GetPointData();
    pointData.AddArray(field);
  }
}

public enum ViewerButton
{
  Left,
  Middle,
  Right,
}

[Flags]
public enum ViewerModifiers
{
  None = 0,
  Shift = 1,
  Control = 2,
}
