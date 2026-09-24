// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * Numbers: by value, by reference (parameters; the fixed-width builtins, which members return by reference too:
 * References.i), pointers to them (C# arrays) and addresses. netocc-gen resolves OCCT's typedefs (Standard_Real).
 *
 * Contract: a non-const C++ reference is a C# `ref` parameter, never `out`,
 * because some OCCT reference parameters are read as well as written.
 */

%include <typemaps.i>

// char is its byte (its sign differs per platform), by value and by reference: a C# char would pass through a code page.
// char* is a byte buffer (below), const char* a string (Strings.i). char32_t is a UTF-32 code point, a uint.
%apply unsigned char { char };
%apply const unsigned char& { const char& };
%apply unsigned char& INOUT { char& };
%apply unsigned int { char32_t };
%apply const unsigned int& { const char32_t& };
// width typedefs by their written names (int64_t is long on Linux, long long on Windows)
%apply unsigned char& INOUT { uint8_t& };
%apply signed char& INOUT { int8_t& };
%apply short& INOUT { int16_t& };
%apply unsigned short& INOUT { uint16_t& };
%apply int& INOUT { int32_t& };
%apply unsigned int& INOUT { uint32_t& };
%apply long long& INOUT { int64_t& };
%apply unsigned long long& INOUT { uint64_t& };

/*
 * C long: 32 bits on Windows, 64 on Linux and macOS. A C# long, 64 bits at the P/Invoke layer; a value a 32-bit long can't
 * hold throws OcctException (Standard_OutOfRange) instead of wrapping around. long& is in/out through a C long local.
 */
%{
#include <climits>
%}
%typemap(ctype) long, const long& "long long"
%typemap(imtype) long, const long& "long"
%typemap(cstype) long, const long& "long"
%typemap(csin) long, const long& "$csinput"
%typemap(in) long %{
  if ($input < LONG_MIN || $input > LONG_MAX) { NetOcc_SetPendingException("Standard_OutOfRange", "the value doesn't fit a C long"); return $null; }
  $1 = (long)$input;
%}
%typemap(in) const long& (long temp) %{
  if ($input < LONG_MIN || $input > LONG_MAX) { NetOcc_SetPendingException("Standard_OutOfRange", "the value doesn't fit a C long"); return $null; }
  temp = (long)$input;
  $1 = &temp;
%}
%typemap(out) long %{ $result = (long long)$1; %}
%typemap(csout, excode=SWIGEXCODE) long {
    long ret = $imcall;$excode
    return ret;
  }
%typemap(ctype) long& "long long *"
%typemap(imtype) long& "ref long"
%typemap(cstype) long& "ref long"
%typemap(csin) long& "ref $csinput"
%typemap(in) long& (long temp) %{
  if (*$input < LONG_MIN || *$input > LONG_MAX) { NetOcc_SetPendingException("Standard_OutOfRange", "the value doesn't fit a C long"); return $null; }
  temp = (long)*$input;
  $1 = &temp;
%}
%typemap(argout) long& %{ *$input = (long long)temp$argnum; %}
// SWIG falls back from const T& to T& for typemaps missing on const T&
%typemap(argout) const long& ""

/*
 * size_t: SWIG's C# default is a 32-bit uint. A ulong in C#, pointer-sized at the P/Invoke layer (win-x86 is 32-bit): a
 * value a 32-bit size_t can't hold throws OcctException (Standard_OutOfRange), as for C long. size_t& is in/out through a
 * UIntPtr.
 */
%typemap(ctype)  size_t, const size_t& "size_t"
%typemap(imtype) size_t, const size_t& "global::System.UIntPtr"
%typemap(cstype) size_t, const size_t& "ulong"
%typemap(csin)   size_t, const size_t& "global::OCC.Core.NativeSize.Of($csinput)"
%typemap(in)     size_t %{ $1 = ($1_ltype)$input; %}
%typemap(in)     const size_t& (size_t temp) %{ temp = $input; $1 = &temp; %}
%typemap(out)    size_t %{ $result = $1; %}
%typemap(out)    const size_t& %{ $result = *$1; %}
%typemap(csout, excode=SWIGEXCODE) size_t, const size_t& {
    ulong ret = (ulong)$imcall;$excode
    return ret;
  }
// a member's size_t& return is a ref UIntPtr into the object (References.i)
%typemap(ctype, out="void *") size_t& "size_t *"
%typemap(imtype, out="global::System.IntPtr") size_t& "ref global::System.UIntPtr"
%typemap(cstype, out="ref global::System.UIntPtr") size_t& "ref ulong"
// cshin: the ref also on the call to a constructor's SwigConstruct helper, which holds pre/post
%typemap(csin, pre="    global::System.UIntPtr temp$csinput = global::OCC.Core.NativeSize.Of($csinput);", post="      $csinput = temp$csinput.ToUInt64();",
         cshin="ref $csinput") size_t& "ref temp$csinput"
%typemap(in) size_t& %{ $1 = ($1_ltype)$input; %}

/*
 * Width typedefs. netocc-gen spells pointers to them as written: a pointer to int64_t converts to no other
 * type where int64_t is long. SWIG gets no typedefs for them, which it would resolve in the wrappers; the
 * array typemaps below name them. intptr_t, uintptr_t and ptrdiff_t are pointer-sized: IntPtr and UIntPtr.
 */

%define %netocc_pointer_sized(TYPE, CSTYPE)
%feature("novaluewrapper") TYPE;
%typemap(ctype)  TYPE, const TYPE& "TYPE"
%typemap(imtype) TYPE, const TYPE& "CSTYPE"
%typemap(cstype) TYPE, const TYPE& "CSTYPE"
%typemap(csin)   TYPE, const TYPE& "$csinput"
%typemap(in)     TYPE %{ $1 = $input; %}
%typemap(in)     const TYPE& ($*1_ltype temp) %{ temp = $input; $1 = &temp; %}
%typemap(out)    TYPE %{ $result = $1; %}
%typemap(out)    const TYPE& %{ $result = *$1; %}
%typemap(csout, excode=SWIGEXCODE) TYPE, const TYPE& {
    CSTYPE ret = $imcall;$excode
    return ret;
  }
%enddef

%netocc_pointer_sized(intptr_t, global::System.IntPtr)
%netocc_pointer_sized(uintptr_t, global::System.UIntPtr)
%netocc_pointer_sized(ptrdiff_t, global::System.IntPtr)
// native window-system handles, by their names: void* on Windows, unsigned long (X11) or an Objective-C pointer elsewhere
%netocc_pointer_sized(Aspect_Drawable, global::System.IntPtr)
%netocc_pointer_sized(Aspect_Handle, global::System.IntPtr)
%netocc_pointer_sized(Aspect_RenderingContext, global::System.IntPtr)
%netocc_pointer_sized(Aspect_Display, global::System.IntPtr)
%netocc_pointer_sized(Aspect_FBConfig, global::System.IntPtr)

/*
 * Pointer parameters to numbers: C# arrays, pinned for the call (null passes nullptr). The callee reads
 * and writes as many elements as its contract says; an array too short is as bad as in C++. netocc-gen
 * passes a pointer the callee may keep (constructors, setters) as an address instead (NetOcc_Address).
 * Returned pointers are addresses too: C# can't know the length. The collections' bulk copies take them
 * too, with the length (Collections.i).
 *
 *   TYPE* / const TYPE* / TYPE[N]     parameter  -> CSTYPE[]
 *   TYPE[N][M]                        parameter  -> CSTYPE[,]  (row-major, as C#)
 *
 * char: char* is a byte buffer (const char* stays a string, Strings.i); char16_t: a UTF-16 char[]
 * (const char16_t* is a string); bool: one byte each.
 */
%define %netocc_pointer_array(TYPE, CSTYPE)
%typemap(ctype)  TYPE *, TYPE [ANY], TYPE [], TYPE [ANY][ANY] "TYPE *"
%typemap(imtype) TYPE *, TYPE [ANY], TYPE [] "[global::System.Runtime.InteropServices.In, global::System.Runtime.InteropServices.Out] CSTYPE[]"
%typemap(imtype) TYPE [ANY][ANY] "[global::System.Runtime.InteropServices.In, global::System.Runtime.InteropServices.Out] CSTYPE[,]"
%typemap(cstype) TYPE *, TYPE [ANY], TYPE [] "CSTYPE[]"
%typemap(cstype) TYPE [ANY][ANY] "CSTYPE[,]"
%typemap(csin)   TYPE *, TYPE [ANY], TYPE [], TYPE [ANY][ANY] "$csinput"
%typemap(in)     TYPE *, TYPE [ANY], TYPE [], TYPE [ANY][ANY] %{ $1 = ($1_ltype)$input; %}
%enddef

%define %netocc_const_pointer_array(TYPE, CSTYPE)
%typemap(ctype)  const TYPE *, const TYPE [ANY], const TYPE [], const TYPE [ANY][ANY] "TYPE *"
%typemap(imtype) const TYPE *, const TYPE [ANY], const TYPE [] "[global::System.Runtime.InteropServices.In] CSTYPE[]"
%typemap(imtype) const TYPE [ANY][ANY] "[global::System.Runtime.InteropServices.In] CSTYPE[,]"
%typemap(cstype) const TYPE *, const TYPE [ANY], const TYPE [] "CSTYPE[]"
%typemap(cstype) const TYPE [ANY][ANY] "CSTYPE[,]"
%typemap(csin)   const TYPE *, const TYPE [ANY], const TYPE [], const TYPE [ANY][ANY] "$csinput"
%typemap(in)     const TYPE *, const TYPE [ANY], const TYPE [], const TYPE [ANY][ANY] %{ $1 = ($1_ltype)$input; %}
%enddef

%define %netocc_pointer_arrays(TYPE, CSTYPE)
%netocc_pointer_array(TYPE, CSTYPE)
%netocc_const_pointer_array(TYPE, CSTYPE)
%enddef

%netocc_pointer_arrays(double, double)
%netocc_pointer_arrays(int64_t, long)
%netocc_pointer_arrays(uint64_t, ulong)
%netocc_pointer_arrays(int32_t, int)
%netocc_pointer_arrays(uint32_t, uint)
%netocc_pointer_arrays(int16_t, short)
%netocc_pointer_arrays(uint16_t, ushort)
%netocc_pointer_arrays(int8_t, sbyte)
%netocc_pointer_arrays(uint8_t, byte)
%netocc_pointer_arrays(intptr_t, global::System.IntPtr)
%netocc_pointer_arrays(uintptr_t, global::System.UIntPtr)
%netocc_pointer_arrays(ptrdiff_t, global::System.IntPtr)
%netocc_pointer_arrays(float, float)
%netocc_pointer_arrays(int, int)
%netocc_pointer_arrays(unsigned int, uint)
%netocc_pointer_arrays(short, short)
%netocc_pointer_arrays(unsigned short, ushort)
%netocc_pointer_arrays(long long, long)
%netocc_pointer_arrays(unsigned long long, ulong)
%netocc_pointer_arrays(signed char, sbyte)
%netocc_pointer_arrays(unsigned char, byte)
%netocc_pointer_array(char, byte)
%netocc_pointer_array(char16_t, char)
%netocc_pointer_arrays(bool, bool)
// bool: bytes P/Invoke pins, copied from and back to the C# array (NativeArrays), since the marshaler's bool[] conversion
// differs between runtimes (CLR 2 writes back four-byte BOOLs)
%typemap(imtype) bool *, bool [ANY], bool [], const bool *, const bool [ANY], const bool [] "byte[]"
%typemap(csin, pre="    byte[] temp$csinput = global::OCC.Core.NativeArrays.ToBytes($csinput);",
         post="      global::OCC.Core.NativeArrays.CopyBack(temp$csinput, $csinput);")
         bool *, bool [ANY], bool [], const bool *, const bool [ANY], const bool [] "temp$csinput"
%typemap(imtype, inattributes="[global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.LPArray, ArraySubType = global::System.Runtime.InteropServices.UnmanagedType.U2)]")
         char16_t *, char16_t [ANY], char16_t [] "[global::System.Runtime.InteropServices.In, global::System.Runtime.InteropServices.Out] char[]"

/*
 * Addresses: IntPtr, for void* and for pointers C# can't type (a pointer the callee may keep, a function,
 * a pointer to a pointer).
 *
 *   void* / const void*                  parameter, return  -> IntPtr
 *   NetOcc_Address< T >                  parameter, return  -> IntPtr; converts to and from T in the wrapper
 *   NetOcc_AddressRef< T >               parameter          -> ref IntPtr; binds to a T& (T*&: the callee may replace it)
 *
 * netocc-gen declares the pointers as NetOcc_Address< T > and calls %netocc_address(T) once per module,
 * with %arg(): a function type holds commas.
 */
%typemap(ctype)  void *, const void * "void *"
%typemap(imtype) void *, const void * "global::System.IntPtr"
%typemap(cstype) void *, const void * "global::System.IntPtr"
%typemap(csin)   void *, const void * "$csinput"
%typemap(in)     void *, const void * %{ $1 = ($1_ltype)$input; %}
%typemap(out)    void *, const void * %{ $result = (void *)$1; %}
%typemap(csout, excode=SWIGEXCODE) void *, const void * {
    global::System.IntPtr ret = $imcall;$excode
    return ret;
  }

%{
// an address C# passes as IntPtr, for a pointer type C# can't type
template <class T> struct NetOcc_Address {
  void* myAddress;
  NetOcc_Address() : myAddress(nullptr) {}
  NetOcc_Address(T theAddress) : myAddress((void*)theAddress) {}
  operator T() const { return (T)myAddress; }
};

// the slot of an address the callee may replace: a T* the caller's IntPtr holds
template <class T> struct NetOcc_AddressRef {
  void** mySlot;
  NetOcc_AddressRef() : mySlot(nullptr) {}
  explicit NetOcc_AddressRef(void** theSlot) : mySlot(theSlot) {}
  operator T&() const { return *static_cast<T*>(static_cast<void*>(mySlot)); }
};

// a C array a function takes by reference (int (&)[3]): the elements of the C# array P/Invoke passes for the call
template <class T, int N> struct NetOcc_ArrayRef {
  typedef T Array[N];
  T* myData;
  NetOcc_ArrayRef() : myData(nullptr) {}
  explicit NetOcc_ArrayRef(T* theData) : myData(theData) {}
  operator Array&() const { return *reinterpret_cast<Array*>(myData); }
};
%}

template <class T> struct NetOcc_Address;
template <class T> struct NetOcc_AddressRef;
template <class T, int N> struct NetOcc_ArrayRef;

%define %netocc_address(TYPE)
%typemap(ctype)  NetOcc_Address< TYPE > "void *"
%typemap(imtype) NetOcc_Address< TYPE > "global::System.IntPtr"
%typemap(cstype) NetOcc_Address< TYPE > "global::System.IntPtr"
%typemap(csin)   NetOcc_Address< TYPE > "$csinput"
%typemap(in)     NetOcc_Address< TYPE > %{ $1 = NetOcc_Address< TYPE >((TYPE)$input); %}
%typemap(out)    NetOcc_Address< TYPE > %{ $result = ((const NetOcc_Address< TYPE >&)$1).myAddress; %}
%typemap(csout, excode=SWIGEXCODE) NetOcc_Address< TYPE > {
    global::System.IntPtr ret = $imcall;$excode
    return ret;
  }
%enddef

/*
 * T (&)[N] parameter (const too) -> a C# array of N elements, checked: the callee reads and writes it in place. netocc-gen
 * declares it as NetOcc_ArrayRef< T, N > and calls the macro per instantiation; bool goes through bytes, as bool* does.
 */
%define %netocc_array_ref(TYPE, N, CSTYPE)
%typemap(ctype)  NetOcc_ArrayRef< TYPE, N > "TYPE *"
%typemap(imtype) NetOcc_ArrayRef< TYPE, N > "[global::System.Runtime.InteropServices.In, global::System.Runtime.InteropServices.Out] CSTYPE[]"
%typemap(cstype) NetOcc_ArrayRef< TYPE, N > "CSTYPE[]"
%typemap(csin)   NetOcc_ArrayRef< TYPE, N > "global::OCC.Core.NativeArrays.Checked($csinput, N, \"$csinput\")"
%typemap(in)     NetOcc_ArrayRef< TYPE, N > %{ $1 = NetOcc_ArrayRef< TYPE, N >($input); %}
%enddef

%define %netocc_bool_array_ref(N)
%netocc_array_ref(bool, N, bool)
%typemap(imtype) NetOcc_ArrayRef< bool, N > "byte[]"
%typemap(csin, pre="    byte[] temp$csinput = global::OCC.Core.NativeArrays.ToBytes(global::OCC.Core.NativeArrays.Checked($csinput, N, \"$csinput\"));",
         post="      global::OCC.Core.NativeArrays.CopyBack(temp$csinput, $csinput);")
         NetOcc_ArrayRef< bool, N > "temp$csinput"
%enddef

%define %netocc_address_ref(TYPE)
%typemap(ctype)  NetOcc_AddressRef< TYPE > "void **"
%typemap(imtype) NetOcc_AddressRef< TYPE > "ref global::System.IntPtr"
%typemap(cstype) NetOcc_AddressRef< TYPE > "ref global::System.IntPtr"
%typemap(csin)   NetOcc_AddressRef< TYPE > "ref $csinput"
%typemap(in)     NetOcc_AddressRef< TYPE > %{ $1 = NetOcc_AddressRef< TYPE >($input); %}
%enddef

/*
 * char16_t: a UTF-16 code unit, C# char. P/Invoke would marshal a char as one ANSI byte, so the
 * P/Invoke layer passes a ushort. Its typecheck (C# char's) tells SWIG it overloads a const char16_t*, a string
 * (Strings.i): without, both rank as pointers, and SWIG drops one (Warning 516).
 */
%typecheck(SWIG_TYPECHECK_CHAR) char16_t, const char16_t& ""
%typemap(ctype)  char16_t, const char16_t& "char16_t"
%typemap(imtype) char16_t, const char16_t& "ushort"
%typemap(cstype) char16_t, const char16_t& "char"
%typemap(csin)   char16_t, const char16_t& "(ushort)$csinput"
%typemap(in)     char16_t %{ $1 = $input; %}
%typemap(in)     const char16_t& ($*1_ltype temp) %{ temp = $input; $1 = &temp; %}
%typemap(out)    char16_t %{ $result = $1; %}
%typemap(out)    const char16_t& %{ $result = *$1; %}
%typemap(csout, excode=SWIGEXCODE) char16_t, const char16_t& {
    char ret = (char)$imcall;$excode
    return ret;
  }
%typemap(ctype)  char16_t& "char16_t *"
%typemap(imtype) char16_t& "ref ushort"
%typemap(cstype) char16_t& "ref char"
%typemap(csin, pre="    ushort temp$csinput = (ushort)$csinput;", post="      $csinput = (char)temp$csinput;", cshin="ref $csinput") char16_t& "ref temp$csinput"
%typemap(in)     char16_t& %{ $1 = ($1_ltype)$input; %}
