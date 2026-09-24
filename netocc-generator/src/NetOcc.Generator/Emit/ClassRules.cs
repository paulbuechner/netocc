// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

//
using NetOcc.Generator.Config;
using NetOcc.Generator.Mapping;
using NetOcc.Generator.Model;

namespace NetOcc.Generator.Emit;

/// <summary>
/// What a package's classes become: a proxy, a struct or nothing, as its config picks and C# covers
/// them.
/// </summary>
internal static class ClassRules
{
  /// <summary>
  /// Not copyable, but movable and deletable by its proxy: a by-value return moves into a proxy
  /// that owns it.
  /// </summary>
  public static bool IsMoveOnly(ClassModel c) =>
    c.Traits is
    {
      IsCopyable: false, IsMovable: true, IsTransient: false, IsAbstract: false, IsCreatable: true,
      HasPublicDestructor: true
    };

  /// <summary>
  /// Picked by the package config: listed in <c>classes</c> (itself or its outer class; or no
  /// list), not excluded. A class template instance the package owns always is: <c>classes</c>
  /// lists the package's own classes, and an instance serves the signatures of every package that
  /// uses it.
  /// </summary>
  public static bool IsListed(ClassModel c, PackageConfig config) =>
    (config.Classes is null
     || c.Instance is not null
     || config.Classes.Contains(c.Name)
     || (c.QualifiedName is { } qualified
         && config.Classes.Any(o => qualified.StartsWith($"{o}::", StringComparison.Ordinal))))
    && !config.ExcludeClasses.Contains(c.Name);

  /// <summary>A C# struct: configured in <c>value_types</c>, or plain data.</summary>
  public static bool IsValueType(ClassModel c, PackageConfig config) =>
    config.ValueTypes.Contains(c.Name) || c.IsPlainData;

  /// <summary>Gets a proxy class: listed and not a value type.</summary>
  public static bool IsSelected(ClassModel c, PackageConfig config) =>
    IsListed(c, config) && !IsValueType(c, config);

  /// <summary>
  /// C# covers the class otherwise, so it gets no proxy (not a skip): strings and GUIDs are .NET
  /// types through the common typemaps (Strings.i, Guid.i); OCCT's legacy Handle_T classes
  /// (DEFINE_STANDARD_HANDLE) are T's proxy, the handle in C#.
  /// </summary>
  public static bool IsCovered(ClassModel c) => TypeRegistry.TypemappedClasses.Contains(c.Name)
                                                || c.Name.StartsWith(
                                                  "Handle_", StringComparison.Ordinal);

  /// <summary>
  /// How a proxied class is wrapped: a transient, a value class (copyable, its copies owned by
  /// proxies, which delete them) or plain.
  /// </summary>
  public static WrapKind KindOf(ClassModel c) =>
    c.Traits.IsTransient
      ? WrapKind.Transient
      :
      c.Traits is
        { IsCopyable: true, IsAbstract: false, IsCreatable: true, HasPublicDestructor: true }
        ?
        WrapKind.ValueClass
        : WrapKind.Plain;

  /// <summary>The package's classes that get proxies: selected, not covered otherwise.</summary>
  public static IEnumerable<ClassModel> Proxied(PackageModel package, PackageConfig config) =>
    package.Classes.Where(c => IsSelected(c, config) && !IsCovered(c));

  /// <summary>
  /// The package's C# structs: the configured value types, then the plain data, as parsed (their
  /// layout goes into the guards).
  /// </summary>
  internal static List<ClassModel> ValueTypes(PackageModel package, PackageConfig config) =>
  [
    .. config.ValueTypes.Select(name => package.Classes.FirstOrDefault(c => c.Name == name)
                                        ?? throw new InvalidOperationException(
                                          $"{package.Name}: value type {name} is not a class of the package")),
    .. package.Classes.Where(c => c.IsPlainData
                                  && !config.ValueTypes.Contains(c.Name)
                                  && IsListed(c, config)
                                  && !IsCovered(c)),
  ];
}
