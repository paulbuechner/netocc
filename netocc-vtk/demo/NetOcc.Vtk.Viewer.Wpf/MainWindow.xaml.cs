// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Windows;

namespace NetOcc.Vtk.Viewer;

public partial class MainWindow : Window
{
  public MainWindow()
  {
    ShapeViewer.InitializeOnUiThread();
    InitializeComponent();
    Host.Viewer.StatusChanged += (_, text) => Status.Text = text;
  }
}
