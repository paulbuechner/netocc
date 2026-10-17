// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

//
using OCC.Core.IVtk;

namespace OCC.Core;

/// <summary>
/// NetOccVtk, NetOcc.Vtk's native library, loads as NetOcc's do (NetOcc's NativeLibraryLoader, the
/// same directories). It needs VTK's libraries, NetVtk.runtime's, which may be in another directory
/// (runtimes\&lt;rid&gt;\native where NetOcc's are in x64\): NetVtk loads them first, and the OS
/// loader then finds them loaded. TKIVtk and NetOccVtk must have been compiled against the VTK build
/// NetVtk's natives carry: <see cref="Vtk.VtkBuild.Require"/> throws where they weren't.
/// </summary>
internal static class NativeLibraries
{
  // CA2255: a library module initializer is the point here; it must run before any P/Invoke.
#pragma warning disable CA2255
  [ModuleInitializer]
#pragma warning restore CA2255
  internal static void Initialize()
  {
    Vtk.vtkVersion.GetVTKVersion();
    NativeLibraryLoader.ResolveFor(typeof(NativeLibraries).Assembly);
    Vtk.VtkBuild.Require(IVtkModule.NetOccVtk_VtkBuild(), "NetOcc.Vtk");
  }
}
