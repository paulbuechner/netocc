// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

//
using NetOcc.Generator.Config;
using NetOcc.Generator.Mapping;
using NetOcc.Generator.Model;

namespace NetOcc.Generator.Emit;

/// <summary>
/// A type C# has, as OCCT's reference manual documents it (netocc-documentation's class index links
/// each to its page).
/// </summary>
/// <param name="Name">The C# name.</param>
/// <param name="Kind">
/// The page's: <c>class</c>, <c>struct</c>, <c>enum</c> (its header's page, a nested one its
/// class's), <c>namespace</c>.
/// </param>
/// <param name="Cpp">What the page documents: the C++ name, or the template of an instance.</param>
/// <param name="Header">An enum's header.</param>
/// <param name="Instance">
/// The type is an instance of the template <paramref name="Cpp"/> (a collection, a template
/// instance).
/// </param>
internal sealed record DocumentedType(string Name, string Kind, string Cpp, string? Header = null,
                                      bool? Instance = null);

/// <summary>
/// netocc-core's <c>src/SWIG_files/classes.json</c>: the types each package gives C#.
/// </summary>
internal static class ClassIndex
{
  private static readonly JsonSerializerOptions Options = new()
  {
    WriteIndented = true,
    NewLine = "\n",
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    // C++ names hold angle brackets, which the default encoder escapes
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
  };

  /// <summary>
  /// The package's types, by C# name: classes and structs, collections, enums, namespaces.
  /// </summary>
  /// <param name="namespaces">
  /// The namespaces the module gives a static class (<see cref="GeneratedModule.Namespaces"/>).
  /// </param>
  public static List<DocumentedType> Of(PackageModel model, PackageConfig config,
                                        TypeRegistry registry, IReadOnlyList<string> namespaces)
  {
    List<DocumentedType> types = [];
    foreach (var c in ClassRules.ValueTypes(model, config)
               .Concat(ClassRules.Proxied(model, config)))
    {
      var kind = c.IsStruct ? "struct" : "class";
      types.Add(c.Template is { } template
                  ? new DocumentedType(c.Name, kind, template, Instance: true)
                  : new DocumentedType(c.Name, kind, c.QualifiedName ?? c.Name));
    }

    types.AddRange(registry.RequestedIn(model.Name)
                     .Select(i => new DocumentedType(i.Alias, "class", i.Template.Name,
                                                     Instance: true)));
    types.AddRange(
      model.Enums.Select(e => new DocumentedType(e.Name, "enum", e.QualifiedName ?? e.Name,
                                                 e.Header)));
    types.AddRange(
      namespaces.Select(n => new DocumentedType(n.Replace("::", "_"), "namespace", n)));
    return [.. types.DistinctBy(t => t.Name).OrderBy(t => t.Name, StringComparer.Ordinal)];
  }

  /// <summary>
  /// The JSON: packages in <paramref name="order"/>, each from <paramref name="packages"/> or, for
  /// a run over some packages, from the <paramref name="previous"/> file.
  /// </summary>
  public static string Json(IReadOnlyList<string> order,
                            IReadOnlyDictionary<string, List<DocumentedType>> packages,
                            string? previous)
  {
    var old = previous is null ? null : JsonNode.Parse(previous)?.AsObject();
    var index = new JsonObject();
    foreach (var package in order)
    {
      if (packages.TryGetValue(package, out var types))
      {
        index[package] = JsonSerializer.SerializeToNode(types, Options);
      }
      else if (old?[package] is { } kept)
      {
        index[package] = kept.DeepClone();
      }
    }

    return index.ToJsonString(Options) + "\n";
  }
}
