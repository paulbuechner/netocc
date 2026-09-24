// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.IO;
using System.Linq;

//
using NetOcc.Generator.Model;
using NetOcc.Generator.Parsing;

namespace NetOcc.Generator;

/// <summary>
/// Plain-text view of a parsed package, for <c>netocc-gen dump</c>: what generate's parse sees.
/// </summary>
internal static class Dump
{
  /// <param name="occtLibraries">OCCT's DLLs, for the export check (as generate).</param>
  public static int Run(Workspace workspace, string occtInclude, string occtLibraries,
                        string package, TextWriter output)
  {
    var parser = new PackageParser(PackageParser.IncludeDirectories(occtInclude), [],
                                   LibraryExports.Load(occtLibraries));
    HashSet<string> valueTypes =
      [.. workspace.Config.Packages.Values.SelectMany(p => p.ValueTypes)];
    Write(Generate.Parse(workspace, parser, valueTypes, package), output);
    return 0;
  }

  public static void Write(PackageModel package, TextWriter output)
  {
    output.WriteLine($"package {package.Name}: {package.Headers.Count} headers");
    foreach (var e in package.Enums)
    {
      output.WriteLine(
        $"enum {e.Name} ({e.Header}): {string.Join(", ", e.Constants.Select(c => $"{c.Name}={c.Value}"))}");
    }

    foreach (var t in package.Typedefs)
    {
      output.WriteLine($"typedef {t.Name} = {t.Target} ({t.Header})");
    }

    foreach (var c in package.Classes)
    {
      var bases = c.Ancestors.Count == 0 ? "" : " : " + string.Join(" < ", c.Ancestors);
      output.WriteLine($"class {c.Name}{bases} ({c.Header}) {c.Traits} {c.Layout}");
      foreach (var field in c.Fields ?? [])
      {
        output.WriteLine($"  field {field.Type} {field.Name} @ {field.Offset}");
      }

      foreach (var o in c.Operators ?? [])
      {
        output.WriteLine(
          $"  operator {o.Return} {o.Name}({Parameters(o.Parameters)}){(o.IsConst ? " const" : "")}{(o.IsStatic ? " static" : "")}");
      }

      foreach (var ctor in c.Constructors)
      {
        output.WriteLine(
          $"  {c.Name}({Parameters(ctor.Parameters)}){(ctor.IsDeprecated ? " [deprecated]" : "")}");
      }

      foreach (var m in c.Methods)
      {
        var prefix = (m.IsStatic ? "static " : "") + (m.IsVirtual ? "virtual " : "");
        output.WriteLine(
          $"  {prefix}{m.Return} {m.Name}({Parameters(m.Parameters)}){(m.IsConst ? " const" : "")}{(m.IsDeprecated ? " [deprecated]" : "")}");
      }
    }

    foreach (var f in package.NamespaceFunctions)
    {
      output.WriteLine(
        $"function {f.Namespace}::{f.Name}({Parameters(f.Parameters)}) -> {f.Return}");
    }
  }

  private static string Parameters(IReadOnlyList<ParameterModel> parameters) =>
    string.Join(
      ", ",
      parameters.Select(p => p.Default is null
                          ? $"{p.Type} {p.Name}"
                          : $"{p.Type} {p.Name} = {p.Default}"));
}
