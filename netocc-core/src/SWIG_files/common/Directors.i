// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * Directors: OCCT classes C# subclasses (netocc-gen's config directors), whose overrides OCCT calls through SWIG's
 * director callbacks.
 *
 * Exceptions. A C# exception can't unwind through C++. The callback's C# side catches what an override throws, parks
 * it in OCC.Core.Directors under a number and hands the number to its module (NetOccDirectorFailed_<module>); right
 * after the callback returns, the C++ side throws a NetOcc_ManagedException carrying it (NetOcc_DirectorCheck, in the
 * conversion of the result and of every parameter: directorout, directorargout). OCCT unwinds, the wrapper of the
 * outer call catches it as any Standard_Failure (Exceptions.i), and C# throws an OcctException whose InnerException is
 * the parked one. An exception OCCT catches itself goes no further, as in C++. SWIG runs no code after a callback
 * without parameters or result: netocc-gen leaves those members to C++.
 *
 * Lifetime and identity (OCC.Core.Directors): a director object lives while OCCT holds a reference, and OCCT handing
 * it back gives C# the same instance. %netocc_director marks a director class, %netocc_directed the director classes
 * and their bases, whose handles may be director objects.
 *
 * The conversions per type sit with the type's typemaps: Handles.i, ValueTypes.i, Strings.i, References.i.
 */

%{
#include <string>
#include <Standard_DefineException.hxx>
#include <Standard_NullObject.hxx>

#ifndef NETOCC_MANAGED_EXCEPTION
#define NETOCC_MANAGED_EXCEPTION
// what a C# override threw: its message is the number C# parked the exception under
DEFINE_STANDARD_EXCEPTION(NetOcc_ManagedException, Standard_Failure)
#endif

// the number of the exception a C# override on this thread threw, until the callback's C++ side throws it
static thread_local long long NetOcc_directorFailure = 0;

#ifdef __cplusplus
extern "C"
#endif
SWIGEXPORT void SWIGSTDCALL NetOccDirectorFailed_$module(long long theNumber) {
  NetOcc_directorFailure = theNumber;
}

// right after a director callback: throws what its C# side caught
static inline void NetOcc_DirectorCheck() {
  if (const long long aNumber = NetOcc_directorFailure) {
    NetOcc_directorFailure = 0;
    throw NetOcc_ManagedException(std::to_string(aNumber).c_str());
  }
}

// drops the reference a returned handle carried for a new proxy, when C# gives back the director object's own
#ifdef __cplusplus
extern "C"
#endif
SWIGEXPORT void SWIGSTDCALL NetOccDirectorRelease_$module(void* theObject) {
  Standard_Transient* anObject = static_cast<Standard_Transient*>(theObject);
  if (anObject->DecrementRefCounter() == 0) {
    anObject->Delete();
  }
}
%}

%pragma(csharp) imclasscode=%{
  [global::System.Runtime.InteropServices.DllImport("$dllimport", EntryPoint="NetOccDirectorFailed_$module")]
  public static extern void NetOccDirectorFailed(long theNumber);

  [global::System.Runtime.InteropServices.DllImport("$dllimport", EntryPoint="NetOccDirectorRelease_$module")]
  public static extern void NetOccDirectorRelease(global::System.IntPtr theObject);

  // for the callbacks' result conversions (Directors.Call) and the identity of returned objects (Directors.Adopt)
  internal static readonly global::System.Action<long> netoccDirectorFailed = NetOccDirectorFailed;
  internal static readonly global::System.Action<global::System.IntPtr> netoccRelease = NetOccDirectorRelease;
%}

// the C# side of a callback: an exception the override throws is parked, and the module told
%typemap(csdirectorout) void
  "try { $cscall; } catch (global::System.Exception netoccError) { $imclassname.NetOccDirectorFailed(global::OCC.Core.Directors.Park(netoccError)); }"
%define %netocc_director_call(TYPE)
%typemap(csdirectorout) TYPE "global::OCC.Core.Directors.Call(() => $cscall, $imclassname.netoccDirectorFailed)"
%enddef
%netocc_director_call(bool)
%netocc_director_call(signed char)
%netocc_director_call(unsigned char)
%netocc_director_call(short)
%netocc_director_call(unsigned short)
%netocc_director_call(int)
%netocc_director_call(unsigned int)
%netocc_director_call(long long)
%netocc_director_call(unsigned long long)
%netocc_director_call(float)
%netocc_director_call(double)
%typemap(csdirectorout) enum SWIGTYPE "(int)global::OCC.Core.Directors.Call(() => $cscall, $imclassname.netoccDirectorFailed)"

// the C++ side: a result's conversion, and every parameter's, rethrows what the C# side parked
%typemap(directorout) bool %{ NetOcc_DirectorCheck(); $result = $input ? true : false; %}
%define %netocc_director_number_out(TYPE)
%typemap(directorout) TYPE %{ NetOcc_DirectorCheck(); $result = ($1_ltype)$input; %}
%enddef
%netocc_director_number_out(signed char)
%netocc_director_number_out(unsigned char)
%netocc_director_number_out(short)
%netocc_director_number_out(unsigned short)
%netocc_director_number_out(int)
%netocc_director_number_out(unsigned int)
%netocc_director_number_out(long long)
%netocc_director_number_out(unsigned long long)
%netocc_director_number_out(float)
%netocc_director_number_out(double)
%netocc_director_number_out(enum SWIGTYPE)
%typemap(directorargout) SWIGTYPE, SWIGTYPE &, const SWIGTYPE &, SWIGTYPE *, enum SWIGTYPE, const enum SWIGTYPE & "NetOcc_DirectorCheck();"

/*
 * A director class: SWIG's C++ subclass calls the C# overrides back. After SWIG connects them, the object joins
 * OCC.Core.Directors, which keeps it alive while OCCT holds a reference. Dispose keeps the reference while OCCT does:
 * explicitly disposed, the object is released once OCCT lets go; finalized, it lives on.
 */
%define %netocc_director(TYPE)
%feature("director") TYPE;
%typemap(csconstruct, excode=SWIGEXCODE,
         directorconnect="\n    SwigDirectorConnect();\n    global::OCC.Core.Directors.Register(this, swigCPtr.Handle);") TYPE %{: this($imcall, true) {$excode$directorconnect
  }
%}
%typemap(csdisposing_derived, methodname="Dispose", methodmodifiers="protected", parameters="bool disposing") TYPE {
    lock(this) {
      if (global::OCC.Core.Directors.Holds(this, swigCPtr.Handle, disposing)) {
        return;
      }
      if (swigCPtr.Handle != global::System.IntPtr.Zero) {
        if (swigCMemOwn) {
          swigCMemOwn = false;
          $imcall;
        }
        swigCPtr = new global::System.Runtime.InteropServices.HandleRef(null, global::System.IntPtr.Zero);
      }
      base.Dispose(disposing);
    }
  }
%enddef

// A director class its proxy owns (not a transient): the application holds it, and its callbacks run while C# calls
// it, so the proxy's lifetime is the object's.
%define %netocc_director_owned(TYPE)
%feature("director") TYPE;
%enddef

/*
 * A director class or one of its bases: a handle of it may be a director object. Returned to C#, it is the object
 * itself (Directors.Adopt), and passed to OCCT, which may keep it, it stays alive (Directors.Passed, in getCPtr).
 */
%define %netocc_directed(TYPE)
%typemap(csout, excode=SWIGEXCODE) opencascade::handle< TYPE >, const opencascade::handle< TYPE >&, opencascade::handle< TYPE >&,
                                   TYPE *const, const TYPE *const {
    global::System.IntPtr cPtr = $imcall;
    TYPE ret = (cPtr == global::System.IntPtr.Zero) ? null : (global::OCC.Core.Directors.Adopt<TYPE>(cPtr, $imclassname.netoccRelease) ?? new TYPE(cPtr, true));$excode
    return ret;
  }
%typemap(csdirectorin) opencascade::handle< TYPE >, const opencascade::handle< TYPE >&
  "(($iminput == global::System.IntPtr.Zero) ? null : (global::OCC.Core.Directors.Adopt<TYPE>($iminput, $imclassname.netoccRelease) ?? new TYPE($iminput, true)))"
%typemap(csbody) TYPE %{
  private global::System.Runtime.InteropServices.HandleRef swigCPtr;
  protected bool swigCMemOwn;
  // what the native object depends on: a borrowed object's owner, or what the object keeps (References.i)
  internal object netoccOwner;

  internal $csclassname(global::System.IntPtr cPtr, bool cMemoryOwn) {
    swigCMemOwn = cMemoryOwn;
    swigCPtr = new global::System.Runtime.InteropServices.HandleRef(this, cPtr);
  }

  // passed to OCCT, which may keep a reference: a director object then stays alive (Directors.Passed)
  internal static global::System.Runtime.InteropServices.HandleRef getCPtr($csclassname obj) {
    if (obj == null) {
      return new global::System.Runtime.InteropServices.HandleRef(null, global::System.IntPtr.Zero);
    }
    global::OCC.Core.Directors.Passed(obj.swigCPtr.Handle);
    return obj.swigCPtr;
  }
%}
%typemap(csbody_derived) TYPE %{
  private global::System.Runtime.InteropServices.HandleRef swigCPtr;

  internal $csclassname(global::System.IntPtr cPtr, bool cMemoryOwn) : base($imclassname.$csclazznameSWIGUpcast(cPtr), cMemoryOwn) {
    swigCPtr = new global::System.Runtime.InteropServices.HandleRef(this, cPtr);
  }

  // passed to OCCT, which may keep a reference: a director object then stays alive (Directors.Passed)
  internal static global::System.Runtime.InteropServices.HandleRef getCPtr($csclassname obj) {
    if (obj == null) {
      return new global::System.Runtime.InteropServices.HandleRef(null, global::System.IntPtr.Zero);
    }
    global::OCC.Core.Directors.Passed(obj.swigCPtr.Handle);
    return obj.swigCPtr;
  }
%}
%enddef
