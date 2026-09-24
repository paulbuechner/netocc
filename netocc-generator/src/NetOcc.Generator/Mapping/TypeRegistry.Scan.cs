// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NetOcc.Generator.Mapping;

// the part of TypeRegistry that reads the modules a run doesn't write from their .i files
internal sealed partial class TypeRegistry
{
  /// <summary>
  /// Reads what a module this run doesn't write provides, as a run writing it would know it:
  /// classes with their kind, default constructor and move-only status (%nodefaultctor, the value
  /// wrapper), value types, enums with their underlying type (csbase), the flat names of nested
  /// types with the header that declares them (<c>&lt;Pkg&gt;_nested.hxx</c> next to the .i's
  /// <c>headers</c>), collections, namespace shims, imports and bases.
  /// </summary>
  public void ScanModule(string interfaceFile)
  {
    var package = Path.GetFileNameWithoutExtension(interfaceFile);
    var text = File.ReadAllText(interfaceFile);
    _imports[package] = [.. ImportPattern().Matches(text).Select(m => m.Groups[1].Value)];
    _bases[package] = [.. BasePattern().Matches(text).Select(m => m.Groups[1].Value)];

    // the nested header's aliases: flat name -> C++ name, declared there
    var nestedHeader = $"{package}_nested.hxx";
    var nestedPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(interfaceFile))!, "..",
                                  "headers", nestedHeader);
    Dictionary<string, string> nested = File.Exists(nestedPath)
      ? NestedPattern()
        .Matches(File.ReadAllText(nestedPath))
        .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value)
      : [];
    string? HeaderOf(string name) => nested.ContainsKey(name) ? nestedHeader : null;

    HashSet<string> Named(string macro) =>
    [
      .. MacroPattern()
        .Matches(text)
        .Where(m => m.Groups[1].Value == macro)
        .Select(m => m.Groups[2].Value)
    ];

    // %occt_handle: the root, Standard_Transient
    var transients = Named("occt_transient").Concat(Named("occt_handle")).ToHashSet();
    var valueClasses = Named("occt_valueclass");
    HashSet<string> noDefault =
      [.. NoDefaultPattern().Matches(text).Select(m => m.Groups[1].Value)];
    HashSet<string> wrapped =
      [.. ValueWrapperPattern().Matches(text).Select(m => m.Groups[1].Value)];
    foreach (var name in Named("occt_valuetype"))
    {
      AddClass(new KnownClass(name, package, WrapKind.ValueType, HeaderOf(name)));
    }

    foreach (Match m in ClassPattern().Matches(text))
    {
      var name = m.Groups[1].Value;
      var kind = transients.Contains(name) ? WrapKind.Transient :
        valueClasses.Contains(name) ? WrapKind.ValueClass : WrapKind.Plain;
      // InterfaceWriter.ClassMacros: both without a default constructor, the value wrapper alone
      // for a move-only class
      AddClass(new KnownClass(name, package, kind, HeaderOf(name),
                              HasDefaultConstructor: !(noDefault.Contains(name)
                                                       && wrapped.Contains(name)),
                              IsMoveOnly: wrapped.Contains(name) && !noDefault.Contains(name)));
    }

    Dictionary<string, string> csBases = CsBasePattern()
      .Matches(text)
      .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
    foreach (Match m in EnumPattern().Matches(text))
    {
      var name = m.Groups[1].Value;
      AddEnum(name, package, HeaderOf(name),
              csBases.TryGetValue(name, out var csBase) ? SignatureMapper.CppBuiltin(csBase) : null,
              nested.GetValueOrDefault(name));
    }

    // the module instantiates these, so they're requested
    foreach (var instantiation in Collections(text, package))
    {
      AddInstantiation(instantiation);
      Request(instantiation);
    }

    // %rename(TopoDS) TopoDS::NetOcc_TopoDS: the static shim for OCCT 8 namespace functions
    foreach (Match m in ShimPattern().Matches(text))
    {
      AddClass(new KnownClass(m.Groups[1].Value, package, WrapKind.Plain));
    }
  }

  /// <summary>
  /// For a run that writes only some packages: the collections a package's current .i instantiates
  /// stay requested, as modules this run doesn't write may use them.
  /// </summary>
  public void KeepRequested(string interfaceFile)
  {
    foreach (var instantiation in Collections(File.ReadAllText(interfaceFile),
                                              Path.GetFileNameWithoutExtension(interfaceFile)))
    {
      if (_instantiations.TryGetValue(instantiation.Spelling, out var known))
      {
        Request(known);
      }
    }
  }

  // %occt_array1(TColgp_Array1OfPnt, gp_Pnt, gp_Pnt): the macro names the template (a suffix a
  // variant), then NAME, the arguments' C++ types, the elements' C# types
  private static IEnumerable<KnownInstantiation> Collections(string text, string package)
  {
    foreach (Match m in CollectionPattern().Matches(text))
    {
      var macro = m.Groups[1].Value;
      if (CollectionTemplate.All.FirstOrDefault(t => macro == t.Macro
                                                     || macro.StartsWith(
                                                       $"{t.Macro}_", StringComparison.Ordinal)) is
          { } template)
      {
        yield return new KnownInstantiation(
          template, [.. SplitArguments(m.Groups[3].Value).Take(template.Arguments.Count)],
          m.Groups[2].Value, package);
      }
    }
  }

  // macro arguments, split at the commas outside template brackets and parentheses; %arg(...),
  // which carries a comma through SWIG's macro call, unwrapped
  internal static IEnumerable<string> SplitArguments(string text)
  {
    var depth = 0;
    var start = 0;
    for (var i = 0; i <= text.Length; i++)
    {
      switch (i < text.Length ? text[i] : ',')
      {
        case '<' or '(':
          depth++;
          break;
        case '>' or ')':
          depth--;
          break;
        case ',' when depth == 0:
          var argument = text[start..i].Trim();
          yield return argument.StartsWith("%arg(", StringComparison.Ordinal)
                       && argument.EndsWith(')')
            ? argument[5..^1].Trim()
            : argument;
          start = i + 1;
          break;
      }
    }
  }

  [GeneratedRegex(@"^%import ""(\w+)\.i""", RegexOptions.Multiline)]
  private static partial Regex ImportPattern();

  [GeneratedRegex(@"^class (\w+)\b", RegexOptions.Multiline)]
  private static partial Regex ClassPattern();

  [GeneratedRegex(@"^class \w+ : public (\w+) \{", RegexOptions.Multiline)]
  private static partial Regex BasePattern();

  // enum X, and a nested one's enum class X
  [GeneratedRegex(@"^enum (?:class )?(\w+)\b", RegexOptions.Multiline)]
  private static partial Regex EnumPattern();

  // %<macro>(NAME, ...): which macros are collections, CollectionTemplate.All tells
  [GeneratedRegex(@"^%(\w+)\((\w+), (.+)\)$", RegexOptions.Multiline)]
  private static partial Regex CollectionPattern();

  [GeneratedRegex(@"^%rename\((\w+)\) (?:\w+::)*NetOcc_\w+;", RegexOptions.Multiline)]
  private static partial Regex ShimPattern();

  // %occt_transient(X), %occt_valueclass(X), ...: the macro and the class it's called for
  [GeneratedRegex(@"^%(occt_\w+)\((\w+)[,)]", RegexOptions.Multiline)]
  private static partial Regex MacroPattern();

  [GeneratedRegex(@"^%nodefaultctor (\w+);", RegexOptions.Multiline)]
  private static partial Regex NoDefaultPattern();

  [GeneratedRegex(@"^%feature\(""valuewrapper""\) (\w+);", RegexOptions.Multiline)]
  private static partial Regex ValueWrapperPattern();

  [GeneratedRegex(@"^%typemap\(csbase\) (\w+) ""(\w+)""", RegexOptions.Multiline)]
  private static partial Regex CsBasePattern();

  // using Geom_Curve_ResD1 = Geom_Curve::ResD1;
  [GeneratedRegex(@"^using (\w+) = (.+);$", RegexOptions.Multiline)]
  private static partial Regex NestedPattern();
}
