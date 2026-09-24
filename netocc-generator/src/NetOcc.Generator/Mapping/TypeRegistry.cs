// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

//
using NetOcc.Generator.Model;

namespace NetOcc.Generator.Mapping;

/// <summary>How netocc-core wraps a class (see its CLAUDE.md, "Wrapper contract").</summary>
internal enum WrapKind
{
  /// <summary>Standard_Transient subclass: %occt_transient, Handle(T) ⇄ proxy.</summary>
  Transient,

  /// <summary>Copyable class: %occt_valueclass, const&amp; returns are owned copies.</summary>
  ValueClass,

  /// <summary>
  /// C# struct with the C++ layout (%occt_valuetype): generated, with a hand-written partial for
  /// managed members.
  /// </summary>
  ValueType,

  /// <summary>Other classes (not copyable, or abstract): plain proxies.</summary>
  Plain,
}

/// <param name="Header">The header that declares the class; null: the one named after it.</param>
/// <param name="HasDefaultConstructor">
/// <c>T()</c> compiles: arrays default-construct their elements.
/// </param>
/// <param name="IsMoveOnly">
/// Not copyable, but movable: a by-value return moves into the proxy (SwigValueWrapper).
/// </param>
internal sealed record KnownClass(string Name, string Package, WrapKind Kind, string? Header = null,
                                  bool HasDefaultConstructor = true, bool IsMoveOnly = false)
{
  public string DeclaringHeader => Header ?? $"{Name}.hxx";

  /// <summary>What a declaration using the class needs: its module and header.</summary>
  public TypeUse Use => new(Package, DeclaringHeader);
}

/// <param name="Header">The header that declares the enum; null: the one named after it.</param>
/// <param name="Underlying">
/// The underlying type when it isn't int (unsigned char), otherwise null.
/// </param>
/// <param name="QualifiedName">
/// C++'s name of a nested or namespace enum, whose flat name is an alias.
/// </param>
internal sealed record KnownEnum(string Name, string Package, string? Header = null,
                                 string? Underlying = null, string? QualifiedName = null)
{
  public string DeclaringHeader => Header ?? $"{Name}.hxx";

  /// <summary>What a declaration using the enum needs: its module and header.</summary>
  public TypeUse Use => new(Package, DeclaringHeader);
}

/// <summary>
/// Every type the generated code may use, and where it lives. Filled from the packages being
/// generated and from the .i files of the modules a run doesn't write (<see cref="ScanModule"/>).
/// </summary>
internal sealed partial class TypeRegistry
{
  /// <summary>
  /// Types the common typemap library maps to .NET types (Strings.i, Guid.i): no proxy, no import.
  /// </summary>
  public static readonly IReadOnlySet<string> TypemappedClasses = new HashSet<string>
  {
    "TCollection_AsciiString",
    "TCollection_ExtendedString",
    "Standard_GUID",
  };

  private readonly Dictionary<string, KnownClass> _classes = [];
  private readonly Dictionary<string, KnownEnum> _enums = [];
  private readonly Dictionary<string, KnownInstantiation> _instantiations = [];
  private readonly Dictionary<string, string> _instances = [];
  private readonly HashSet<string> _requested = [];
  private readonly Dictionary<string, List<string>> _imports = [];
  private readonly Dictionary<string, List<string>> _bases = [];

  public void AddClass(KnownClass known) => _classes[known.Name] = known;

  public void AddEnum(string name, string package, string? header = null, string? underlying = null,
                      string? qualifiedName = null) =>
    _enums[name] = new KnownEnum(name, package, header, underlying, qualifiedName);

  /// <summary>
  /// Registers a class template instance as the class of that name (<see cref="ClassInstance"/>).
  /// </summary>
  public void AddInstance(NamedType type, string name) =>
    _instances[(type with { Const = false }).Spelling] = name;

  /// <summary>
  /// A signature type with every class template instance that is a class of its own
  /// (<see cref="AddInstance"/>) named by that class, whose C++ alias the generated code spells:
  /// SWIG takes the alias and the template for different types.
  /// </summary>
  public CppType Resolve(CppType type) => type switch
  {
    NamedType { TemplateArguments.Count: > 0 } n when _instances.TryGetValue(
      (n with { Const = false }).Spelling,
      out var name) => new NamedType(name, n.Kind, [], n.Const),
    NamedType { TemplateArguments.Count: > 0 } n => n with
    {
      TemplateArguments = [.. n.TemplateArguments.Select(Resolve)]
    },
    PointerType p => p with { Pointee = Resolve(p.Pointee) },
    ReferenceType r => r with { Referee = Resolve(r.Referee) },
    ArrayType a => a with { Element = Resolve(a.Element) },
    FunctionType f => f with
    {
      Return = Resolve(f.Return), Parameters = [.. f.Parameters.Select(Resolve)]
    },
    _ => type,
  };

  /// <summary>Registers an alias; the first one for an instantiation names it.</summary>
  public void AddInstantiation(KnownInstantiation instantiation) =>
    _instantiations.TryAdd(instantiation.Spelling, instantiation);

  /// <summary>
  /// The package of a synthesized instantiation until <see cref="AssignOwners"/> gives it its first
  /// user's.
  /// </summary>
  public const string Unowned = "";

  public KnownClass? Class(string name) => _classes.GetValueOrDefault(name);

  public KnownEnum? Enum(string name) => _enums.GetValueOrDefault(name);

  /// <summary>
  /// The instantiation of a collection template, requested or not: the registered one, or, when no
  /// alias names it, one with a name of its own, unowned until the first writer pass
  /// (<see cref="AssignOwners"/>).
  /// </summary>
  public KnownInstantiation? Instantiation(NamedType type)
  {
    if (CollectionTemplate.Named(type.Name) is not { } template
        || type.TemplateArguments.Count != template.Arguments.Count)
    {
      return null;
    }

    List<CppType> arguments = [.. type.TemplateArguments.Select(a => Resolve(a.WithoutConst()))];
    var spelling = KnownInstantiation.Spell(template.Name, arguments.Select(a => a.Spelling));
    if (_instantiations.TryGetValue(spelling, out var known))
    {
      return known;
    }

    var synthesized = new KnownInstantiation(template, [.. arguments.Select(a => a.Spelling)],
                                             FreeName(SynthesizedName(template, arguments)),
                                             Unowned, arguments);
    _instantiations[spelling] = synthesized;
    return synthesized;
  }

  /// <summary>
  /// The collection a handle-managed instantiation derives from: its alias's, or one with a name of
  /// its own.
  /// </summary>
  public KnownInstantiation? BaseOf(KnownInstantiation instantiation) =>
    instantiation.Template.Base is not { } template
      ? null
      :
      instantiation.ArgumentTypes is { } types
        ?
        Instantiation(new NamedType(template, NamedKind.Class, types))
        : _instantiations.GetValueOrDefault(
          KnownInstantiation.Spell(template, instantiation.Arguments));

  /// <summary>
  /// Gives each synthesized instantiation the package of its first user, in package order, after
  /// the first writer pass saw every use; the collections it needs (its base, the collections it
  /// holds) go with it where they have none yet.
  /// </summary>
  /// <param name="uses">Each package's used instantiations, in package order.</param>
  /// <param name="packages">
  /// The packages this run writes: one that names no other package's type goes to its template's.
  /// </param>
  public void AssignOwners(
    IEnumerable<(string Package, IEnumerable<KnownInstantiation> Collections)> uses,
    IReadOnlySet<string>? packages = null)
  {
    void Own(KnownInstantiation instantiation, string package)
    {
      if (_instantiations[instantiation.Spelling] is not { Package: Unowned } unowned)
      {
        return;
      }

      _instantiations[instantiation.Spelling] = unowned with { Package = package };
      foreach (var dependency in DependenciesOf(unowned))
      {
        Own(dependency, package);
      }
    }

    List<(string Package, IEnumerable<KnownInstantiation> Collections)> used = [.. uses];
    foreach (var collection in used.SelectMany(u => u.Collections))
    {
      if (collection.ArgumentTypes is { } arguments
          && new NamedType(collection.Template.Name, NamedKind.Class, arguments).TemplatePackage is
            { } home
          && packages?.Contains(home) == true)
      {
        Own(collection, home);
      }
    }

    foreach (var (package, collections) in used)
    {
      foreach (var collection in collections)
      {
        Own(collection, package);
      }
    }
  }

  // a name for an instantiation no alias names: the template and its arguments, flattened
  // (NCollection_Array1_gp_Pnt2d, NCollection_DataMap_TopoDS_Shape_Handle_Geom_Curve); a default
  // hasher is left out
  private string SynthesizedName(CollectionTemplate template, IReadOnlyList<CppType> arguments)
  {
    List<string> parts = [template.Name.Replace("::", "_", StringComparison.Ordinal)];
    for (var i = 0; i < arguments.Count; i++)
    {
      if (template.Arguments[i] != CollectionArgument.Hasher
          || arguments[i] is not NamedType { Name: "NCollection_DefaultHasher" })
      {
        parts.Add(Flat(arguments[i]));
      }
    }

    return string.Join("_", parts);
  }

  private string Flat(CppType type)
  {
    var flat = type switch
    {
      NamedType { Name: "opencascade::handle", TemplateArguments: [var target] } =>
        $"Handle_{Flat(target)}",
      NamedType { TemplateArguments.Count: > 0 } n when Instantiation(n) is { } nested => nested
        .Alias,
      NamedType n => string.Join("_", n.TemplateArguments.Select(Flat).Prepend(n.Name)),
      _ => type.Spelling,
    };
    return NonWord().Replace(flat.Replace("::", "_", StringComparison.Ordinal), "_");
  }

  // a synthesized name another class or alias may have already: then numbered
  private string FreeName(string name)
  {
    var free = name;
    for (var i = 2;
         _classes.ContainsKey(free) || _instantiations.Values.Any(k => k.Alias == free);
         i++)
    {
      free = $"{name}_{i}";
    }

    return free;
  }

  // what the instantiation's macro needs instantiated first: its base, and the collections among
  // its arguments (by value or, handle-managed, by handle)
  private IEnumerable<KnownInstantiation> DependenciesOf(KnownInstantiation instantiation)
  {
    if (BaseOf(instantiation) is { } baseCollection)
    {
      yield return baseCollection;
    }

    foreach (var argument in instantiation.ArgumentTypes ?? [])
    {
      var collection = argument is NamedType
      {
        Name: "opencascade::handle", TemplateArguments: [NamedType inner]
      }
        ? inner
        : argument as NamedType;
      if (collection is { TemplateArguments.Count: > 0 } && Instantiation(collection) is { } nested)
      {
        yield return nested;
      }
    }
  }

  private int Depth(KnownInstantiation instantiation) => DependenciesOf(instantiation)
    .Select(d => Depth(d) + 1)
    .DefaultIfEmpty(0)
    .Max();

  /// <summary>
  /// Marks an instantiation as used by a signature, so its package instantiates it, and a
  /// handle-managed one's base too. Collections nothing uses stay out of the interface files.
  /// </summary>
  public void Request(KnownInstantiation instantiation)
  {
    if (_requested.Add(instantiation.Spelling) && BaseOf(instantiation) is { } baseCollection)
    {
      Request(baseCollection);
    }
  }

  public int RequestCount => _requested.Count;

  /// <summary>
  /// The requested instantiations a package owns, each after the ones it needs (bases, collections
  /// it holds).
  /// </summary>
  public IReadOnlyList<KnownInstantiation> RequestedIn(string package) =>
  [
    .. _instantiations.Values.Where(i => i.Package == package && _requested.Contains(i.Spelling))
      .OrderBy(Depth)
      .ThenBy(i => i.Alias, StringComparer.Ordinal)
  ];

  /// <summary>The %import list of a module this run doesn't write (its dependencies).</summary>
  public IReadOnlyList<string> ImportsOf(string package) =>
    _imports.GetValueOrDefault(package) ?? [];

  /// <summary>
  /// The other packages the classes of a module this run doesn't write derive from.
  /// </summary>
  public IReadOnlyCollection<string> BasePackagesOf(string package) =>
  [
    .. (_bases.GetValueOrDefault(package) ?? []).Select(b => Class(b)?.Package)
    .OfType<string>()
    .Where(p => p != package)
    .Distinct()
  ];

  [GeneratedRegex(@"\W+")]
  private static partial Regex NonWord();
}
