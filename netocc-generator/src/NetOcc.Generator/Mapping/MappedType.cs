// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

//
using NetOcc.Generator.Model;

namespace NetOcc.Generator.Mapping;

/// <summary>
/// A type the generated code needs: its module (for %import) and header (for the module header).
/// </summary>
/// <param name="Collection">
/// For an NCollection instantiation, which one: its package instantiates it once a member uses it.
/// </param>
internal sealed record TypeUse(string Package, string Header,
                               KnownInstantiation? Collection = null);

/// <summary>
/// A collection's element in C#: <c>double</c>, <c>gp_Pnt</c>, <c>TopoDS_Shape</c>, <c>string</c>,
/// <c>Geom_Curve</c> (a handle), <c>TopTools_ListOfShape</c> (a collection).
/// </summary>
/// <param name="IsBlittable">
/// C# copies it as bytes (numbers and value-type structs), so an array of it gets bulk copies.
/// </param>
/// <param name="Typemap">
/// The macro the element's type needs in the module that instantiates the collection (a bitset's).
/// </param>
internal sealed record CollectionElement(string CsType, bool IsBlittable,
                                         IReadOnlyList<TypeUse> Uses, string? Typemap = null);

/// <summary>
/// A collection's template arguments in C#: the elements' C# types, in order (hashers have none),
/// and what they use.
/// </summary>
/// <param name="IsBlittable">Every element is blittable.</param>
/// <param name="Typemaps">
/// The macros the elements' types need (<see cref="CollectionElement.Typemap"/>).
/// </param>
internal sealed record CollectionArguments(IReadOnlyList<string> CsTypes, bool IsBlittable,
                                           IReadOnlyList<TypeUse> Uses,
                                           IReadOnlyList<string> Typemaps);

/// <param name="Spelling">
/// The type as the .i declares it (C++ spelling the netocc-core typemaps match).
/// </param>
/// <param name="CsKey">What the type becomes in a C# signature, for overload collisions.</param>
/// <param name="CsType">
/// The type in a C# signature as the typemaps produce it, parameter modifier included: <c>in
/// gp_Pnt</c>, <c>ref double</c>, <c>string</c>. Returns never have a modifier.
/// </param>
/// <param name="Typemap">
/// A typemap macro the module calls before its declarations, for a type the typemap library can't
/// cover generically: <c>%netocc_address(%arg(double*))</c>.
/// </param>
/// <param name="Declared">
/// The type a parameter is declared with when that isn't <c>Spelling name</c>: a C array, <c>const
/// double theJ[3][3]</c>.
/// </param>
internal sealed record MappedType(string Spelling, IReadOnlyList<TypeUse> Uses, string CsKey,
                                  string CsType, string? Typemap = null, CppType? Declared = null)
{
  /// <summary>The parameter declaration in the .i.</summary>
  public string Declare(string name) => Declared?.Declare(name) ?? $"{Spelling} {name}";
}

/// <summary>Either a mapped type or the reason it can't be wrapped (for the skip log).</summary>
internal readonly record struct TypeMapping(MappedType? Type, string? Skip)
{
  public static TypeMapping Of(string spelling, string csKey, string csType,
                               params TypeUse[] uses) =>
    new(new MappedType(spelling, uses, csKey, csType), null);

  public static TypeMapping Skipped(string reason) => new(null, reason);
}
