// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

// Avalonia
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace NetOcc.Viewer.Avalonia;

/// <summary>
/// The viewer's Win32 window in Avalonia's layout (Windows only). Avalonia creates the native
/// control once the window shows, and may create it again after the control moves: the viewer lives
/// with each.
/// </summary>
public sealed class ViewerHost : NativeControlHost
{
  private ViewerWindow? _window;

  /// <summary>The viewer, once the native control exists.</summary>
  public OcctViewer? Viewer { get; private set; }

  /// <summary>A viewer exists: raised after the layout pass that created it.</summary>
  public event EventHandler? ViewerCreated;

  protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
  {
    if (!OperatingSystem.IsWindows())
    {
      return base.CreateNativeControlCore(parent);
    }

    Viewer = new OcctViewer();
    _window = ViewerWindow.Create(parent.Handle, Viewer);
    Dispatcher.UIThread.Post(() => ViewerCreated?.Invoke(this, EventArgs.Empty));
    return new PlatformHandle(_window.Handle, "HWND");
  }

  // the view goes before its window
  protected override void DestroyNativeControlCore(IPlatformHandle control)
  {
    if (_window is null || !OperatingSystem.IsWindows())
    {
      base.DestroyNativeControlCore(control);
      return;
    }

    Viewer?.Dispose();
    Viewer = null;
    _window.Dispose();
    _window = null;
  }
}
