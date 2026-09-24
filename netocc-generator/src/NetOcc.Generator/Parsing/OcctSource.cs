// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// YamlDotNet
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetOcc.Generator.Parsing;

/// <summary>
/// The OCCT source tree as the generator needs it: which module and toolkit a package belongs to
/// (config/toolkits.yaml, written by bootstrap) and which headers it has
/// (src/&lt;Module&gt;/&lt;TK&gt;/&lt;Package&gt;/*.hxx).
/// </summary>
internal sealed class OcctSource
{
  private readonly string _sourceRoot;
  private readonly string? _includeDirectory;
  private readonly Dictionary<string, (string Module, string Toolkit)> _packages =
    new(StringComparer.Ordinal);
  private readonly Dictionary<string, List<string>> _moduleToolkits = new(StringComparer.Ordinal);
  private readonly Dictionary<string, List<string>> _toolkitPackages = new(StringComparer.Ordinal);

  // the installed headers, sorted: thousands of names, listed once for the packages to pick theirs
  // from
  private readonly Lazy<string[]> _installedHeaders;

  /// <param name="includeDirectory">
  /// OCCT's installed headers, which stand in for a missing source tree (CI restores OCCT without
  /// its sources): the headers named after the package, as OCCT names each.
  /// </param>
  public OcctSource(string sourceRoot, string toolkitsYaml, string? includeDirectory = null)
  {
    _sourceRoot = sourceRoot;
    _includeDirectory = includeDirectory;
    _installedHeaders = new(() =>
    [
      .. Directory.EnumerateFiles(includeDirectory!, "*.hxx")
        .Select(Path.GetFileName)
        .OfType<string>()
        .Order(StringComparer.Ordinal)
    ]);
    var file = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
      .Build()
      .Deserialize<ToolkitsFile>(File.ReadAllText(toolkitsYaml));
    Version = file.Occt;
    foreach (var (module, toolkits) in file.Modules)
    {
      _moduleToolkits[module] = [.. toolkits.Keys];
      foreach (var (toolkit, packages) in toolkits)
      {
        _toolkitPackages[toolkit] = packages;
        foreach (var package in packages)
        {
          _packages[package] = (module, toolkit);
        }
      }
    }
  }

  public string Version { get; }

  /// <summary>OCCT's modules in toolkits.yaml's order, which is OCCT's.</summary>
  public IReadOnlyList<string> Modules => [.. _moduleToolkits.Keys];

  public string ModuleOf(string package) => Locate(package).Module;

  /// <summary>
  /// What a config entry stands for, in toolkits.yaml order: a module's or toolkit's packages, or
  /// the package itself.
  /// </summary>
  public IReadOnlyList<string> Expand(string name)
  {
    if (_moduleToolkits.TryGetValue(name, out var toolkits))
    {
      return [.. toolkits.SelectMany(t => _toolkitPackages[t])];
    }

    if (_toolkitPackages.TryGetValue(name, out var packages))
    {
      return packages;
    }

    Locate(name);
    return [name];
  }

  /// <summary>
  /// The package's public headers, sorted: file names, as included from the flat include directory.
  /// </summary>
  public IReadOnlyList<string> Headers(string package)
  {
    var (module, toolkit) = Locate(package);
    var directory = Path.Combine(_sourceRoot, "src", module, toolkit, package);
    if (!Directory.Exists(directory) && _includeDirectory is not null)
    {
      return Named(package);
    }

    if (!Directory.Exists(directory))
    {
      throw new DirectoryNotFoundException($"{package}: no source directory {directory}");
    }

    return
    [
      .. Directory.EnumerateFiles(directory, "*.hxx")
        .Select(Path.GetFileName)
        .OfType<string>()
        .Order(StringComparer.Ordinal)
    ];
  }

  /// <summary>
  /// OCCT 8's deprecated typedefs for NCollection instantiations that are named after the package
  /// (src/Deprecated/NCollectionAliases/TColgp_Array1OfPnt.hxx: <c>typedef
  /// NCollection_Array1&lt;gp_Pnt&gt; TColgp_Array1OfPnt</c>). They name the C# collection classes,
  /// also of packages OCCT 8 dropped (TColgp).
  /// </summary>
  public IReadOnlyList<string> AliasHeaders(string package)
  {
    var directory = Path.Combine(_sourceRoot, "src", "Deprecated", "NCollectionAliases");
    if (!Directory.Exists(directory) && _includeDirectory is not null)
    {
      // installed with the others: a package OCCT 8 dropped (TColgp) has only these, the others'
      // come with Headers
      return _packages.ContainsKey(package) ? [] : Named(package);
    }

    return Directory.Exists(directory)
      ?
      [
        .. Directory.EnumerateFiles(directory, $"{package}_*.hxx")
          .Select(Path.GetFileName)
          .OfType<string>()
          .Order(StringComparer.Ordinal)
      ]
      : [];
  }

  // the installed headers named after the package: Package.hxx and Package_*.hxx
  private List<string> Named(string package) =>
  [
    .. _installedHeaders.Value.Where(f => f == $"{package}.hxx"
                                          || f.StartsWith($"{package}_", StringComparison.Ordinal))
  ];

  private (string Module, string Toolkit) Locate(string package) =>
    _packages.TryGetValue(package, out var location)
      ? location
      : throw new KeyNotFoundException(
        $"{package} is not in toolkits.yaml (re-run bootstrap after an OCCT upgrade)");

  private sealed class ToolkitsFile
  {
    public string Occt { get; set; } = "";

    public Dictionary<string, Dictionary<string, List<string>>> Modules { get; set; } = [];
  }
}
