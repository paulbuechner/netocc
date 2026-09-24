// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

//
using OCC.Core.AIS;
using OCC.Core.Aspect;
using OCC.Core.NCollection;
using OCC.Core.OpenGl;
using OCC.Core.Quantity;
using OCC.Core.V3d;
using OCC.Core.XCAFPrs;

// static usings
using static OCC.Core.AIS.AIS_DisplayMode;
using static OCC.Core.Aspect.Aspect_GradientFillMethod;
using static OCC.Core.Aspect.Aspect_TypeOfTriedronPosition;
using static OCC.Core.Quantity.Quantity_NameOfColor;
using static OCC.Core.Quantity.Quantity_TypeOfColor;
using static OCC.Core.V3d.V3d_TypeOfVisualization;

namespace NetOcc.Viewer;

/// <summary>
/// Mouse buttons as OCCT's view controller counts them (<c>Aspect_VKeyMouse</c>, an anonymous enum
/// C# doesn't get).
/// </summary>
[Flags]
public enum ViewerButtons : uint
{
  None = 0,
  Left = 1 << 13,
  Middle = 1 << 14,
  Right = 1 << 15,
}

/// <summary>
/// Modifier keys as OCCT's view controller counts them (<c>Aspect_VKeyFlags</c>).
/// </summary>
[Flags]
public enum ViewerModifiers : uint
{
  None = 0,
  Shift = 1 << 8,
  Control = 1 << 9,
  Alt = 1 << 10,
}

/// <summary>
/// An OCCT view in a native window: the OpenGL driver, the viewer, the interactive context, and the
/// view controller that turns mouse input into rotation (left button), panning (middle), zoom
/// (right, wheel) and selection (click). Positions are in the window's pixels.
/// </summary>
public sealed class OcctViewer : IDisposable
{
  private readonly Aspect_DisplayConnection _display = new();
  private readonly OpenGl_GraphicDriver _driver;
  private readonly V3d_Viewer _viewer;
  private readonly AIS_InteractiveContext _context;
  private readonly AIS_ViewController _controller = new();
  private V3d_View? _view;
  private StepDocument? _document;

  public OcctViewer()
  {
    _driver = new OpenGl_GraphicDriver(_display);
    _viewer = new V3d_Viewer(_driver);
    _viewer.SetDefaultLights();
    _viewer.SetLightOn();
    _context = new AIS_InteractiveContext(_viewer);
    _context.SetDisplayMode((int)AIS_Shaded, false);
  }

  /// <summary>Shows the view in a native window: an HWND on Windows.</summary>
  public void Attach(IntPtr window)
  {
    _view = _viewer.CreateView();
    _view.SetWindow(Aspect_Window.FromNativeHandle(window, _display));
    _view.SetBgGradientColors(new Quantity_Color(0.33, 0.40, 0.50, Quantity_TOC_sRGB),
                              new Quantity_Color(0.86, 0.88, 0.91, Quantity_TOC_sRGB),
                              Aspect_GradientFillMethod_Vertical, false);
    _view.TriedronDisplay(Aspect_TOTP_LEFT_LOWER, new Quantity_Color(Quantity_NOC_WHITE), 0.1,
                          V3d_ZBUFFER);
    _view.MustBeResized();
  }

  /// <summary>
  /// Shows the document's shapes with their colors, fitted into the view; the viewer closes the
  /// previous document.
  /// </summary>
  public void Show(StepDocument document)
  {
    _context.RemoveAll(false);
    _document?.Dispose();
    _document = document;
    foreach (var root in document.Roots)
    {
      _context.Display(new XCAFPrs_AISObject(root), (int)AIS_Shaded, 0, false);
    }

    FitAll();
  }

  public void FitAll()
  {
    _view?.FitAll(0.01, false);
    Redraw();
  }

  /// <summary>The window changed size; the paint that follows draws the view.</summary>
  public void Resize() => _view?.MustBeResized();

  /// <summary>Draws the view again, handling the input so far.</summary>
  public void Redraw()
  {
    if (_view is null)
    {
      return;
    }

    _view.Invalidate();
    _controller.FlushViewEvents(_context, _view, true);
  }

  public void MouseMove(int x, int y, ViewerButtons buttons, ViewerModifiers modifiers)
  {
    using var point = new BVH_Vec2i(x, y);
    if (_controller.UpdateMousePosition(point, (uint)buttons, (uint)modifiers, false))
    {
      Flush();
    }
  }

  /// <summary>A button went down or up: <paramref name="buttons"/> are the ones held now.</summary>
  public void MouseButtons(int x, int y, ViewerButtons buttons, ViewerModifiers modifiers)
  {
    using var point = new BVH_Vec2i(x, y);
    if (_controller.UpdateMouseButtons(point, (uint)buttons, (uint)modifiers, false))
    {
      Flush();
    }
  }

  /// <summary>
  /// Another window took the mouse mid-drag (a dialog, Alt+Tab): ends the gesture the buttons
  /// started.
  /// </summary>
  public void CancelInput() => _controller.ResetViewInput();

  /// <summary>
  /// The wheel turned by <paramref name="notches"/> (positive: away from the user, zooming in).
  /// </summary>
  public void MouseWheel(int x, int y, double notches)
  {
    using var point = new BVH_Vec2i(x, y);
    using var delta = new Aspect_ScrollDelta(point, notches);
    if (_controller.UpdateZoom(delta))
    {
      Flush();
    }
  }

  public void Dispose()
  {
    _context.RemoveAll(false);
    _view?.Remove();
    _document?.Dispose();
    _controller.Dispose();
    _context.Dispose();
    _view?.Dispose();
    _viewer.Dispose();
    _driver.Dispose();
    _display.Dispose();
  }

  // handles the input without invalidating the whole view: the controller redraws what it changed
  private void Flush()
  {
    if (_view is not null)
    {
      _controller.FlushViewEvents(_context, _view, true);
    }
  }
}
