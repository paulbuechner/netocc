// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * Standard library types OCCT's signatures use (strings: Strings.i). netocc-gen calls a macro per instantiation, before
 * the declarations. An array, optional or complex number returned is copied into a thread-local buffer first, like a
 * struct (ValueTypes.i).
 *
 *   std::streampos                   long, a byte offset
 *   %netocc_bitset(N)                std::bitset<N> (N <= 64): a ulong mask, bit i the i-th flag
 *   %netocc_std_array(T, N, CSTYPE)  std::array<T, N> of numbers: a C# array of N elements; a non-const & writes it back
 *   %netocc_optional(T, CSTYPE)      std::optional<T> of a number, enum or struct: a C# nullable
 *   std::complex<double>             OCC.Core.Complex, a C# struct with its layout: passed like a value type (ValueTypes.i)
 *   %netocc_pair(NAME, T1, T2, CS1, CS2)
 *                                    std::pair<T1, T2>: a class with First and Second, copies of the values; netocc-gen
 *                                    instantiates it like a collection (OCCT's alias, or std_pair_<T1>_<T2>)
 */

%{
#include <algorithm>
#include <array>
#include <bitset>
#include <complex>
#include <ios>
#include <optional>
#include <utility>
%}

%typemap(ctype)  std::streampos "long long"
%typemap(imtype) std::streampos "long"
%typemap(cstype) std::streampos "long"
%typemap(csin)   std::streampos "$csinput"
%typemap(in)     std::streampos %{ $1 = std::streampos(static_cast<std::streamoff>($input)); %}
%typemap(out)    std::streampos %{ $result = static_cast<long long>(static_cast<std::streamoff>($1)); %}
%typemap(csout, excode=SWIGEXCODE) std::streampos {
    long ret = $imcall;$excode
    return ret;
  }

// a C# struct of its layout, like a value type
%netocc_struct(std::complex<double>, global::OCC.Core.Complex)

%define %netocc_bitset(N)
%typemap(ctype)  std::bitset< N >, const std::bitset< N >& "unsigned long long"
%typemap(imtype) std::bitset< N >, const std::bitset< N >& "ulong"
%typemap(cstype) std::bitset< N >, const std::bitset< N >& "ulong"
%typemap(csin)   std::bitset< N >, const std::bitset< N >& "$csinput"
%typemap(in) std::bitset< N > %{ $1 = std::bitset< N >($input); %}
%typemap(in) const std::bitset< N >& (std::bitset< N > temp) %{ temp = std::bitset< N >($input); $1 = &temp; %}
%typemap(out) std::bitset< N > %{ $result = static_cast<const std::bitset< N >&>($1).to_ullong(); %}
%typemap(out) const std::bitset< N >& %{ $result = $1->to_ullong(); %}
%typemap(csout, excode=SWIGEXCODE) std::bitset< N >, const std::bitset< N >& {
    ulong ret = $imcall;$excode
    return ret;
  }
%enddef

%define %netocc_std_array(TYPE, N, CSTYPE)
%typemap(ctype, out="void *") std::array< TYPE, N >, const std::array< TYPE, N >&, std::array< TYPE, N >& "TYPE *"
%typemap(imtype, out="global::System.IntPtr") std::array< TYPE, N >, const std::array< TYPE, N >&, std::array< TYPE, N >& "CSTYPE[]"
%typemap(cstype) std::array< TYPE, N >, const std::array< TYPE, N >&, std::array< TYPE, N >& "CSTYPE[]"
%typemap(csin) std::array< TYPE, N >, const std::array< TYPE, N >&, std::array< TYPE, N >&
         "global::OCC.Core.NativeArrays.Checked($csinput, N, \"$csinput\")"
%typemap(in) std::array< TYPE, N > %{ std::copy($input, $input + N, $1.begin()); %}
// each type its local ($*1_ltype: the array without const)
%typemap(in) const std::array< TYPE, N >& ($*1_ltype temp), std::array< TYPE, N >& ($*1_ltype temp) %{
  std::copy($input, $input + N, temp.begin());
  $1 = &temp;
%}
%typemap(argout) std::array< TYPE, N >& %{ std::copy(temp$argnum.begin(), temp$argnum.end(), $input); %}
// SWIG falls back from const T& to T& for typemaps missing on const T&
%typemap(argout) const std::array< TYPE, N >& ""
%typemap(out) std::array< TYPE, N > %{
  { std::array< TYPE, N >& netocc_ret = NetOcc_Slot< std::array< TYPE, N > >(); netocc_ret = $1; $result = netocc_ret.data(); }
%}
%typemap(out) const std::array< TYPE, N >& %{
  { std::array< TYPE, N >& netocc_ret = NetOcc_Slot< std::array< TYPE, N > >(); netocc_ret = *$1; $result = netocc_ret.data(); }
%}
%typemap(csout, excode=SWIGEXCODE) std::array< TYPE, N >, const std::array< TYPE, N >& {
    global::System.IntPtr ptr = $imcall;$excode
    var ret = new CSTYPE[N];
    unsafe { for (int i = 0; i < N; i++) ret[i] = ((CSTYPE*)ptr)[i]; }
    return ret;
  }
%enddef

// in: a C# array of one element, or null, which the marshaler pins; out: the value in the thread-local buffer, or null
%define %netocc_optional(TYPE, CSTYPE)
%typemap(ctype, out="void *") std::optional< TYPE >, const std::optional< TYPE >& "TYPE *"
%typemap(imtype, out="global::System.IntPtr") std::optional< TYPE >, const std::optional< TYPE >& "CSTYPE[]"
%typemap(cstype) std::optional< TYPE >, const std::optional< TYPE >& "CSTYPE?"
%typemap(csin) std::optional< TYPE >, const std::optional< TYPE >& "$csinput.HasValue ? new CSTYPE[] { $csinput.Value } : null"
%typemap(in) std::optional< TYPE > %{ if ($input) $1 = *$input; %}
%typemap(in) const std::optional< TYPE >& (std::optional< TYPE > temp) %{
  if ($input) temp = *$input;
  $1 = &temp;
%}
%typemap(out) std::optional< TYPE > %{
  { std::optional< TYPE >& netocc_ret = NetOcc_Slot< std::optional< TYPE > >(); netocc_ret = $1; $result = netocc_ret ? (void*)&*netocc_ret : nullptr; }
%}
%typemap(out) const std::optional< TYPE >& %{
  { std::optional< TYPE >& netocc_ret = NetOcc_Slot< std::optional< TYPE > >(); netocc_ret = *$1; $result = netocc_ret ? (void*)&*netocc_ret : nullptr; }
%}
%typemap(csout, excode=SWIGEXCODE) std::optional< TYPE >, const std::optional< TYPE >& {
    global::System.IntPtr ptr = $imcall;$excode
    unsafe { return ptr == global::System.IntPtr.Zero ? (CSTYPE?)null : *(CSTYPE*)ptr; }
  }
%enddef

// the accessors are %extend: a pair can't hold a nested enum, whose flat alias SWIG's casts ("enum X") can't follow
%csmethodmodifiers NetOcc_First "internal";
%csmethodmodifiers NetOcc_Second "internal";

namespace std {
template <class T1, class T2> struct pair {
  pair();
  pair(const T1& theFirst, const T2& theSecond);
  %extend {
    T1 NetOcc_First() const { return $self->first; }
    T2 NetOcc_Second() const { return $self->second; }
  }
};
}

%define %netocc_pair(NAME, T1, T2, CS1, CS2)
%typemap(cscode) std::pair< T1, T2 > %{
  public CS1 First => NetOcc_First();

  public CS2 Second => NetOcc_Second();
%}
%occt_valueclass(%arg(std::pair< T1, T2 >))
%template(NAME) std::pair< T1, T2 >;
%enddef
