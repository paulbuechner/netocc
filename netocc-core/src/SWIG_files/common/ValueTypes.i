// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * Value types: blittable C# structs with the exact C++ layout (netocc-gen writes
 * src/NetOcc/<Package>/<Type>.g.cs next to a hand-written partial). SWIG never builds
 * proxies for them; these typemaps marshal them in other classes' signatures.
 *
 *   TYPE / const TYPE&   parameter  -> TYPE / in TYPE   (pointer to the C# struct)
 *   TYPE&                parameter  -> ref TYPE         (callee writes the caller's struct)
 *   TYPE* / const TYPE*  parameter  -> TYPE[]           (pinned array, as for numbers: see Types.i)
 *   TYPE / const TYPE&   return     -> TYPE             (via a thread-local buffer, see below)
 *   TYPE&                return     -> ref TYPE         (into the native owner, see References.i)
 *
 * Returns never pass a struct by value across the C ABI: on x86 __stdcall the hidden
 * return pointer is part of the export's _Name@N decoration, which .NET computes from
 * the managed signature (without it) -> EntryPointNotFoundException. The wrapper copies
 * the result into a thread-local buffer per type (NetOcc_Slot, NetOcc.i) and returns its
 * address; C# checks for a pending exception first, then reads the struct (one unmanaged copy).
 */
// TYPE as the C# struct CSTYPE of its layout, as above (not the arrays)
%define %netocc_struct(TYPE, CSTYPE)
%typemap(ctype, out="void *") TYPE, const TYPE&, TYPE& "TYPE *"
%typemap(imtype, out="global::System.IntPtr") TYPE, TYPE& "ref CSTYPE"
%typemap(imtype, out="global::System.IntPtr") const TYPE& "in CSTYPE"
%typemap(cstype, out="CSTYPE") TYPE "CSTYPE"
%typemap(cstype, out="CSTYPE") const TYPE& "in CSTYPE"
%typemap(cstype, out="ref CSTYPE") TYPE& "ref CSTYPE"
%typemap(csin) TYPE, TYPE& "ref $csinput"
%typemap(csin) const TYPE& "in $csinput"
%typemap(in) TYPE %{ $1 = *($&1_ltype)$input; %}
%typemap(in) const TYPE&, TYPE& %{ $1 = ($1_ltype)$input; %}
%typemap(out, null="0") TYPE %{
  { TYPE& netocc_ret = NetOcc_Slot< TYPE >(); netocc_ret = $1; $result = (void*)&netocc_ret; }
%}
%typemap(out, null="0") const TYPE& %{
  { TYPE& netocc_ret = NetOcc_Slot< TYPE >(); netocc_ret = *$1; $result = (void*)&netocc_ret; }
%}
%typemap(out, null="0") TYPE& %{ $result = (void*)$1; %}
%typemap(csout, excode=SWIGEXCODE) TYPE, const TYPE& {
    global::System.IntPtr ptr = $imcall;$excode
    return global::OCC.Core.NativeStruct.Read<CSTYPE>(ptr);
  }
%typemap(csout, excode=SWIGEXCODE) TYPE& {
    global::System.IntPtr ptr = $imcall;$excode
    unsafe { return ref *(CSTYPE*)ptr; }
  }
%enddef

%define %occt_valuetype(TYPE)
%netocc_struct(TYPE, TYPE)
%netocc_pointer_arrays(TYPE, TYPE)
%enddef

/*
 * Value classes: copyable non-transient classes wrapped as proxies
 * (TopoDS_Shape, TopLoc_Location, NCollection_List<T>, ...). A const& return
 * points into its parent, so it is copied into a proxy that owns the copy.
 */
%define %occt_valueclass(TYPE)
%typemap(out) const TYPE& %{ $result = (void*)new TYPE(*$1); %}
%typemap(csout, excode=SWIGEXCODE) const TYPE& {
    global::System.IntPtr cPtr = $imcall;
    $csclassname ret = (cPtr == global::System.IntPtr.Zero) ? null : new $csclassname(cPtr, true);$excode
    return ret;
  }
%enddef
