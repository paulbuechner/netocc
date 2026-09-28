// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * Strings, in both directions: UTF-8 (const char*, std::string, TCollection_AsciiString) and UTF-16 (const char16_t*,
 * TCollection_ExtendedString). OCCT reads file paths as UTF-8, while default P/Invoke char* marshaling is ANSI on Windows
 * (breaks C:\Daten\Übersicht\...). netstandard2.0 has no UnmanagedType.LPUTF8Str, so encode/decode by hand.
 */

// a ref string parameter: the C# string, copied natively (ALLOC), goes in through the slot, and what the slot points to
// after the call comes back (DECODE); a slot the callee didn't change (after a C++ exception) gives the input back.
// cshin: constructors call a SwigConstruct helper that holds pre/post; its call needs the ref too.
%define %netocc_ref_string_csin(TYPE, ALLOC, DECODE)
%typemap(csin,
         pre="    global::System.IntPtr buffer$csinput = ALLOC($csinput); global::System.IntPtr slot$csinput = buffer$csinput;",
         post="      $csinput = DECODE(slot$csinput); global::System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer$csinput);",
         cshin="ref $csinput")
         TYPE "ref slot$csinput"
%enddef

// const char* parameters: null-terminated UTF-8 byte[] (pinned, no copy on the native side)
%typemap(ctype, out="void *") const char*, const char* const "char *"
%typemap(imtype, out="global::System.IntPtr") const char*, const char* const "byte[]"
%typemap(cstype) const char*, const char* const "string"
%typemap(csin) const char*, const char* const "global::OCC.Core.Utf8.Encode($csinput)"
%typemap(in) const char*, const char* const %{ $1 = ($1_ltype)$input; %}

// const char* returns: decoded immediately (the pointer is owned by OCCT)
%typemap(out) const char*, const char* const %{ $result = (void*)$1; %}
%typemap(csout, excode=SWIGEXCODE) const char*, const char* const {
    string ret = global::OCC.Core.Utf8.Decode($imcall);$excode
    return ret;
  }

/*
 * const char*& -> ref string: in, a UTF-8 copy the callee may read and move along (XmlObjMgt::GetReal
 * parses from it); out, whatever the pointer then points to, decoded right after the call. After a C++
 * exception the variable keeps its value.
 */
%typemap(ctype, out="void *")  const char*& "const char **"
%typemap(imtype, out="global::System.IntPtr") const char*& "ref global::System.IntPtr"
%typemap(cstype, out="ref global::System.IntPtr") const char*& "ref string"
%netocc_ref_string_csin(const char*&, global::OCC.Core.Utf8.Alloc, global::OCC.Core.Utf8.Decode)
%typemap(in) const char*& (const char* temp) %{ temp = *$input; $1 = ($1_ltype)&temp; %}
%typemap(argout) const char*& %{ *$input = temp$argnum; %}
// SWIG falls back from const T& to T& for typemaps missing on const T&
%typemap(argout) const char* const& ""
// a member's const char*& return: a ref IntPtr into the object, as for other pointers (References.i)
%netocc_ref_out(const char*&, global::System.IntPtr)

// const char16_t* (Standard_ExtString): UTF-16 in both directions, like TCollection_ExtendedString
%typecheck(SWIG_TYPECHECK_STRING) const char16_t*, const char16_t* const ""
%typemap(ctype, out="void *") const char16_t*, const char16_t* const "const char16_t *"
%typemap(imtype, out="global::System.IntPtr",
         inattributes="[global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.LPWStr)]")
         const char16_t*, const char16_t* const "string"
%typemap(cstype) const char16_t*, const char16_t* const "string"
%typemap(csin) const char16_t*, const char16_t* const "$csinput"
%typemap(in) const char16_t*, const char16_t* const %{ $1 = ($1_ltype)$input; %}
%typemap(out) const char16_t*, const char16_t* const %{ $result = (void*)$1; %}
%typemap(csout, excode=SWIGEXCODE) const char16_t*, const char16_t* const {
    global::System.IntPtr ptr = $imcall;$excode
    return global::System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr);
  }

/*
 * std::string and std::string_view: UTF-8, like const char*. A const std::stringstream& (Standard_SStream: OCCT's JSON
 * dumps, message streams) is its text, a UTF-8 string too. Returns are copied into a thread-local buffer first.
 */
%{
#include <sstream>
#include <string>
#include <string_view>
%}

%typemap(ctype, out="void *") std::string, const std::string&, std::string_view, const std::string_view&, const std::stringstream& "char *"
%typemap(imtype, out="global::System.IntPtr") std::string, const std::string&, std::string_view, const std::string_view&, const std::stringstream& "byte[]"
%typemap(cstype) std::string, const std::string&, std::string_view, const std::string_view&, const std::stringstream& "string"
%typemap(csin) std::string, const std::string&, std::string_view, const std::string_view&, const std::stringstream& "global::OCC.Core.Utf8.Encode($csinput)"
%typemap(in) std::string %{ if ($input) $1 = std::string((const char*)$input); %}
%typemap(in) const std::string& (std::string temp) %{
  if ($input) temp = (const char*)$input;
  $1 = &temp;
%}
// the pinned C# bytes, for the call
%typemap(in) std::string_view %{ if ($input) $1 = std::string_view((const char*)$input); %}
%typemap(in) const std::string_view& (std::string_view temp) %{
  if ($input) temp = std::string_view((const char*)$input);
  $1 = &temp;
%}
%typemap(in) const std::stringstream& (std::stringstream temp) %{
  if ($input) temp << (const char*)$input;
  $1 = &temp;
%}
%typemap(out, null="0") std::string, std::string_view %{
  { std::string& netocc_ret = NetOcc_Slot< std::string >(); netocc_ret = $1; $result = (void*)netocc_ret.c_str(); }
%}
%typemap(out, null="0") const std::string&, const std::string_view& %{
  { std::string& netocc_ret = NetOcc_Slot< std::string >(); netocc_ret = *$1; $result = (void*)netocc_ret.c_str(); }
%}
%typemap(out, null="0") const std::stringstream& %{
  { std::string& netocc_ret = NetOcc_Slot< std::string >(); netocc_ret = $1->str(); $result = (void*)netocc_ret.c_str(); }
%}
%typemap(csout, excode=SWIGEXCODE) std::string, const std::string&, std::string_view, const std::string_view&, const std::stringstream& {
    global::System.IntPtr ptr = $imcall;$excode
    return global::OCC.Core.Utf8.Decode(ptr);
  }

/*
 * TCollection strings are C# strings; no proxy classes.
 *
 *   TCollection_AsciiString      bytes, read and written as UTF-8 (paths, entries)
 *   TCollection_ExtendedString   UTF-16, passed as LPWStr (char16_t on every platform)
 *
 *   TYPE / const TYPE&  parameter  -> string       (null = empty)
 *   TYPE& parameter                -> ref string   (in/out)
 *   TYPE / const TYPE&  return     -> string       (copied into a thread-local buffer)
 *   TYPE& return                   -> string       (a copy too: C# strings are immutable)
 *
 * Returns are copied natively first: the owner of a const& result may be a proxy
 * that the GC finalizes as soon as the P/Invoke call returns.
 */
%{
#include <TCollection_AsciiString.hxx>
#include <TCollection_ExtendedString.hxx>
%}

%typemap(ctype, out="void *") TCollection_AsciiString, const TCollection_AsciiString& "const char *"
%typemap(imtype, out="global::System.IntPtr") TCollection_AsciiString, const TCollection_AsciiString& "byte[]"
%typemap(cstype) TCollection_AsciiString, const TCollection_AsciiString& "string"
%typemap(csin) TCollection_AsciiString, const TCollection_AsciiString& "global::OCC.Core.Utf8.Encode($csinput)"
%typemap(in) TCollection_AsciiString %{ if ($input) $1 = TCollection_AsciiString($input); %}
%typemap(in) const TCollection_AsciiString& (TCollection_AsciiString temp) %{
  if ($input) temp = TCollection_AsciiString($input);
  $1 = &temp;
%}
%typemap(out, null="0") TCollection_AsciiString %{
  { TCollection_AsciiString& netocc_ret = NetOcc_Slot< TCollection_AsciiString >(); netocc_ret = $1; $result = (void*)netocc_ret.ToCString(); }
%}
// a non-const TYPE& return is a read-only copy too
%typemap(out, null="0") const TCollection_AsciiString&, TCollection_AsciiString& %{
  { TCollection_AsciiString& netocc_ret = NetOcc_Slot< TCollection_AsciiString >(); netocc_ret = *$1; $result = (void*)netocc_ret.ToCString(); }
%}
%typemap(csout, excode=SWIGEXCODE) TCollection_AsciiString, const TCollection_AsciiString&, TCollection_AsciiString& {
    global::System.IntPtr ptr = $imcall;$excode
    return global::OCC.Core.Utf8.Decode(ptr);
  }

%typemap(ctype, out="void *") TCollection_ExtendedString, const TCollection_ExtendedString& "const char16_t *"
%typemap(imtype, out="global::System.IntPtr",
         inattributes="[global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.LPWStr)]")
         TCollection_ExtendedString, const TCollection_ExtendedString& "string"
%typemap(cstype) TCollection_ExtendedString, const TCollection_ExtendedString& "string"
%typemap(csin) TCollection_ExtendedString, const TCollection_ExtendedString& "$csinput"
%typemap(in) TCollection_ExtendedString %{ if ($input) $1 = TCollection_ExtendedString($input); %}
%typemap(in) const TCollection_ExtendedString& (TCollection_ExtendedString temp) %{
  if ($input) temp = TCollection_ExtendedString($input);
  $1 = &temp;
%}
%typemap(out, null="0") TCollection_ExtendedString %{
  { TCollection_ExtendedString& netocc_ret = NetOcc_Slot< TCollection_ExtendedString >(); netocc_ret = $1; $result = (void*)netocc_ret.ToExtString(); }
%}
%typemap(out, null="0") const TCollection_ExtendedString&, TCollection_ExtendedString& %{
  { TCollection_ExtendedString& netocc_ret = NetOcc_Slot< TCollection_ExtendedString >(); netocc_ret = *$1; $result = (void*)netocc_ret.ToExtString(); }
%}
%typemap(csout, excode=SWIGEXCODE) TCollection_ExtendedString, const TCollection_ExtendedString&, TCollection_ExtendedString& {
    global::System.IntPtr ptr = $imcall;$excode
    return global::System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr);
  }

/*
 * TYPE& -> ref string. The slot carries the input string in and the address of a
 * thread-local copy of the result out; C# decodes it right after the call. After
 * a C++ exception the slot still holds the input, so the variable keeps its value.
 */
%typemap(ctype, out="void *") TCollection_AsciiString&, TCollection_ExtendedString& "void **"
%typemap(imtype, out="global::System.IntPtr") TCollection_AsciiString&, TCollection_ExtendedString& "ref global::System.IntPtr"
%typemap(cstype, out="string") TCollection_AsciiString&, TCollection_ExtendedString& "ref string"
%netocc_ref_string_csin(TCollection_AsciiString&, global::OCC.Core.Utf8.Alloc, global::OCC.Core.Utf8.Decode)
%netocc_ref_string_csin(TCollection_ExtendedString&, global::System.Runtime.InteropServices.Marshal.StringToHGlobalUni,
                        global::System.Runtime.InteropServices.Marshal.PtrToStringUni)
%typemap(in) TCollection_AsciiString& (TCollection_AsciiString temp) %{
  if (*$input) temp = TCollection_AsciiString((const char*)*$input);
  $1 = &temp;
%}
%typemap(in) TCollection_ExtendedString& (TCollection_ExtendedString temp) %{
  if (*$input) temp = TCollection_ExtendedString((const char16_t*)*$input);
  $1 = &temp;
%}
%typemap(argout) TCollection_AsciiString& %{
  { TCollection_AsciiString& netocc_out = NetOcc_Slot< TCollection_AsciiString, $argnum >(); netocc_out = temp$argnum; *$input = (void*)netocc_out.ToCString(); }
%}
%typemap(argout) TCollection_ExtendedString& %{
  { TCollection_ExtendedString& netocc_out = NetOcc_Slot< TCollection_ExtendedString, $argnum >(); netocc_out = temp$argnum; *$input = (void*)netocc_out.ToExtString(); }
%}

// SWIG falls back from const T& to T& for typemaps missing on const T&: keep argout off const strings.
%typemap(argout) const TCollection_AsciiString&, const TCollection_ExtendedString& ""

// director callbacks (Directors.i): a string parameter reaches the C# override decoded
%typemap(directorin) TCollection_AsciiString, const TCollection_AsciiString& "$input = (void*)$1.ToCString();"
%typemap(csdirectorin) TCollection_AsciiString, const TCollection_AsciiString& "global::OCC.Core.Utf8.Decode($iminput)"
%typemap(directorin) TCollection_ExtendedString, const TCollection_ExtendedString& "$input = (void*)$1.ToExtString();"
%typemap(csdirectorin) TCollection_ExtendedString, const TCollection_ExtendedString&
  "global::System.Runtime.InteropServices.Marshal.PtrToStringUni($iminput)"
%typemap(directorin) const char*, const char* const "$input = (void*)$1;"
%typemap(csdirectorin) const char*, const char* const "global::OCC.Core.Utf8.Decode($iminput)"
