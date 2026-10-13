// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace OCC.Core;

/// <summary>
/// NetOccVtk, NetOcc.Vtk's native library, loads as NetOcc's do (NetOcc's NativeLibraryLoader, the
/// same directories). It needs VTK's libraries, NetVtk.runtime's, which may be in another directory
/// (runtimes\&lt;rid&gt;\native where NetOcc's are in x64\): NetVtk loads them first, and the OS
/// loader then finds them loaded.
/// </summary>
internal static class NativeLibraries
{
  /// <summary>The VTK version NetVtk's libraries are.</summary>
  internal static string VtkVersion { get; private set; }

  // CA2255: a library module initializer is the point here; it must run before any P/Invoke.
#pragma warning disable CA2255
  [ModuleInitializer]
#pragma warning restore CA2255
  internal static void Initialize()
  {
    VtkVersion = Vtk.vtkVersion.GetVTKVersion();
    NativeLibraryLoader.ResolveFor(typeof(NativeLibraries).Assembly);
  }
}
