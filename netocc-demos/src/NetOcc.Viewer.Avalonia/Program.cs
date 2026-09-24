// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

// Avalonia
using Avalonia;

namespace NetOcc.Viewer.Avalonia;

internal static class Program
{
  // Avalonia's previewer builds the app through this
  public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();

  [STAThread]
  public static int Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
}
