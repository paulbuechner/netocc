// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

//
using NetOcc.Generator.Model;

namespace NetOcc.Generator.Mapping;

/// <summary>
/// What an argument of a collection template is: an element C# sees (value, key or item), or a
/// hasher, which only C++ does.
/// </summary>
internal enum CollectionArgument
{
  Element,
  Hasher,
}

/// <summary>
/// A collection template the typemap library declares (netocc-core's Collections.i; math_VectorBase
/// in its math companion), with the macro that instantiates it: <c>%macro(NAME, each argument's C++
/// type, each element's C# type)</c>.
/// </summary>
/// <param name="Base">
/// For a handle-managed template (NCollection_HArray1), the collection it derives from.
/// </param>
/// <param name="DefaultConstructs">
/// The wrapped constructors default-construct elements (the arrays).
/// </param>
/// <param name="HasBlittableVariant">
/// A <c>_blittable</c> macro adds bulk copies when every element is blittable.
/// </param>
/// <param name="Include">
/// The header that declares the template, when it isn't <c>&lt;Name&gt;.hxx</c>.
/// </param>
internal sealed record CollectionTemplate(string Name, string Macro,
                                          IReadOnlyList<CollectionArgument> Arguments,
                                          string? Base = null, bool DefaultConstructs = false,
                                          bool HasBlittableVariant = false, string? Include = null)
{
  /// <summary>The header that declares the template.</summary>
  public string Header => Include ?? $"{Name}.hxx";

  private static readonly CollectionArgument[] Elements = [CollectionArgument.Element];
  private static readonly CollectionArgument[] Keys =
    [CollectionArgument.Element, CollectionArgument.Hasher];
  private static readonly CollectionArgument[] Items =
    [CollectionArgument.Element, CollectionArgument.Element, CollectionArgument.Hasher];
  private static readonly CollectionArgument[] Pair =
    [CollectionArgument.Element, CollectionArgument.Element];

  public static readonly IReadOnlyList<CollectionTemplate> All =
  [
    new("NCollection_Array1", "occt_array1", Elements, DefaultConstructs: true,
        HasBlittableVariant: true),
    new("NCollection_Array2", "occt_array2", Elements, DefaultConstructs: true),
    new("NCollection_List", "occt_list", Elements),
    new("NCollection_Sequence", "occt_sequence", Elements),
    new("NCollection_LinearVector", "occt_linearvector", Elements, HasBlittableVariant: true),
    new("NCollection_DynamicArray", "occt_dynamicarray", Elements),
    new("NCollection_HArray1", "occt_harray1", Elements, "NCollection_Array1",
        DefaultConstructs: true),
    new("NCollection_HArray2", "occt_harray2", Elements, "NCollection_Array2",
        DefaultConstructs: true),
    new("NCollection_HSequence", "occt_hsequence", Elements, "NCollection_Sequence"),
    new("NCollection_Map", "occt_map", Keys),
    new("NCollection_IndexedMap", "occt_indexedmap", Keys),
    new("NCollection_DataMap", "occt_datamap", Items),
    new("NCollection_FlatMap", "occt_flatmap", Keys),
    new("NCollection_FlatDataMap", "occt_flatdatamap", Items),
    new("NCollection_IndexedDataMap", "occt_indexeddatamap", Items),
    new("math_VectorBase", "occt_math_vector", Elements, DefaultConstructs: true),
    // two values of any element type, as First and Second (netocc-core's Std.i)
    new("std::pair", "netocc_pair", Pair, Include: "utility"),
  ];

  public bool IsTransient => Base is not null;

  public static CollectionTemplate? Named(string name) => All.FirstOrDefault(t => t.Name == name);
}

/// <summary>
/// A collection instantiation, named by OCCT's alias for it (<c>typedef
/// NCollection_Array1&lt;gp_Pnt&gt; TColgp_Array1OfPnt</c>): the C# class in the alias's package.
/// The first alias in package order names it. One no alias names gets a name of its own
/// (<c>NCollection_Array1_BRepGraph_NodeId</c>) in the package of its first user. Equal by
/// spelling.
/// </summary>
/// <param name="Arguments">
/// The template arguments as the .i spells them: <c>gp_Pnt</c>,
/// <c>opencascade::handle&lt;Geom_Curve&gt;</c>, hashers.
/// </param>
/// <param name="ArgumentTypes">
/// The parsed arguments; unknown for scanned modules, which this run doesn't write.
/// </param>
internal sealed record KnownInstantiation(CollectionTemplate Template,
                                          IReadOnlyList<string> Arguments, string Alias,
                                          string Package,
                                          IReadOnlyList<CppType>? ArgumentTypes = null)
{
  /// <summary>
  /// The instantiation as the .i spells it, e.g. <c>NCollection_Array1&lt;gp_Pnt&gt;</c>.
  /// </summary>
  public string Spelling => Spell(Template.Name, Arguments);

  /// <summary>The template's header, which declares the instantiation.</summary>
  public string Header => Template.Header;

  public bool Equals(KnownInstantiation? other) => other is not null && Spelling == other.Spelling;

  public override int GetHashCode() => Spelling.GetHashCode(StringComparison.Ordinal);

  public static string Spell(string template, IEnumerable<string> arguments) =>
    $"{template}<{string.Join(", ", arguments)}>";
}
