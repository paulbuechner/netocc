// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * NetOcc common SWIG setup. First %include of every module .i.
 *
 * C# layout: one SWIG module per OCCT package, C# namespace
 * OCC.Core.<Package>, OCCT class and method names unchanged.
 */

%begin %{
#if defined(_MSC_VER)
// conversions in SWIG's wrappers (4244, 4267); OCCT's exported classes with members or bases of other DLLs (4251, 4275)
#pragma warning(disable : 4244 4267 4251 4275)
#endif
%}

%{
#include <exception>
#include <functional>
#include <iterator>
#include <type_traits>
#include <Standard_Failure.hxx>
#include <Standard_OutOfRange.hxx>
#include <Standard_Transient.hxx>
#include <Standard_Type.hxx>

// The buffer a wrapper returns a value's address in, per thread, type and position (0: the return, n: argument n): C#
// reads it before its next native call, so one per type serves every wrapper of a library.
template <class T, int Position = 0>
inline T& NetOcc_Slot()
{
  static thread_local T theSlot;
  return theSlot;
}
%}

// Constructors are wrapped only when declared explicitly (OCCT has many
// protected or abstract bases).
%nodefaultctor;

/*
 * C# usings of the generated files: System, plus what the module passes, the namespaces of its
 * %import closure. Every module calls %netocc_csimports after its imports; the last call wins.
 */
%define %netocc_csimports(USINGS)
%typemap(csimports) SWIGTYPE %{
using global::System;
using global::System.Runtime.InteropServices;
USINGS
%}
%pragma(csharp) imclassimports=%{
using global::System;
using global::System.Runtime.InteropServices;
USINGS
%}
%pragma(csharp) moduleimports=%{
using global::System;
using global::System.Runtime.InteropServices;
USINGS
%}
%enddef

%netocc_csimports()

// Module and intermediary classes are plumbing; the API is the proxy classes.
%pragma(csharp) moduleclassmodifiers="internal class"
%pragma(csharp) imclassclassmodifiers="internal class"

%include "Types.i"
%include "References.i"
%include "Exceptions.i"
%include "Directors.i"
%include "Strings.i"
%include "ValueTypes.i"
%include "Std.i"
%include "Streams.i"
%include "Guid.i"
%include "Handles.i"
%include "Collections.i"
