// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace NetOcc.Viewer.Wpf;

/// <summary>
/// The viewer's Win32 window in WPF's layout. The viewer lives as long as the window.
/// </summary>
public sealed class ViewerHost : HwndHost
{
  private ViewerWindow? _window;

  public OcctViewer Viewer { get; } = new();

  protected override HandleRef BuildWindowCore(HandleRef hwndParent)
  {
    _window = ViewerWindow.Create(hwndParent.Handle, Viewer);
    return new HandleRef(this, _window.Handle);
  }

  // the view goes before its window
  protected override void DestroyWindowCore(HandleRef hwnd)
  {
    Viewer.Dispose();
    _window?.Dispose();
    _window = null;
  }
}
