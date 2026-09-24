// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

// NUnit
using NUnit.Framework;

//
using OCC.Core.AIS;
using OCC.Core.Aspect;
using OCC.Core.BRepPrimAPI;
using OCC.Core.Graphic3d;
using OCC.Core.NCollection;
using OCC.Core.OpenGl;
using OCC.Core.Quantity;
using OCC.Core.TDF;
using OCC.Core.TopAbs;
using OCC.Core.TopoDS;
using OCC.Core.V3d;
using OCC.Core.XCAFPrs;

namespace NetOcc.Samples;

/// <summary>
/// Visualization: the view needs a window and a GPU, so these compile without running;
/// <see cref="Presentation"/> runs, since presentations don't draw until they're displayed.
/// </summary>
[TestFixture]
public class Visualization
{
  #region viewer
  public sealed class ShapeView : IDisposable
  {
    public ShapeView(IntPtr window)
    {
      // one driver per display connection, one viewer per driver, views of the viewer per window
      Display = new Aspect_DisplayConnection();
      Driver = new OpenGl_GraphicDriver(Display);
      Viewer = new V3d_Viewer(Driver);
      Viewer.SetDefaultLights();
      Viewer.SetLightOn();

      // the interactive context shows and selects presentations
      Context = new AIS_InteractiveContext(Viewer);
      Context.SetDisplayMode((int)AIS_DisplayMode.AIS_Shaded, false);

      View = Viewer.CreateView();
      View.SetWindow(Aspect_Window.FromNativeHandle(window, Display));
      View.SetBackgroundColor(new Quantity_Color(Quantity_NameOfColor.Quantity_NOC_GRAY20));
      View.TriedronDisplay(Aspect_TypeOfTriedronPosition.Aspect_TOTP_LEFT_LOWER,
                           new Quantity_Color(Quantity_NameOfColor.Quantity_NOC_WHITE), 0.1,
                           V3d_TypeOfVisualization.V3d_ZBUFFER);
      View.MustBeResized();
    }

    public Aspect_DisplayConnection Display { get; }
    public OpenGl_GraphicDriver Driver { get; }
    public V3d_Viewer Viewer { get; }
    public AIS_InteractiveContext Context { get; }
    public V3d_View View { get; }

    public void Show(TopoDS_Shape shape)
    {
      Context.Display(new AIS_Shape(shape), true);
      View.FitAll(0.01, true);
    }

    // on the UI thread, before the window goes: the finalizer thread has no GL context
    public void Dispose()
    {
      Context.RemoveAll(false);
      View.Remove();
      Context.Dispose();
      View.Dispose();
      Viewer.Dispose();
      Driver.Dispose();
      Display.Dispose();
    }
  }
  #endregion

  #region camera
  public static void Look(V3d_View view)
  {
    view.SetProj(V3d_TypeOfOrientation.V3d_TypeOfOrientation_Zup_AxoRight); // isometric
    view.FitAll(0.01, false);
    view.SetZoom(1.5, true);
    view.Redraw();

    view.Dump("view.png"); // what the view shows, as an image
  }
  #endregion

  #region xcaf-display
  public static void ShowDocument(AIS_InteractiveContext context, TDF_LabelSequence roots)
  {
    // XCAFPrs_AISObject shows a label with the colors the document gives its parts
    foreach (var root in roots)
    {
      context.Display(new XCAFPrs_AISObject(root), (int)AIS_DisplayMode.AIS_Shaded, 0, false);
    }

    context.UpdateCurrentViewer();
  }
  #endregion

  #region selection
  public static List<TopoDS_Shape> PickFaces(AIS_InteractiveContext context, V3d_View view,
                                             AIS_Shape presentation, int x, int y)
  {
    // select faces instead of the whole shape
    context.Deactivate(presentation);
    context.Activate(presentation, AIS_Shape.SelectionMode(TopAbs_ShapeEnum.TopAbs_FACE), false);

    // highlight what's under the pixel, then select it
    context.MoveTo(x, y, view, true);
    context.SelectDetected(AIS_SelectionScheme.AIS_SelectionScheme_Replace);
    context.UpdateCurrentViewer();

    var faces = new List<TopoDS_Shape>();
    for (context.InitSelected(); context.MoreSelected(); context.NextSelected())
    {
      faces.Add(context.SelectedShape());
    }

    return faces;
  }
  #endregion

  #region input
  /// <summary>
  /// OCCT's mouse buttons (<c>Aspect_VKeyMouse</c>), which C# doesn't get since OCCT declares them
  /// in an anonymous enum.
  /// </summary>
  [Flags]
  public enum Buttons : uint
  {
    None = 0,
    Left = 1 << 13,
    Middle = 1 << 14,
    Right = 1 << 15,
  }

  public static void MouseMove(AIS_ViewController controller, AIS_InteractiveContext context,
                               V3d_View view, int x, int y, Buttons buttons)
  {
    // the controller turns input into rotation, panning, zoom and selection, then FlushViewEvents
    // applies it
    using var point = new BVH_Vec2i(x, y);
    if (controller.UpdateMousePosition(point, (uint)buttons, 0, false))
    {
      controller.FlushViewEvents(context, view, true);
    }
  }

  public static void MouseWheel(AIS_ViewController controller, AIS_InteractiveContext context,
                                V3d_View view, int x, int y, double notches)
  {
    using var point = new BVH_Vec2i(x, y);
    using var delta = new Aspect_ScrollDelta(point, notches);
    if (controller.UpdateZoom(delta))
    {
      controller.FlushViewEvents(context, view, true);
    }
  }
  #endregion

  [Test]
  public void Presentation()
  {
    // Act
    #region presentation
    var presentation = new AIS_Shape(new BRepPrimAPI_MakeBox(10, 20, 30).Shape());
    presentation.SetColor(new Quantity_Color(0.2, 0.5, 0.8,
                                             Quantity_TypeOfColor.Quantity_TOC_sRGB));
    presentation.SetTransparency(0.3);
    presentation.SetMaterial(
      new Graphic3d_MaterialAspect(Graphic3d_NameOfMaterial.Graphic3d_NameOfMaterial_Aluminum));
    presentation.SetDisplayMode((int)AIS_DisplayMode.AIS_Shaded);

    // edges drawn over the shading
    presentation.Attributes().SetFaceBoundaryDraw(true);
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(presentation.HasColor(), Is.True);
      Assert.That(presentation.Transparency(), Is.EqualTo(0.3).Within(1e-6));
      Assert.That(presentation.Attributes().FaceBoundaryDraw(), Is.True);
    }
  }
}
