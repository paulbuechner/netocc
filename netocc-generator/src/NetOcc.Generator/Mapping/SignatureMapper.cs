// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

//
using NetOcc.Generator.Model;

namespace NetOcc.Generator.Mapping;

/// <summary>
/// Maps C++ signature types to netocc-core's wrapper contract: <c>&amp;</c> → <c>ref</c>, struct
/// <c>const&amp;</c> → <c>in</c>, handles ⇄ proxies, strings and GUIDs through the common typemaps.
/// Anything else is skipped with a reason.
/// </summary>
internal sealed partial class SignatureMapper(TypeRegistry registry)
{
  // builtins with the same C# width on every platform, and their C# type: char its byte, whose sign
  // differs per platform (C long, 32-bit on Windows and 64-bit elsewhere, has its own cases: a C#
  // long, range-checked)
  private static readonly Dictionary<string, string> Builtins = new()
  {
    ["bool"] = "bool",
    ["char"] = "byte",
    ["signed char"] = "sbyte",
    ["unsigned char"] = "byte",
    ["char32_t"] = "uint",
    ["short"] = "short",
    ["unsigned short"] = "ushort",
    ["int"] = "int",
    ["unsigned int"] = "uint",
    ["long long"] = "long",
    ["unsigned long long"] = "ulong",
    ["float"] = "float",
    ["double"] = "double",
    ["size_t"] = "ulong",
    ["intptr_t"] = "global::System.IntPtr",
    ["uintptr_t"] = "global::System.UIntPtr",
    ["ptrdiff_t"] = "global::System.IntPtr",
  };

  // by-reference builtins with an INOUT typemap in Types.i and a ref return in References.i
  private static readonly HashSet<string> RefBuiltins =
  [
    "bool", "int", "unsigned int", "short", "unsigned short", "signed char", "unsigned char",
    "long long", "unsigned long long", "float", "double"
  ];

  // collection elements C# copies as bytes: Types.i's %netocc_array list (bool and char marshal
  // differently)
  private static readonly HashSet<string> BlittableBuiltins =
  [
    "double", "float", "int", "unsigned int", "short", "unsigned short", "signed char",
    "unsigned char"
  ];

  // template arguments are canonical types: size_t or int64_t spell differently per platform
  // (unsigned long on Linux)
  private static readonly HashSet<string> PlatformSpelledBuiltins =
    ["long long", "unsigned long long", "size_t"];

  // a pointer to a builtin as a parameter: a C# array of it (Types.i's %netocc_pointer_array). char
  // is a byte there (const char* is a string), char16_t a UTF-16 char. size_t, long and wchar_t
  // have no fixed width.
  private static readonly Dictionary<string, string> ArrayElements = new()
  {
    ["bool"] = "bool",
    ["char"] = "byte",
    ["signed char"] = "sbyte",
    ["unsigned char"] = "byte",
    ["char16_t"] = "char",
    ["short"] = "short",
    ["unsigned short"] = "ushort",
    ["int"] = "int",
    ["unsigned int"] = "uint",
    ["long long"] = "long",
    ["unsigned long long"] = "ulong",
    ["float"] = "float",
    ["double"] = "double",
    ["intptr_t"] = "global::System.IntPtr",
    ["uintptr_t"] = "global::System.UIntPtr",
    ["ptrdiff_t"] = "global::System.IntPtr",
  };

  /// <summary>
  /// The C# type of an address: void*, and pointers C# can't type (kept by the callee, functions,
  /// pointers to pointers).
  /// </summary>
  public const string CsAddress = "global::System.IntPtr";

  /// <summary>The C# type of a builtin that maps (see <see cref="Builtins"/>), or null.</summary>
  public static string? CsBuiltin(string name) => Builtins.GetValueOrDefault(name);

  /// <summary>
  /// A C++ integer type of the C# one's width and sign (an enum's underlying type from its csbase),
  /// or null.
  /// </summary>
  public static string? CppBuiltin(string csType) => csType switch
  {
    "byte" => "unsigned char",
    "sbyte" => "signed char",
    "short" => "short",
    "ushort" => "unsigned short",
    "int" => "int",
    "uint" => "unsigned int",
    "long" => "long long",
    "ulong" => "unsigned long long",
    _ => null,
  };

  /// <summary>
  /// The C# type of a stream parameter: netocc-core's Streams.i lends a System.IO.Stream for the
  /// call.
  /// </summary>
  public const string CsStream = "global::System.IO.Stream";

  /// <summary>
  /// <c>std::ostream</c> or <c>std::istream</c> for a reference to one
  /// (<c>Standard_OStream&amp;</c>, canonical <c>std::basic_ostream&lt;char, ...&gt;&amp;</c>),
  /// otherwise null.
  /// </summary>
  public static string? StreamOf(CppType type) =>
    type is ReferenceType { IsRValue: false, Referee: var referee } ? StreamClass(referee) : null;

  /// <summary>
  /// <c>std::ostream</c> or <c>std::istream</c> for the (non-const) stream class, as
  /// <see cref="StreamOf"/>; otherwise null.
  /// </summary>
  public static string? StreamClass(CppType type) =>
    type is NamedType
    {
      IsConst: false, TemplateArguments: [BuiltinType { Name: "char" }, ..]
    } stream
    && stream.Name.StartsWith("std::", StringComparison.Ordinal)
      ? stream.Name.EndsWith("::basic_ostream", StringComparison.Ordinal) ? "std::ostream" :
      stream.Name.EndsWith("::basic_istream", StringComparison.Ordinal) ? "std::istream" : null
      : null;

  /// <summary>
  /// A member returning its own object by reference, for chaining (<c>gp_XYZ&amp; Add(...)</c>):
  /// void in C#.
  /// </summary>
  public static bool IsChaining(CppType returned, string owner) =>
    returned is ReferenceType
    {
      IsRValue: false,
      Referee: NamedType
      {
        Kind: NamedKind.Class, TemplateArguments.Count: 0, IsConst: false
      } self
    }
    && self.Name == owner;

  /// <param name="mayKeep">
  /// The callee may keep a pointer it's given (a constructor, a <c>Set*</c> method). A C# array is
  /// pinned for the call only, so a pointer to numbers or structs is an address there.
  /// </param>
  public TypeMapping Parameter(CppType type, bool mayKeep = false) =>
    MapParameter(registry.Resolve(type), mayKeep);

  private TypeMapping MapParameter(CppType type, bool mayKeep) => type switch
  {
    BuiltinType { Name: "void" } => TypeMapping.Skipped("void parameter"),
    BuiltinType b => Number(b, ValueSpelling(b))
                     ?? TypeMapping.Skipped($"{b.Name} has no fixed-width C# type"),
    NamedType { Kind: NamedKind.Enum } e => Enum(e, e.Name),
    NamedType n => Class(n, n with { Const = false }, byMutableReference: false),
    ReferenceType { IsRValue: true } => TypeMapping.Skipped("rvalue reference"),
    ReferenceType when StreamOf(type) is { } stream => TypeMapping.Of(
      $"{stream}&", "Stream", CsStream),
    ReferenceType { Referee: BuiltinType { IsConst: true } b } when Number(
      b, $"const {ValueSpelling(b)}&") is { } number => number,
    ReferenceType { Referee: BuiltinType { Name: "char16_t" } } => TypeMapping.Of(
      "char16_t&", "ref char", "ref char"),
    // a width typedef by its written name: int64_t& is long& on Linux, and Types.i applies INOUT to
    // the written names
    ReferenceType { Referee: BuiltinType b } when !b.IsConst && RefBuiltins.Contains(b.Name) =>
      TypeMapping.Of($"{b.Written ?? b.Name}&", $"ref {b.Name}", $"ref {Builtins[b.Name]}"),
    // char's bits (its sign differs per platform), size_t pointer-sized underneath, C long
    // range-checked (Types.i)
    ReferenceType { Referee: BuiltinType { Name: "char", IsConst: false } } => TypeMapping.Of(
      "char&", "ref unsigned char", "ref byte"),
    ReferenceType { Referee: BuiltinType { Name: "size_t", IsConst: false, Written: null } } =>
      TypeMapping.Of("size_t&", "ref unsigned long long", "ref ulong"),
    ReferenceType { Referee: BuiltinType { Name: "long", IsConst: false } } => TypeMapping.Of(
      "long&", "ref long long", "ref long"),
    ReferenceType { Referee: BuiltinType b } =>
      TypeMapping.Skipped($"{b.Spelling}& has no typemap"),
    // a C array by reference (int (&)[3]): a C# array of its length, checked, which the callee
    // reads and writes in place (Types.i's NetOcc_ArrayRef); bool one byte each
    ReferenceType
    {
      IsRValue: false, Referee: ArrayType { Element: BuiltinType { Name: "bool" }, Length: > 0 } a
    } => new TypeMapping(
      new MappedType($"NetOcc_ArrayRef< bool, {a.Length} >", [], "bool[]", "bool[]",
                     $"%netocc_bool_array_ref({a.Length})"), null),
    ReferenceType { IsRValue: false, Referee: ArrayType { Element: BuiltinType e, Length: > 0 } a }
      when ArrayElement(e) is { } cs => new TypeMapping(
        new MappedType($"NetOcc_ArrayRef< {e.Name}, {a.Length} >", [], $"{cs}[]", $"{cs}[]",
                       $"%netocc_array_ref({e.Name}, {a.Length}, {cs})"), null),
    // by value: SWIG's temporary for a const enum& is an elaborated "enum X", which a flat alias (a
    // nested enum's) can't be
    ReferenceType
    {
      Referee: NamedType { Kind: NamedKind.Enum, IsConst: true } e
    } => Enum(e, e.Name),
    // References.i's ref slot is an int
    ReferenceType { Referee: NamedType { Kind: NamedKind.Enum } e } when registry.Enum(e.Name) is
      { Underlying: { } underlying } => TypeMapping.Skipped(
      $"{e.Name}& of an enum on {underlying}, and the ref slot is an int"),
    ReferenceType { Referee: NamedType { Kind: NamedKind.Enum } e } => Enum(
      e, $"{e.Name}&", byReference: true),
    ReferenceType { Referee: NamedType n } => Class(n, n, byMutableReference: !n.IsConst,
                                                    reference: true),
    // T* const& is the pointer, passed by value
    ReferenceType { Referee: PointerType { IsConst: true } p } => Pointer(p, mayKeep),
    ReferenceType { Referee: PointerType p } => PointerReference(p),
    PointerType p => Pointer(p, mayKeep),
    ArrayType { Element: ArrayType } a => Matrix(a, mayKeep),
    ArrayType a => Pointer(new PointerType(a.Element), mayKeep),
    _ => TypeMapping.Skipped($"unsupported type {type.Spelling}"),
  };

  /// <summary>
  /// The return of a value-type thunk: no object to borrow from, since a C# struct is pinned for
  /// the call only.
  /// </summary>
  public TypeMapping Return(CppType type) => Return(registry.Resolve(type), member: false);

  private TypeMapping Return(CppType type, bool member) => type switch
  {
    BuiltinType { Name: "void" } => TypeMapping.Of("void", "void", "void"),
    BuiltinType b => Number(b, ValueSpelling(b))
                     ?? TypeMapping.Skipped($"returns {b.Name}, which has no fixed-width C# type"),
    NamedType { Kind: NamedKind.Enum } e => Enum(e, e.Name),
    NamedType n => ReturnedClass(n with { Const = false }, reference: false),
    // Types.i returns no const long& (OCCT has none)
    ReferenceType { IsRValue: false, Referee: BuiltinType { IsConst: true, Name: not "long" } b }
      when Number(b, $"const {ValueSpelling(b)}&") is { } number => number,
    ReferenceType
    {
      IsRValue: false, Referee: NamedType { Kind: NamedKind.Enum, IsConst: true } e
    } => Enum(e, $"const {e.Name}&"),
    ReferenceType { IsRValue: false, Referee: NamedType { IsConst: true } n } => ReturnedClass(
      n, reference: true),
    // a string's mutable reference comes back as a copy too (Strings.i)
    ReferenceType
    {
      IsRValue: false,
      Referee: NamedType { Name: "TCollection_AsciiString" or "TCollection_ExtendedString" } n
    } => TypeMapping.Of($"{n.Name}&", "string", "string"),
    // a pointer returned by const reference is the pointer
    ReferenceType { IsRValue: false, Referee: PointerType { IsConst: true } p } =>
      ReturnedPointer(p, member),
    ReferenceType => TypeMapping.Skipped($"returns a mutable reference, {type.Spelling}"),
    PointerType p => ReturnedPointer(p, member),
    _ => TypeMapping.Skipped($"returns unsupported type {type.Spelling}"),
  };

  /// <summary>
  /// A function's return. Returning the stream it was given, for chaining (<c>Standard_OStream&amp;
  /// Print(Standard_OStream&amp;)</c>), is void in C#. A non-static member of
  /// <paramref name="owner"/> returns references too (netocc-core's References.i): its own class,
  /// for chaining, is void (<see cref="IsChaining"/>); a number, enum or struct is a C# ref into
  /// the object; a class a proxy that borrows the object and keeps the owner's proxy alive, and so
  /// does a pointer to a class.
  /// </summary>
  public TypeMapping Return(CppType type, IReadOnlyList<ParameterModel> parameters,
                            string? owner = null)
  {
    type = registry.Resolve(type);
    if ((StreamOf(type) is { } stream && parameters.Any(p => StreamOf(p.Type) == stream))
        || (owner is not null && IsChaining(type, owner)))
    {
      return TypeMapping.Of("void", "void", "void");
    }

    return owner is not null
           && type is ReferenceType { IsRValue: false, Referee: var referee }
           && Borrowed(referee) is { } borrowed
      ? borrowed
      : Return(type, member: owner is not null);
  }

  // a builtin by value as the .i spells it: a pointer-sized typedef by its written name
  // (Aspect_Drawable is void* on Windows, unsigned long on X11: no one type converts to both),
  // others by the builtin, which converts to theirs
  private static string ValueSpelling(BuiltinType b) =>
    b is { Name: "intptr_t" or "uintptr_t", Written: { } written } ? written : b.Name;

  // a number by value (spelled so) or const& (spelled "const T&"): its C# type, or null without a
  // fixed-width one. Keyed as the builtin whose C# type it shares: char (its byte) as unsigned
  // char, char32_t (a code point) as unsigned int, C long (a C# long; Types.i checks the range) as
  // long long, size_t (a ulong) as unsigned long long, the pointer-sized ones as void* is.
  private static TypeMapping? Number(BuiltinType b, string spelling) => b.Name switch
  {
    "char16_t" => TypeMapping.Of(spelling, "char", "char"),
    "char" => TypeMapping.Of(spelling, "unsigned char", "byte"),
    "char32_t" => TypeMapping.Of(spelling, "unsigned int", "uint"),
    "long" => TypeMapping.Of(spelling, "long long", "long"),
    "size_t" => TypeMapping.Of(spelling, "unsigned long long", "ulong"),
    "intptr_t" or "ptrdiff_t" => TypeMapping.Of(spelling, "IntPtr", CsAddress),
    "uintptr_t" => TypeMapping.Of(spelling, "UIntPtr", Builtins["uintptr_t"]),
    _ when Builtins.TryGetValue(b.Name, out var cs) => TypeMapping.Of(spelling, b.Name, cs),
    _ => null,
  };

  // a reference a member returns, or null where Return decides: copies of const value classes and
  // collections, strings
  private TypeMapping? Borrowed(CppType referee)
  {
    switch (referee)
    {
      // spelled by the canonical name, not the written one as parameters are: References.i returns
      // those
      case BuiltinType { IsConst: false } b when RefBuiltins.Contains(b.Name):
        return TypeMapping.Of($"{b.Name}&", $"ref {b.Name}", $"ref {Builtins[b.Name]}");
      // pointer-sized: a ref UIntPtr, four bytes on win-x86 as size_t is
      case BuiltinType { Name: "size_t", IsConst: false, Written: null }:
        return TypeMapping.Of("size_t&", "ref UIntPtr", "ref global::System.UIntPtr");
      case NamedType { Kind: NamedKind.Enum, IsConst: false } e:
        return registry.Enum(e.Name) is { } knownEnum
          ? TypeMapping.Of($"{e.Name}&", $"ref {e.Name}", $"ref {e.Name}", knownEnum.Use)
          : TypeMapping.Skipped($"returns enum {e.Name}&, which is not wrapped");
      // a pointer the object holds, as a ref IntPtr into it
      case PointerType { IsConst: false } pointer:
        return TypeMapping.Of($"{pointer.Spelling}&", "ref IntPtr", $"ref {CsAddress}");
      // the object itself, as for a const& handle
      case NamedType { Name: "opencascade::handle", IsConst: false } handle:
        return Handle(handle, handle, "&", byMutableReference: false);
      case NamedType { TemplateArguments.Count: 0 } n
        when !TypeRegistry.TypemappedClasses.Contains(n.Name)
             && registry.Class(n.Name) is { } known:
        var use = known.Use;
        return known.Kind switch
        {
          WrapKind.ValueType => n.IsConst
            ? null
            : TypeMapping.Of($"{n.Name}&", $"ref {n.Name}", $"ref {n.Name}", use),
          WrapKind.ValueClass when n.IsConst => null,
          _ => TypeMapping.Of($"{n.Spelling}&", n.Name, n.Name, use),
        };
      case NamedType { TemplateArguments.Count: > 0, IsConst: false } n
        when CollectionTemplate.Named(n.Name) is { IsTransient: false }:
        var (instantiation, uses, skip) = Collection(n);
        return instantiation is null
          ? TypeMapping.Skipped($"returns {n.Spelling}&: {skip}")
          : TypeMapping.Of($"{n.Spelling}&", instantiation.Alias, instantiation.Alias, uses);
      default:
        return null;
    }
  }

  private TypeMapping Enum(NamedType e, string spelling, bool byReference = false) =>
    registry.Enum(e.Name) is { } known
      ? TypeMapping.Of(spelling, byReference ? $"ref {e.Name}" : e.Name,
                       byReference ? $"ref {e.Name}" : e.Name, known.Use)
      : TypeMapping.Skipped($"enum {e.Name} is not wrapped");

  // parameter: by value, const&, or & (the callee writes the caller's object)
  private TypeMapping Class(NamedType n, NamedType spelled, bool byMutableReference,
                            bool reference = false)
  {
    var suffix = reference ? "&" : "";
    if (n.Name.StartsWith("std::", StringComparison.Ordinal)
        && CollectionTemplate.Named(n.Name) is null)
    {
      return Std(n, byMutableReference, reference);
    }

    if (TypeRegistry.TypemappedClasses.Contains(n.Name))
    {
      var typemapped = Typemapped(n.Name);
      return byMutableReference
        ? TypeMapping.Of($"{spelled.Spelling}{suffix}", $"ref {typemapped.Key}",
                         $"ref {typemapped.CsType}")
        : TypeMapping.Of($"{spelled.Spelling}{suffix}", typemapped.Key, typemapped.CsType);
    }

    if (n.Name == "opencascade::handle")
    {
      return Handle(n, spelled, suffix, byMutableReference);
    }

    if (n.TemplateArguments.Count > 0)
    {
      var (instantiation, uses, skip) = Collection(n);
      if (instantiation is null)
      {
        return TypeMapping.Skipped(skip!);
      }

      // a handle-managed collection is passed as its handle, or by const& like a transient
      return instantiation.Template.IsTransient && (!reference || byMutableReference)
        ? TypeMapping.Skipped(
          $"{spelled.Spelling}{suffix}: {instantiation.Alias} is handle-managed (pass the handle)")
        : TypeMapping.Of($"{spelled.Spelling}{suffix}", instantiation.Alias, instantiation.Alias,
                         uses);
    }

    if (registry.Class(n.Name) is not { } known)
    {
      return TypeMapping.Skipped($"class {n.Name} is not wrapped");
    }

    // a struct: const& is a pointer to the caller's struct (in), & lets the callee write it (ref)
    var key = known.Kind == WrapKind.ValueType && byMutableReference ? $"ref {n.Name}" : n.Name;
    var csType = known.Kind != WrapKind.ValueType ? n.Name :
      byMutableReference ? $"ref {n.Name}" :
      reference ? $"in {n.Name}" : n.Name;
    return TypeMapping.Of($"{spelled.Spelling}{suffix}", key, csType, known.Use);
  }

  private TypeMapping Handle(NamedType n, NamedType spelled, string suffix, bool byMutableReference)
  {
    // Handle(TColStd_HArray1OfReal): a handle-managed collection
    if (n.TemplateArguments is [NamedType { TemplateArguments.Count: > 0 } collection])
    {
      var (instantiation, uses, skip) = Collection(collection);
      if (instantiation is null)
      {
        return TypeMapping.Skipped($"handle {n.Spelling}: {skip}");
      }

      var alias = byMutableReference ? $"ref {instantiation.Alias}" : instantiation.Alias;
      return TypeMapping.Of($"{spelled.Spelling}{suffix}", alias, alias, uses);
    }

    if (n.TemplateArguments is not [NamedType { TemplateArguments.Count: 0 } target]
        || registry.Class(target.Name) is not { Kind: WrapKind.Transient } known)
    {
      return TypeMapping.Skipped($"handle {n.Spelling} of an unwrapped class");
    }

    var key = byMutableReference ? $"ref {target.Name}" : target.Name;
    return TypeMapping.Of($"{spelled.Spelling}{suffix}", key, key, known.Use);
  }

  private TypeMapping ReturnedClass(NamedType n, bool reference)
  {
    // a handle-managed collection comes back as its handle
    if (n.TemplateArguments.Count > 0 && CollectionTemplate.Named(n.Name) is { IsTransient: true })
    {
      return TypeMapping.Skipped(
        $"returns {(n with { Const = false }).Spelling}{(reference ? "&" : "")}, which is handle-managed");
    }

    var mapped = Class(n, n, byMutableReference: false, reference: reference);
    if (mapped.Type is null)
    {
      return mapped;
    }

    // returned structs come back by value: no "in"
    mapped = new TypeMapping(mapped.Type with
    {
      CsType = mapped.Type.CsType.StartsWith("in ", StringComparison.Ordinal)
        ? mapped.Type.CsType[3..]
        : mapped.Type.CsType
    }, null);
    if (n.Name == "opencascade::handle"
        || n.TemplateArguments.Count > 0
        || TypeRegistry.TypemappedClasses.Contains(n.Name)
        || n.Name.StartsWith("std::", StringComparison.Ordinal))
    {
      return mapped;
    }

    // a returned object is copied into a proxy that owns it (by value, or %occt_valueclass for
    // const&); a move-only one returned by value moves in
    var known = registry.Class(n.Name)!;
    return known.Kind is WrapKind.ValueClass or WrapKind.ValueType
           || (!reference && known.IsMoveOnly)
      ? mapped
      : TypeMapping.Skipped(reference
                              ? $"returns const {n.Name}&, which isn't copyable"
                              : $"returns {n.Name} by value, which isn't copyable");
  }
}
