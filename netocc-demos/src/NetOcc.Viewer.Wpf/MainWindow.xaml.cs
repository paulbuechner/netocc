// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace NetOcc.Viewer.Wpf;

public partial class MainWindow : Window
{
  private readonly FileOpener _opener = new();

  public MainWindow() => InitializeComponent();

  // a STEP file given on the command line, or --sample, opens right away
  private async void Window_Loaded(object sender, RoutedEventArgs e)
  {
    switch (Environment.GetCommandLineArgs())
    {
      case [_, "--sample", ..]:
        await RunAsync(() => _opener.OpenSampleAsync(() => Host.Viewer, Report));
        break;
      case [_, var path, ..]:
        await RunAsync(() => _opener.OpenAsync(path, () => Host.Viewer, Report));
        break;
    }
  }

  private void Window_Closed(object sender, EventArgs e) => Host.Dispose();

  private async void Open_Click(object sender, RoutedEventArgs e)
  {
    var dialog = new Microsoft.Win32.OpenFileDialog
    {
      Title = "Open STEP", Filter = "STEP files (*.step;*.stp)|*.step;*.stp|All files (*.*)|*.*"
    };
    if (dialog.ShowDialog(this) == true)
    {
      await RunAsync(() => _opener.OpenAsync(dialog.FileName, () => Host.Viewer, Report));
    }
  }

  private async void OpenSample_Click(object sender, RoutedEventArgs e) =>
    await RunAsync(() => _opener.OpenSampleAsync(() => Host.Viewer, Report));

  private void Exit_Click(object sender, RoutedEventArgs e) => Close();

  private void FitAll_Click(object sender, RoutedEventArgs e) => Host.Viewer.FitAll();

  private void Report(string status) => Status.Text = status;

  // the wait cursor while a file opens, its outcome in the status line
  private async Task RunAsync(Func<Task<string>> open)
  {
    Cursor = Cursors.Wait;
    try
    {
      Report(await open());
    }
    finally
    {
      Cursor = null;
    }
  }
}
