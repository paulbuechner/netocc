// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

// Avalonia
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace NetOcc.Viewer.Avalonia;

public partial class MainWindow : Window
{
  private readonly FileOpener _opener = new();
  private string? _startup;

  // Avalonia's XAML loader and previewer need a parameterless constructor
  public MainWindow() : this(null)
  {
  }

  /// <param name="startup">A STEP file to open once there's a view, or --sample.</param>
  public MainWindow(string? startup)
  {
    _startup = startup;
    InitializeComponent();
    Host.ViewerCreated += Host_ViewerCreated;
    if (!OperatingSystem.IsWindows())
    {
      Status.Text = "This demo hosts the OCCT view as a Win32 window: it runs on Windows only.";
    }
  }

  // the command line's file, once
  private async void Host_ViewerCreated(object? sender, EventArgs e)
  {
    var startup = _startup;
    _startup = null;
    switch (startup)
    {
      case "--sample":
        Report(await _opener.OpenSampleAsync(() => Host.Viewer, Report));
        break;
      case not null:
        Report(await _opener.OpenAsync(startup, () => Host.Viewer, Report));
        break;
    }
  }

  private async void Open_Click(object? sender, RoutedEventArgs e)
  {
    var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
    {
      Title = "Open STEP",
      AllowMultiple = false,
      FileTypeFilter =
      [
        new FilePickerFileType("STEP files") { Patterns = ["*.step", "*.stp"] },
        FilePickerFileTypes.All
      ],
    });
    if (files is [var file] && file.TryGetLocalPath() is { } path)
    {
      Report(await _opener.OpenAsync(path, () => Host.Viewer, Report));
    }
  }

  private async void OpenSample_Click(object? sender, RoutedEventArgs e) =>
    Report(await _opener.OpenSampleAsync(() => Host.Viewer, Report));

  private void Exit_Click(object? sender, RoutedEventArgs e) => Close();

  private void FitAll_Click(object? sender, RoutedEventArgs e) => Host.Viewer?.FitAll();

  private void Report(string status) => Status.Text = status;
}
