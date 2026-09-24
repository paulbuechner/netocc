// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// YamlDotNet
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetOcc.Generator.Config;

/// <summary>config/modules.yaml: which packages to generate and how. Hand-edited.</summary>
internal sealed class GeneratorConfig
{
  /// <summary>
  /// What to generate: package, toolkit or module names; a toolkit or module stands for all its
  /// packages.
  /// </summary>
  public List<string> Generate { get; set; } = [];

  /// <summary>
  /// Packages a toolkit or module in <see cref="Generate"/> doesn't bring in, e.g.
  /// platform-specific ones.
  /// </summary>
  public List<string> ExcludePackages { get; set; } = [];

  /// <summary>
  /// Packages OCCT 8 dropped whose collection aliases remain (TColgp_Array1OfPnt), with the OCCT
  /// module they belonged to: each gets a module that holds those collections, after the packages
  /// of <see cref="Generate"/>.
  /// </summary>
  public Dictionary<string, string> AliasPackages { get; set; } = [];

  public Dictionary<string, PackageConfig> Packages { get; set; } = [];

  public PackageConfig For(string package) =>
    Packages.TryGetValue(package, out var config) ? config : new PackageConfig();

  public static GeneratorConfig Load(string path) =>
    new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance)
      .Build()
      .Deserialize<GeneratorConfig>(File.ReadAllText(path))
    ?? new GeneratorConfig();
}

internal sealed class PackageConfig
{
  /// <summary>
  /// Only these classes; null means every class of the package, an empty list means enums only.
  /// </summary>
  public List<string>? Classes { get; set; }

  public List<string> ExcludeClasses { get; set; } = [];

  /// <summary>
  /// <c>Class::Method</c> and <c>Namespace::Function</c> entries left out, all overloads.
  /// </summary>
  public List<string> ExcludeMethods { get; set; } = [];

  /// <summary>
  /// C++ namespaces whose types and functions are left out (implementation details in public
  /// headers).
  /// </summary>
  public List<string> ExcludeNamespaces { get; set; } = [];

  /// <summary>
  /// Declared in one of <see cref="ExcludeNamespaces"/>: a qualified name like <c>step::parser</c>,
  /// or a namespace.
  /// </summary>
  public bool IsExcludedScope(string? qualifiedName) =>
    qualifiedName is not null
    && ExcludeNamespaces.Any(ns => qualifiedName == ns
                                   || qualifiedName.StartsWith(
                                     $"{ns}::", StringComparison.Ordinal));

  /// <summary>
  /// Headers included before the package's own, for package headers that miss an include.
  /// </summary>
  public List<string> Prelude { get; set; } = [];

  /// <summary>
  /// Classes that are C# structs (generated into netocc-core's src/NetOcc/&lt;Package&gt;/, where a
  /// hand-written partial may add managed members): %occt_valuetype and layout guards, no proxy.
  /// </summary>
  public List<string> ValueTypes { get; set; } = [];
}
