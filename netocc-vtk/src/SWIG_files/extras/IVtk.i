// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * IVtk's companion (hand-written; the generated module includes it): the VTK build TKIVtk and NetOccVtk are compiled
 * against (NetVtkInterop.i's %netvtk_build), which NetOcc.Vtk checks against NetVtk's when it loads (NativeLibraries).
 */

%include "NetVtkInterop.i"

%netvtk_build(NetOccVtk_VtkBuild)

%inline %{
inline int NetOccVtk_IdTypeSize() { return (int)sizeof(vtkIdType); }
%}
