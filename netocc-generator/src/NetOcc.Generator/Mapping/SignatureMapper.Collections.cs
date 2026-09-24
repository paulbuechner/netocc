// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

//
using NetOcc.Generator.Model;

namespace NetOcc.Generator.Mapping;

// the part of SignatureMapper that maps NCollection instantiations and their elements
internal sealed partial class SignatureMapper
{
  // an instance of a collection template, named by its OCCT alias or a name of its own, holding
  // elements C# can hold
  private (KnownInstantiation? Instantiation, TypeUse[] Uses, string? Skip) Collection(NamedType n)
  {
    // the registry names every instance of a wrapped template
    if (registry.Instantiation(n) is not { } instantiation)
    {
      return (null, [], $"template {(n with { Const = false }).Spelling} is not wrapped");
    }

    var template = instantiation.Template;
    var (arguments, skip) = Arguments(template, n.TemplateArguments);
    if (arguments is null)
    {
      return (null, [], $"{instantiation.Alias}: {skip}");
    }

    if (template.IsTransient && registry.BaseOf(instantiation) is null)
    {
      return (null, [],
        $"{instantiation.Alias}: its base {KnownInstantiation.Spell(template.Base!, instantiation.Arguments)} isn't wrapped");
    }

    return (instantiation,
      [new TypeUse(instantiation.Package, instantiation.Header, instantiation), .. arguments.Uses],
      null);
  }

  /// <summary>
  /// A collection template's arguments in C#, or why one can't be: every element must map; a hasher
  /// is C++ only.
  /// </summary>
  public (CollectionArguments? Arguments, string? Skip) Arguments(
    CollectionTemplate template, IReadOnlyList<CppType> arguments)
  {
    List<string> csTypes = [];
    List<TypeUse> uses = [];
    List<string> typemaps = [];
    var blittable = true;
    for (var i = 0; i < template.Arguments.Count; i++)
    {
      if (template.Arguments[i] == CollectionArgument.Hasher)
      {
        if (arguments[i] is NamedType { TemplateArguments.Count: 0 } hasher
            && registry.Class(hasher.Name) is { } known)
        {
          uses.Add(known.Use);
        }

        continue;
      }

      // a pair's accessors are %extend, whose casts SWIG writes with "enum X", which a nested
      // enum's flat alias can't follow
      if (template.Name == "std::pair"
          && registry.Resolve(arguments[i]) is NamedType { Kind: NamedKind.Enum } e
          && registry.Enum(e.Name)?.QualifiedName is not null)
      {
        return (null,
          $"element {e.Name}: a pair's accessors are %extend, which SWIG casts with enum {e.Name}, and a nested enum's flat alias can't follow that");
      }

      var (element, skip) = Element(arguments[i], template.DefaultConstructs);
      if (element is null)
      {
        return (null, skip);
      }

      csTypes.Add(element.CsType);
      uses.AddRange(element.Uses);
      blittable &= element.IsBlittable;
      if (element.Typemap is { } typemap)
      {
        typemaps.Add(typemap);
      }
    }

    return (new CollectionArguments(csTypes, blittable, uses, typemaps), null);
  }

  /// <summary>What a collection's element is in C#, or why the collection can't hold it.</summary>
  /// <param name="defaultConstructed">
  /// The collection default-constructs elements (the arrays).
  /// </param>
  public (CollectionElement? Element, string? Skip) Element(CppType type, bool defaultConstructed)
  {
    switch (registry.Resolve(type))
    {
      case BuiltinType b when PlatformSpelledBuiltins.Contains(b.Name):
        return (null, $"element {b.Name}: spelled differently per platform");
      case BuiltinType b when Builtins.TryGetValue(b.Name, out var cs):
        return (new CollectionElement(cs, BlittableBuiltins.Contains(b.Name), []), null);
      case NamedType { Kind: NamedKind.Enum } e:
        return registry.Enum(e.Name) is { } known
          ? (new CollectionElement(e.Name, false, [known.Use]), null)
          : (null, $"element enum {e.Name} is not wrapped");
      case NamedType { TemplateArguments.Count: 0 } n
        when TypeRegistry.TypemappedClasses.Contains(n.Name):
        return (new CollectionElement(Typemapped(n.Name).CsType, false, []), null);
      case NamedType { Name: "std::bitset" } n when BitsetWidth(n) is { } bits:
        return (new CollectionElement("ulong", false, [], $"%netocc_bitset({bits})"), null);
      case NamedType
      {
        Name: "opencascade::handle",
        TemplateArguments: [NamedType { TemplateArguments.Count: 0 } target]
      }:
        return registry.Class(target.Name) is { Kind: WrapKind.Transient } transient
          ? (new CollectionElement(target.Name, false, [transient.Use]), null)
          : (null, $"element handle of {target.Name}, which is not wrapped");
      // collections of collections (TopTools_DataMapOfShapeListOfShape): a plain one by value, a
      // handle-managed one by handle
      case NamedType
      {
        Name: "opencascade::handle",
        TemplateArguments: [NamedType { TemplateArguments.Count: > 0 } inner]
      }:
        return NestedCollection(inner, byHandle: true);
      case NamedType { TemplateArguments.Count: > 0 } n when n.Name != "opencascade::handle":
        return NestedCollection(n, byHandle: false);
      case NamedType { TemplateArguments.Count: 0 } n:
        if (registry.Class(n.Name) is not { } element)
        {
          return (null, $"element class {n.Name} is not wrapped");
        }

        if (element.Kind is not (WrapKind.ValueType or WrapKind.ValueClass))
        {
          return (null,
            element.Kind == WrapKind.Transient
              ? $"element {n.Name} is a transient held by value"
              : $"element {n.Name} isn't copyable");
        }

        return defaultConstructed && !element.HasDefaultConstructor
          ? (null, $"element {n.Name} has no default constructor")
          : (new CollectionElement(n.Name, element.Kind == WrapKind.ValueType, [element.Use]),
            null);
      default:
        return (null, $"element {type.Spelling} is not supported");
    }
  }

  private (CollectionElement? Element, string? Skip) NestedCollection(NamedType n, bool byHandle)
  {
    var (instantiation, uses, skip) = Collection(n);
    if (instantiation is null)
    {
      return (null, $"element {skip}");
    }

    return instantiation.Template.IsTransient == byHandle
      ? (new CollectionElement(instantiation.Alias, false, uses), null)
      : (null,
        byHandle
          ? $"element handle of {instantiation.Alias}, which isn't handle-managed"
          : $"element {instantiation.Alias} is handle-managed, held by value");
  }
}
