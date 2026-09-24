// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

//
using NetOcc.Generator.Model;

namespace NetOcc.Generator.Mapping;

// the part of SignatureMapper that maps pointers: proxies, arrays, addresses, and what a pointer
// return is
internal sealed partial class SignatureMapper
{
  // an address C# can't type, as an IntPtr: NetOcc_Address<T> converts to T in the wrapper
  // (Types.i), and the module instantiates its typemaps. A pointer is passed as a copy: no
  // top-level const.
  private static TypeMapping Address(PointerType pointer, params TypeUse[] uses)
  {
    var spelling = (pointer with { Const = false }).Spelling;
    return new TypeMapping(
      new MappedType($"NetOcc_Address< {spelling} >", uses, "IntPtr", CsAddress,
                     $"%netocc_address(%arg({spelling}))"), null);
  }

  // a pointer parameter (a copy: no top-level const): a class is its proxy, null allowed; numbers
  // and structs are a C# array, pinned for the call, unless the callee may keep them; void* is an
  // IntPtr; a stream is lent like a stream reference
  private TypeMapping Pointer(PointerType p, bool mayKeep)
  {
    var pointer = p with { Const = false };
    switch (p.Pointee)
    {
      case BuiltinType { Name: "char", IsConst: true }:
        return TypeMapping.Of("const char*", "string", "string");
      // Standard_ExtString: UTF-16, like TCollection_ExtendedString
      case BuiltinType { Name: "char16_t", IsConst: true }:
        return TypeMapping.Of("const char16_t*", "string", "string");
      case BuiltinType { Name: "void" }:
        return TypeMapping.Of(pointer.Spelling, "IntPtr", CsAddress);
      // a pointer-sized typedef Types.i has no arrays of (a window-system handle: void* on Windows,
      // an integer on X11)
      case BuiltinType
      {
        Name: "intptr_t" or "uintptr_t" or "ptrdiff_t",
        Written: not (null or "intptr_t" or "uintptr_t" or "ptrdiff_t")
      }:
        return Address(pointer);
      case BuiltinType b when ArrayElements.TryGetValue(b.Name, out var element):
        return mayKeep
          ? Address(pointer)
          : TypeMapping.Of(pointer.Spelling, $"{element}[]", $"{element}[]");
      case NamedType { Kind: NamedKind.Class, TemplateArguments.Count: 0 } n
        when registry.Class(n.Name) is { Kind: WrapKind.ValueType } valueType:
        var use = valueType.Use;
        return mayKeep
          ? Address(pointer, use)
          : TypeMapping.Of(pointer.Spelling, $"{n.Name}[]", $"{n.Name}[]", use);
      case NamedType { Kind: NamedKind.Class } n when StreamClass(n) is { } stream:
        return TypeMapping.Of($"{stream}*", "Stream", CsStream);
      // an opaque pointer (no package defines the class, the callee only passes it on) or one to an
      // enum
      case NamedType n when IsOpaque(n) || n.Kind == NamedKind.Enum:
        return Address(Opaque(pointer));
      case NamedType { Kind: NamedKind.Class } n when IsProxied(n):
        return PointedClass(n, pointer.Spelling);
      case NamedType { Name: var name } when name.StartsWith("std::", StringComparison.Ordinal):
        return TypeMapping.Skipped($"raw pointer {p.Spelling}: std type {name}");
      case BuiltinType or NamedType or PointerType or FunctionType:
        return Address(pointer);
      default:
        return TypeMapping.Skipped($"raw pointer {p.Spelling}");
    }
  }

  // a class the headers only declare: no package defines it (a defined one is known by its flat
  // name)
  private bool IsOpaque(NamedType n) =>
    n.DeclaredOnly is not null && registry.Class(n.Name) is null;

  // a pointer spelled by C++'s names where a flat one won't do: an opaque class has no flat alias,
  // and SWIG spells a known enum in a template argument "enum X", which a nested enum's flat alias
  // can't follow
  private PointerType Opaque(PointerType p) => p.Pointee switch
  {
    NamedType { DeclaredOnly: { } qualified } n => p with { Pointee = n with { Name = qualified } },
    NamedType { Kind: NamedKind.Enum } e when registry.Enum(e.Name) is
      { QualifiedName: { } qualified } => p with { Pointee = e with { Name = qualified } },
    PointerType inner => p with { Pointee = Opaque(inner) },
    _ => p,
  };

  // a class that is a proxy in C#: not a handle, a standard library type, or one of the .NET types
  // of the typemaps
  internal static bool IsProxied(NamedType n) =>
    n.Name != "opencascade::handle"
    && !n.Name.StartsWith("std::", StringComparison.Ordinal)
    && !TypeRegistry.TypemappedClasses.Contains(n.Name);

  // a class a pointer points to: its proxy (a collection's by the alias), or why there's none
  private TypeMapping PointedClass(NamedType n, string spelling)
  {
    if (n.TemplateArguments.Count > 0)
    {
      var (instantiation, uses, skip) = Collection(n);
      return instantiation is null
        ? TypeMapping.Skipped(skip!)
        : TypeMapping.Of(spelling, instantiation.Alias, instantiation.Alias, uses);
    }

    return registry.Class(n.Name) is { } known
      ? TypeMapping.Of(spelling, n.Name, n.Name, known.Use)
      : TypeMapping.Skipped($"class {n.Name} is not wrapped");
  }

  // T*& (the callee may replace the pointer): a class is ref of its proxy (References.i,
  // Handles.i), const char* a ref string, anything else a ref IntPtr through NetOcc_AddressRef<T>
  private TypeMapping PointerReference(PointerType p)
  {
    switch (p.Pointee)
    {
      case BuiltinType { Name: "char", IsConst: true }:
        return TypeMapping.Of("const char*&", "ref string", "ref string");
      case NamedType { Kind: NamedKind.Class, TemplateArguments.Count: 0 } n
        when registry.Class(n.Name) is { Kind: WrapKind.ValueType }:
        return AddressReference(p);
      case NamedType n when IsOpaque(n) || n.Kind == NamedKind.Enum:
        return AddressReference(Opaque(p));
      case NamedType { Kind: NamedKind.Class } n when IsProxied(n):
        var pointed = PointedClass(n, $"{p.Spelling}&");
        return pointed.Type is { } type
          ? new TypeMapping(
            type with { CsKey = $"ref {type.CsKey}", CsType = $"ref {type.CsType}" }, null)
          : pointed;
      case NamedType { Name: var name } when name.StartsWith("std::", StringComparison.Ordinal):
        return TypeMapping.Skipped($"{p.Spelling}&: std type {name}");
      case BuiltinType or NamedType or PointerType or FunctionType:
        return AddressReference(p);
      default:
        return TypeMapping.Skipped($"unsupported type {p.Spelling}&");
    }
  }

  private static TypeMapping AddressReference(PointerType p) => new(
    new MappedType($"NetOcc_AddressRef< {p.Spelling} >", [], "ref IntPtr", $"ref {CsAddress}",
                   $"%netocc_address_ref(%arg({p.Spelling}))"), null);

  // a parameter declared as an array of arrays (const double theJ[3][3]): a C# rectangular array of
  // numbers
  private static TypeMapping Matrix(ArrayType a, bool mayKeep) =>
    a is { Element: ArrayType { Element: BuiltinType b } }
    && ArrayElement(b) is { } element
    && !mayKeep
      ? new TypeMapping(
        new MappedType(a.Spelling, [], $"{element}[,]", $"{element}[,]", Declared: a), null)
      : Address(new PointerType(a.Element));

  // a returned pointer: a class is a proxy (References.i: a member's borrows from the object and
  // keeps its proxy alive, spelled T*; one without an object doesn't, T* const; Handles.i: a
  // transient's owns a reference, T* const, since constructors return T*), a string a string,
  // anything else an address. C# has no const, so neither has the proxy.
  private TypeMapping ReturnedPointer(PointerType p, bool member)
  {
    var owned =
      p.Pointee is NamedType { TemplateArguments.Count: 0 } target
      && registry.Class(target.Name) is { Kind: WrapKind.Transient }
      || p.Pointee is NamedType { TemplateArguments.Count: > 0 } collection
      && CollectionTemplate.Named(collection.Name) is { IsTransient: true };
    var pointer = p with { Const = !member || owned };
    switch (p.Pointee)
    {
      case BuiltinType { Name: "char", IsConst: true }:
        return TypeMapping.Of("const char*", "string", "string");
      case BuiltinType { Name: "char16_t", IsConst: true }:
        return TypeMapping.Of("const char16_t*", "string", "string");
      case BuiltinType { Name: "void" }:
        return TypeMapping.Of((p with { Const = false }).Spelling, "IntPtr", CsAddress);
      case NamedType { Kind: NamedKind.Class, TemplateArguments.Count: 0 } n
        when registry.Class(n.Name) is { Kind: WrapKind.ValueType } valueType:
        return Address(p, valueType.Use);
      case NamedType n when IsOpaque(n) || n.Kind == NamedKind.Enum:
        return Address(Opaque(p));
      case NamedType { Kind: NamedKind.Class } n when IsProxied(n):
        var pointed = PointedClass(n, pointer.Spelling);
        return pointed.Type is null
          ? TypeMapping.Skipped($"returns {p.Spelling}: {pointed.Skip}")
          : pointed;
      case NamedType { Kind: NamedKind.Class } n when StreamClass(n) is { } stream:
        return TypeMapping.Skipped(
          $"returns a native {stream}: Streams.i lends C# streams to OCCT for a call and has no C# view of OCCT's");
      case NamedType { Name: var name } when name.StartsWith("std::", StringComparison.Ordinal):
        return TypeMapping.Skipped($"returns raw pointer {p.Spelling}: std type {name}");
      case BuiltinType or NamedType or PointerType or FunctionType:
        return Address(p);
      default:
        return TypeMapping.Skipped($"returns raw pointer {p.Spelling}");
    }
  }
}
