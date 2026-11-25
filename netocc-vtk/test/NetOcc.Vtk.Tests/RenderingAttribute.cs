// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

// NUnit
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal.Commands;

namespace NetOcc.Vtk.Tests;

/// <summary>
/// A test that renders: the category Rendering, which build.py test --no-rendering leaves out. On
/// macOS it runs on the process's main thread, which AppKit wants for VTK's Cocoa window even off
/// the screen and NUnit's workers aren't: in a process of its own (<see cref="MainThreadRun"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RenderingAttribute() : CategoryAttribute("Rendering"), IWrapSetUpTearDown
{
  public TestCommand Wrap(TestCommand command) =>
#if NET5_0_OR_GREATER
    MainThreadRun.Needed ? MainThreadRun.Wrap(command) : command;
#else
    command;
#endif
}
