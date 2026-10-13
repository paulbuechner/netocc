// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * IVtk's companion (hand-written; the generated module includes it): the VTK build TKIVtk and NetOccVtk are compiled
 * against, which the tests compare with NetVtk's at run time (they must be the same build: NetVtkInterop.i).
 */

%{
#include <vtkVersionMacros.h>
%}

%inline %{
inline const char* NetOccVtk_VtkVersion() { return VTK_VERSION; }
inline int NetOccVtk_IdTypeSize() { return (int)sizeof(vtkIdType); }
%}
