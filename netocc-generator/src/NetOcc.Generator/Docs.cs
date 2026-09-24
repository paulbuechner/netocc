// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

// Microsoft.CodeAnalysis.CSharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

//
using NetOcc.Generator.Emit;
using NetOcc.Generator.Parsing;

namespace NetOcc.Generator;

/// <summary>
/// <c>netocc-gen docs</c>: OCCT's documentation comments as NetOcc's XML documentation file, for
/// IntelliSense. Each public member of the compiled assembly gets the comment of the C++
/// declaration it wraps: its type by classes.json (a collection's or instance's is its template's),
/// then name and parameter names, which SWIG keeps. Hand-written members keep the compiler's
/// documentation. The output holds OCCT's text: it ships in the package, never in committed files.
/// </summary>
internal static class Docs
{
  /// <param name="compilerXml">
  /// The documentation file the compiler wrote from the hand-written comments, if any.
  /// </param>
  public static int Run(Workspace workspace, string occtInclude, string core, string assemblyPath,
                        string? compilerXml, string output, TextWriter log)
  {
    if (compilerXml is not null && !File.Exists(compilerXml))
    {
      throw new FileNotFoundException(
        $"netocc-gen docs: no compiler documentation at {compilerXml} (--xml)");
    }

    if (Parse(workspace, occtInclude, log) is not { } docs)
    {
      return 1;
    }

    var index = TypeIndex(Path.Combine(core, "src", "SWIG_files", "classes.json"));
    var existing = compilerXml is null
      ? []
      : XDocument.Load(compilerXml)
        .Descendants("member")
        .Where(m => m.Attribute("name") is not null)
        .ToDictionary(m => m.Attribute("name")!.Value);

    var assembly = Load(assemblyPath);
    var writer = new Writer(docs, index);
    var members = new XElement("members", existing.Values);
    foreach (var type in Types(assembly.GlobalNamespace)
               .Where(t => t.DeclaredAccessibility == Accessibility.Public
                           && t.ContainingNamespace.ToDisplayString()
                             .StartsWith("OCC.Core", StringComparison.Ordinal)))
    {
      foreach (var (id, xml) in writer.Document(type).Where(d => !existing.ContainsKey(d.Id)))
      {
        members.Add(new XElement("member", new XAttribute("name", id),
                                 XElement.Parse($"<doc>{xml}</doc>").Nodes()));
      }
    }

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    var document =
      new XDocument(
        new XElement("doc", new XElement("assembly", new XElement("name", assembly.Name)),
                     members));
    File.WriteAllText(output, document.ToString().ReplaceLineEndings("\n") + "\n",
                      new UTF8Encoding(false));
    log.WriteLine(
      $"docs: {writer.TypeTally.Documented} of {writer.TypeTally.All} types, {writer.MemberTally.Documented} of {writer.MemberTally.All} members, "
      + $"{writer.Missed.Count} unmatched where OCCT documents one of that name -> {Path.GetFullPath(output)}");
    return writer.TypeTally.Documented > 0 ? 0 : 1;
  }

  // the documentation of every configured package, parsed in parallel; a type's first comment wins.
  // A package that doesn't parse would leave its members undocumented: it fails the run, which
  // writes nothing (null), as for generate
  private static Dictionary<string, TypeDocs>? Parse(Workspace workspace, string occtInclude,
                                                     TextWriter log)
  {
    var parser = new PackageParser(PackageParser.IncludeDirectories(occtInclude), []);
    var parsed = Workspace.ParseAll(workspace.Configured,
                                    package =>
                                      parser.ParseDocs(package, workspace.Headers(package).Headers,
                                                       workspace.Config.For(package).Prelude,
                                                       workspace.Source.AliasHeaders(package)));
    List<string> failed = [.. parsed.Select(p => p.Error).OfType<string>()];
    if (failed.Count > 0)
    {
      foreach (var message in failed)
      {
        log.WriteLine($"error: {message}");
      }

      log.WriteLine(
        $"docs: {failed.Count} of {parsed.Length} packages failed to parse; nothing written");
      return null;
    }

    Dictionary<string, TypeDocs> docs = new(StringComparer.Ordinal);
    foreach (var (name, type) in parsed.SelectMany(p => p.Result!))
    {
      docs.TryAdd(name, type);
    }

    return docs;
  }

  // classes.json: each C# type's C++ name, the template for a collection or instance
  private static Dictionary<string, string> TypeIndex(string classesJson)
  {
    Dictionary<string, string> index = new(StringComparer.Ordinal);
    using var json = JsonDocument.Parse(File.ReadAllText(classesJson));
    foreach (var entry in json.RootElement.EnumerateObject()
               .SelectMany(p => p.Value.EnumerateArray()))
    {
      index.TryAdd(entry.GetProperty("name").GetString()!,
                   PackageParser.WithoutTemplateArguments(entry.GetProperty("cpp").GetString()!));
    }

    return index;
  }

  // the assembly's symbols from its metadata, its references the running .NET's
  private static IAssemblySymbol Load(string assemblyPath)
  {
    var platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
      .Split(Path.PathSeparator)
      .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
    var netocc = MetadataReference.CreateFromFile(assemblyPath);
    var compilation = CSharpCompilation.Create("NetOcc.Docs", references: [.. platform, netocc]);
    return compilation.GetAssemblyOrModuleSymbol(netocc) as IAssemblySymbol
           ?? throw new InvalidOperationException($"{assemblyPath}: not an assembly");
  }

  private static IEnumerable<INamedTypeSymbol> Types(INamespaceSymbol scope) =>
    scope.GetTypeMembers().Concat(scope.GetNamespaceMembers().SelectMany(Types));

  /// <summary>
  /// The documentation of C# types, found by their C++ names; counts what it documents as it goes.
  /// </summary>
  private sealed class Writer(Dictionary<string, TypeDocs> docs, Dictionary<string, string> index)
  {
    public (int All, int Documented) TypeTally;

    public (int All, int Documented) MemberTally;

    /// <summary>
    /// Members OCCT documents one of that name for, without a match: the matching's misses.
    /// </summary>
    public List<string> Missed { get; } = [];

    /// <summary>The type's and its members' documentation: doc comment IDs and XML.</summary>
    public IEnumerable<(string Id, string Xml)> Document(INamedTypeSymbol type)
    {
      TypeTally.All++;
      // an alias's own comment first (TColgp_Array1OfPnt), then its template's
      if ((docs.GetValueOrDefault(type.Name)?.Xml ?? DocsOf(type)?.Xml) is { } typeXml
          && type.GetDocumentationCommentId() is { } typeId)
      {
        TypeTally.Documented++;
        yield return (typeId, typeXml);
      }

      foreach (var member in type.GetMembers()
                 .Where(m => m.DeclaredAccessibility == Accessibility.Public
                             && !m.IsImplicitlyDeclared))
      {
        (string Name, IReadOnlyList<string> Parameters)? key = member switch
        {
          IMethodSymbol { MethodKind: MethodKind.Constructor } constructor => ("#ctor",
            [.. constructor.Parameters.Select(p => p.Name)]),
          IMethodSymbol { MethodKind: MethodKind.Ordinary } method => (method.Name,
            [.. method.Parameters.Select(p => p.Name)]),
          IFieldSymbol field => (field.Name, []),
          _ => null,
        };
        if (key is not { } found || member.GetDocumentationCommentId() is not { } id)
        {
          continue;
        }

        MemberTally.All++;
        if (Find(type, found.Name, found.Parameters) is { } xml)
        {
          MemberTally.Documented++;
          yield return (id, xml);
        }
        else if (DocsOf(type)?.Members.Any(m => m.Name == found.Name) == true)
        {
          Missed.Add(id);
        }
      }
    }

    // the C++ type the C# one wraps
    private TypeDocs? DocsOf(INamedTypeSymbol type) =>
      docs.GetValueOrDefault(index.GetValueOrDefault(type.Name) ?? type.Name);

    // the member's documentation in its class or, for one a class redeclares without a comment (an
    // override), a base's
    private string? Find(INamedTypeSymbol type, string name, IReadOnlyList<string> parameters)
    {
      for (var t = type; t is not null; t = t.BaseType)
      {
        if (Match(DocsOf(t), name, parameters) is { } xml)
        {
          return xml;
        }
      }

      return null;
    }
  }

  /// <summary>
  /// The documentation of the C++ member a C# one wraps: of its name, with the C# parameter names
  /// first among its own (made safe for C# as the generator does; SWIG keeps them), the shortest
  /// such, as default arguments make shorter C# overloads. Otherwise the one documentation the
  /// members of its name that take that many arguments share (a hand-written struct member).
  /// </summary>
  internal static string? Match(TypeDocs? type, string name, IReadOnlyList<string> parameters)
  {
    if (type is null)
    {
      return null;
    }

    List<MemberDocs> candidates =
      [.. type.Members.Where(m => m.Name == name && m.Parameters.Count >= parameters.Count)];
    if (candidates
          .Where(m => SafeNames(m.Parameters).Take(parameters.Count).SequenceEqual(parameters))
          .MinBy(m => m.Parameters.Count) is { } named)
    {
      return named.Xml;
    }

    // overloads OCCT documents alike (a const and a mutable one) count as one
    List<string> counted =
    [
      .. candidates.Where(m => m.Parameters.Count == parameters.Count).Select(m => m.Xml).Distinct()
    ];
    return counted is [var xml] ? xml :
      candidates.Select(m => m.Xml).Distinct().ToList() is [var only] ? only : null;
  }

  private static List<string> SafeNames(IReadOnlyList<string> names)
  {
    HashSet<string> taken = [];
    return [.. names.Select((name, i) => MemberRules.SafeName(name, i, taken))];
  }
}
