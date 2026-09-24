// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * Standard_GUID <-> System.Guid.
 *
 * OCCT 8's Standard_UUID {uint32; uint16; uint16; uint8[8]} has System.Guid's
 * memory layout, and Standard_GUID converts to and from it, so the C# Guid is
 * passed by address and read back from a thread-local Standard_UUID.
 */
%{
#include <Standard_GUID.hxx>
#include <Standard_UUID.hxx>
static_assert(sizeof(Standard_UUID) == 16 && alignof(Standard_UUID) == 4, "Standard_UUID must match System.Guid");
static_assert(std::is_trivially_copyable<Standard_UUID>::value, "Standard_UUID must be trivially copyable");
%}

%typemap(ctype, out="void *") Standard_GUID, const Standard_GUID& "const Standard_UUID *"
%typemap(imtype, out="global::System.IntPtr") Standard_GUID, const Standard_GUID& "in global::System.Guid"
%typemap(cstype) Standard_GUID, const Standard_GUID& "global::System.Guid"
%typemap(csin) Standard_GUID, const Standard_GUID& "in $csinput"
%typemap(in) Standard_GUID %{ $1 = Standard_GUID(*$input); %}
// each type its local
%typemap(in) const Standard_GUID& (Standard_GUID temp), Standard_GUID& (Standard_GUID temp) %{
  temp = Standard_GUID(*$input);
  $1 = &temp;
%}
%typemap(out, null="0") Standard_GUID %{
  { Standard_UUID& netocc_ret = NetOcc_Slot< Standard_UUID >(); netocc_ret = $1.ToUUID(); $result = (void*)&netocc_ret; }
%}
%typemap(out, null="0") const Standard_GUID& %{
  { Standard_UUID& netocc_ret = NetOcc_Slot< Standard_UUID >(); netocc_ret = $1->ToUUID(); $result = (void*)&netocc_ret; }
%}
%typemap(csout, excode=SWIGEXCODE) Standard_GUID, const Standard_GUID& {
    global::System.IntPtr ptr = $imcall;$excode
    return global::OCC.Core.NativeStruct.Read<global::System.Guid>(ptr);
  }

// Standard_GUID& (in/out): ref Guid, the caller's written back after the call (not after a C++ exception)
%typemap(ctype) Standard_GUID& "Standard_UUID *"
%typemap(imtype) Standard_GUID& "ref global::System.Guid"
%typemap(cstype) Standard_GUID& "ref global::System.Guid"
%typemap(csin) Standard_GUID& "ref $csinput"
%typemap(argout) Standard_GUID& %{ *$input = temp$argnum.ToUUID(); %}
// SWIG falls back from const T& to T& for typemaps missing on const T&
%typemap(argout) const Standard_GUID& ""
