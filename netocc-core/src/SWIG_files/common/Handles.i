// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * OCCT handles (Standard_Transient hierarchy).
 *
 * The C# proxy is the object: it holds the raw T* and owns exactly one
 * reference. SWIG's ref/unref features add that reference after construction
 * and drop it in Dispose/the finalizer; Handle(T) parameters and returns are
 * converted at the boundary. The counter is atomic, so the GC finalizer
 * thread may release.
 */

%{
#include <Standard_Handle.hxx>
%}

namespace opencascade { template <class T> class handle; }

// the reference a proxy owns, which SWIG adds after construction and drops in Dispose or the finalizer
%define %netocc_refcounted(TYPE)
%feature("ref")   TYPE "$this->IncrementRefCounter();"
%feature("unref") TYPE "if ($this->DecrementRefCounter() == 0) $this->Delete();"
%enddef

%netocc_refcounted(Standard_Transient)

// Handle(TYPE) <-> CSTYPE proxy. Use once per transient class, before its declaration. CSTYPE is the
// proxy's C# name: TYPE for classes, the %template name for template instances (NCollection_HArray1< double >
// is TColStd_HArray1OfReal).
%define %occt_handle(TYPE, CSTYPE)
%typemap(ctype)  opencascade::handle< TYPE >, const opencascade::handle< TYPE >& "void *"
%typemap(imtype, out="global::System.IntPtr") opencascade::handle< TYPE >, const opencascade::handle< TYPE >& "global::System.Runtime.InteropServices.HandleRef"
%typemap(cstype) opencascade::handle< TYPE >, const opencascade::handle< TYPE >& "CSTYPE"
%typemap(csin)   opencascade::handle< TYPE >, const opencascade::handle< TYPE >& "CSTYPE.getCPtr($csinput)"
%typemap(in) opencascade::handle< TYPE > %{
  $1 = opencascade::handle< TYPE >(static_cast< TYPE* >($input));
%}
%typemap(in) const opencascade::handle< TYPE >& (opencascade::handle< TYPE > temp) %{
  temp = opencascade::handle< TYPE >(static_cast< TYPE* >($input));
  $1 = &temp;
%}
%typemap(out) opencascade::handle< TYPE > %{
  { TYPE* p = $1.get(); if (p) p->IncrementRefCounter(); $result = (void*)p; }
%}
// a non-const Handle(TYPE)& return is the object too
%typemap(out) const opencascade::handle< TYPE >&, opencascade::handle< TYPE >& %{
  { TYPE* p = $1->get(); if (p) p->IncrementRefCounter(); $result = (void*)p; }
%}
// the object, and so is a returned TYPE* (below)
%typemap(csout, excode=SWIGEXCODE) opencascade::handle< TYPE >, const opencascade::handle< TYPE >&, opencascade::handle< TYPE >&,
                                   TYPE *const, const TYPE *const {
    global::System.IntPtr cPtr = $imcall;
    CSTYPE ret = (cPtr == global::System.IntPtr.Zero) ? null : new CSTYPE(cPtr, true);$excode
    return ret;
  }

/*
 * Handle(TYPE)& and TYPE*& (in/out) -> ref CSTYPE. The slot carries the caller's object in; an object the callee put in
 * its place comes back with one reference for its new proxy. Otherwise the slot still holds the caller's pointer and the
 * variable keeps its proxy: after a C++ exception, which skips argout, and after an argument check that returns before the
 * call (a null class reference), which skips the rest of the wrapper.
 */
%typemap(ctype, out="void *") opencascade::handle< TYPE >& "void **"
%typemap(imtype, out="global::System.IntPtr") opencascade::handle< TYPE >& "ref global::System.IntPtr"
%typemap(cstype, out="CSTYPE") opencascade::handle< TYPE >& "ref CSTYPE"
%typemap(in) opencascade::handle< TYPE >& (opencascade::handle< TYPE > temp) %{
  temp = opencascade::handle< TYPE >(static_cast< TYPE* >(*$input));
  $1 = &temp;
%}
%typemap(argout) opencascade::handle< TYPE >& %{
  { TYPE* p = temp$argnum.get(); if (p != static_cast< TYPE* >(*$input)) { if (p) p->IncrementRefCounter(); *$input = (void*)p; } }
%}
// SWIG falls back from const T& to T& for typemaps missing on const T&: keep argout off const handles.
%typemap(argout) const opencascade::handle< TYPE >& ""
%typemap(argout) TYPE *& %{
  if (temp$argnum != *(TYPE**)$input && temp$argnum) temp$argnum->IncrementRefCounter();
  *$input = (void*)temp$argnum;
%}
// cshin: in a constructor SWIG moves pre/post into a SwigConstruct helper, whose call needs the ref too
%typemap(csin,
         pre="    CSTYPE keep$csinput = $csinput; global::System.IntPtr slot$csinput = CSTYPE.getCPtr(keep$csinput).Handle, old$csinput = slot$csinput;",
         post="      global::System.GC.KeepAlive(keep$csinput);"
              " if (slot$csinput != old$csinput) $csinput = (slot$csinput == global::System.IntPtr.Zero) ? null : new CSTYPE(slot$csinput, true);",
         cshin="ref $csinput")
         opencascade::handle< TYPE >&, TYPE *& "ref slot$csinput"

// A returned TYPE* is the object, like a returned handle: the proxy owns a reference (References.i's borrowing
// proxies are for classes without a reference count). OCCT holds transients by handle, so the count is live.
// netocc-gen spells these returns TYPE* const: SWIG applies the out typemap of TYPE* to constructors too, whose
// reference the ref feature adds.
%typemap(out) TYPE *const, const TYPE *const %{
  { TYPE* p = const_cast< TYPE* >($1); if (p) p->IncrementRefCounter(); $result = (void*)p; }
%}
%enddef

// Handle(TYPE)::DownCast as a static method: Geom_Plane.DownCast(surface), null on mismatch.
%define %occt_downcast(TYPE)
%extend TYPE {
  static opencascade::handle< TYPE > DownCast(const opencascade::handle< Standard_Transient >& theObject) {
    return opencascade::handle< TYPE >::DownCast(theObject);
  }
}
%enddef

%define %occt_transient(TYPE)
%occt_handle(TYPE, TYPE)
%occt_downcast(TYPE)
%enddef

// A handle-managed template instance (NCollection_HArray1< double >). For C#, SWIG keeps a class's first base
// only, the collection, so the instance gets Standard_Transient's reference counting itself.
%define %occt_transient_instance(TYPE, CSTYPE)
%occt_handle(TYPE, CSTYPE)
%netocc_refcounted(TYPE)
%occt_downcast(TYPE)
%enddef
