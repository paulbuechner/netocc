// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

// static usings
using static NetOcc.Viewer.NativeMethods;

namespace NetOcc.Viewer;

/// <summary>
/// A Win32 child window that shows an <see cref="OcctViewer"/>: its class owns a device context,
/// which OpenGL needs, and its messages drive the viewer (size, paint, mouse). WPF
/// (<c>HwndHost</c>) and Avalonia (<c>NativeControlHost</c>) host it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ViewerWindow : IDisposable
{
  private const string ClassName = "NetOccViewerWindow";

  private static readonly Dictionary<IntPtr, ViewerWindow> Windows = [];
  private static bool _registered;

  private readonly OcctViewer _viewer;

  private ViewerWindow(IntPtr handle, OcctViewer viewer)
  {
    Handle = handle;
    _viewer = viewer;
  }

  public IntPtr Handle { get; private set; }

  /// <summary>
  /// A child window of <paramref name="parent"/> showing <paramref name="viewer"/>.
  /// </summary>
  public static unsafe ViewerWindow Create(IntPtr parent, OcctViewer viewer)
  {
    var instance = GetModuleHandleW(null);
    if (!_registered)
    {
      fixed (char* className = ClassName)
      {
        var windowClass = new WNDCLASSEX
        {
          cbSize = (uint)sizeof(WNDCLASSEX),
          style = CS_OWNDC | CS_HREDRAW | CS_VREDRAW,
          lpfnWndProc =
            (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&Receive,
          hInstance = instance,
          hCursor = LoadCursorW(IntPtr.Zero, IDC_ARROW),
          lpszClassName = (IntPtr)className,
        };
        if (RegisterClassExW(in windowClass) == 0)
        {
          throw new Win32Exception();
        }
      }

      _registered = true;
    }

    var handle = CreateWindowExW(0, ClassName, "",
                                 WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN, 0, 0, 1,
                                 1, parent, IntPtr.Zero, instance, IntPtr.Zero);
    if (handle == IntPtr.Zero)
    {
      throw new Win32Exception();
    }

    var window = new ViewerWindow(handle, viewer);
    Windows[handle] = window;
    viewer.Attach(handle);
    return window;
  }

  public void Dispose()
  {
    if (Handle != IntPtr.Zero)
    {
      DestroyWindow(Handle);
      Handle = IntPtr.Zero;
    }
  }

  // the window procedure, which user32 calls: no .NET exception may leave it
  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
  private static IntPtr Receive(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam)
  {
    // messages during CreateWindowEx come before the window is known
    if (!Windows.TryGetValue(handle, out var window))
    {
      return DefWindowProcW(handle, message, wParam, lParam);
    }

    try
    {
      return window.OnMessage(message, wParam, lParam);
    }
    catch (Exception exception)
    {
      // a .NET exception can't unwind through user32, which called us: report it and carry on
      Trace.TraceError($"NetOcc viewer, message 0x{message:X4}: {exception}");
      return DefWindowProcW(handle, message, wParam, lParam);
    }
  }

  private IntPtr OnMessage(uint message, IntPtr wParam, IntPtr lParam)
  {
    var handle = Handle;
    switch (message)
    {
      case WM_SIZE:
        _viewer.Resize();
        return IntPtr.Zero;
      case WM_PAINT:
        BeginPaint(handle, out var paint);
        _viewer.Redraw();
        EndPaint(handle, in paint);
        return IntPtr.Zero;
      case WM_ERASEBKGND:
        // OpenGL paints every pixel
        return (IntPtr)1;
      case WM_MOUSEMOVE:
        _viewer.MouseMove(X(lParam), Y(lParam), Buttons(wParam), Modifiers(wParam));
        return IntPtr.Zero;
      case WM_LBUTTONDOWN or WM_MBUTTONDOWN or WM_RBUTTONDOWN:
        SetFocus(handle);
        SetCapture(handle);
        _viewer.MouseButtons(X(lParam), Y(lParam), Buttons(wParam), Modifiers(wParam));
        return IntPtr.Zero;
      case WM_LBUTTONUP or WM_MBUTTONUP or WM_RBUTTONUP:
        // the viewer first: releasing the capture cancels the input, which would swallow a click's
        // selection
        _viewer.MouseButtons(X(lParam), Y(lParam), Buttons(wParam), Modifiers(wParam));
        if (Buttons(wParam) == ViewerButtons.None)
        {
          ReleaseCapture();
        }

        return IntPtr.Zero;
      case WM_CAPTURECHANGED:
        // a dialog or Alt+Tab took the mouse mid-drag (lParam: who has it now): without its
        // button-up, the gesture would go on
        if (lParam != handle)
        {
          _viewer.CancelInput();
        }

        return IntPtr.Zero;
      case WM_MOUSEWHEEL:
        // in screen coordinates
        var point = new POINT { X = X(lParam), Y = Y(lParam) };
        ScreenToClient(handle, ref point);
        _viewer.MouseWheel(point.X, point.Y, (short)((long)wParam >> 16) / (double)WHEEL_DELTA);
        return IntPtr.Zero;
      case WM_DESTROY:
        Windows.Remove(handle);
        return IntPtr.Zero;
      default:
        return DefWindowProcW(handle, message, wParam, lParam);
    }
  }

  // the signed coordinates in lParam's words
  private static int X(IntPtr lParam) => (short)((long)lParam & 0xFFFF);

  private static int Y(IntPtr lParam) => (short)(((long)lParam >> 16) & 0xFFFF);

  private static ViewerButtons Buttons(IntPtr wParam)
  {
    var keys = (uint)(long)wParam;
    return ((keys & MK_LBUTTON) != 0 ? ViewerButtons.Left : 0)
           | ((keys & MK_MBUTTON) != 0 ? ViewerButtons.Middle : 0)
           | ((keys & MK_RBUTTON) != 0 ? ViewerButtons.Right : 0);
  }

  private static ViewerModifiers Modifiers(IntPtr wParam)
  {
    var keys = (uint)(long)wParam;
    return ((keys & MK_SHIFT) != 0 ? ViewerModifiers.Shift : 0)
           | ((keys & MK_CONTROL) != 0 ? ViewerModifiers.Control : 0)
           | (GetKeyState(VK_MENU) < 0 ? ViewerModifiers.Alt : 0);
  }
}
