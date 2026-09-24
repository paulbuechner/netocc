// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// ClangSharp
using ClangSharp;
using ClangSharp.Interop;

//
using NetOcc.Generator.Model;

namespace NetOcc.Generator.Parsing;

/// <summary>
/// Parses one OCCT package with libclang: a translation unit that includes every header of the
/// package, keeping the public declarations located in those headers.
/// </summary>
/// <param name="exports">
/// OCCT's export tables, to tell which out-of-line members exist; null: trust Standard_EXPORT.
/// </param>
internal sealed partial class PackageParser(IReadOnlyList<string> includeDirectories,
                                            IReadOnlyList<string> extraArguments,
                                            LibraryExports? exports = null)
{
  // a libclang string as a .NET one, the native string freed (ClangSharp's ToString doesn't free
  // it)
  private static string Consume(CXString value)
  {
    using (value)
    {
      return value.ToString();
    }
  }

  /// <summary>
  /// A parameter's name; an unnamed one's by its position, the same for the generator and the docs.
  /// </summary>
  internal static string ParameterName(string name, int index) =>
    string.IsNullOrEmpty(name) ? $"arg{index}" : name;

  // a function's parameter names, as the generator declares them
  private static List<string> ParameterNames(FunctionDecl function) =>
  [
    .. function.Parameters.Select((p, i) => ParameterName(p.Name, i))
  ];

  /// <summary>
  /// OCCT's include directory, plus the one above it, where vcpkg puts OCCT's third-party headers
  /// (rapidjson, ...).
  /// </summary>
  public static IReadOnlyList<string> IncludeDirectories(string occtInclude) =>
    [occtInclude, Path.GetFullPath(Path.Combine(occtInclude, ".."))];

  /// <param name="prelude">
  /// Headers included first, for package headers that don't compile on their own.
  /// </param>
  /// <param name="valueTypes">
  /// Classes that become C# structs: their fields are recorded with the members of class-typed
  /// ones.
  /// </param>
  /// <param name="aliases">
  /// Headers with the package's typedefs only (OCCT 8's deprecated collection aliases): parsed with
  /// it, never included by its module header.
  /// </param>
  /// <param name="classes">
  /// The config's <c>classes</c>, or null for all: the only file-scope classes the parse takes,
  /// with the value types. The others add nothing, neither the instances their members use nor the
  /// names their typedefs give them (OpenGl is its driver: the renderer's classes spell instances
  /// through each other).
  /// </param>
  public PackageModel Parse(string package, IReadOnlyList<string> headers,
                            IReadOnlyList<string>? prelude = null,
                            IReadOnlyCollection<string>? valueTypes = null,
                            IReadOnlyList<string>? aliases = null,
                            IReadOnlyCollection<string>? classes = null)
  {
    var parsed = ParseAll(package, headers, prelude ?? [], valueTypes ?? [], aliases ?? [], [],
                          classes);
    List<string> instantiate = [];
    HashSet<string> failed = [];

    // template instances the headers never instantiate (BRepGraph_FaceIterator; a class template
    // instance a signature uses): again with their classes instantiated, not their members. Those
    // that don't instantiate stay out, and so do all of a round that breaks a header. An
    // instantiated class may use more instances: another round.
    for (var round = 0; round < MaxRounds; round++)
    {
      List<string> attempt =
      [
        .. instantiate,
        .. parsed.Uninstantiated.Where(u => !instantiate.Contains(u) && !failed.Contains(u))
      ];
      Parsed? forced = null;
      // while new candidates are left: the ones that fail go, the rest again
      while (attempt.Count > instantiate.Count)
      {
        forced = ParseAll(package, headers, prelude ?? [], valueTypes ?? [], aliases ?? [], attempt,
                          classes);
        if (forced.Failed.Count == 0)
        {
          break;
        }

        failed.UnionWith(forced.Failed.Select(i => attempt[i]));
        attempt = [.. attempt.Where(a => !failed.Contains(a))];
        forced = null;
      }

      // nothing new instantiates, or the round broke a header: the last round's parse stands
      if (forced is null
          || (forced.Model.ExcludedHeaders?.Count ?? 0)
          != (parsed.Model.ExcludedHeaders?.Count ?? 0))
      {
        break;
      }

      instantiate = attempt;
      parsed = forced;
    }

    // what no round instantiated, and why
    IEnumerable<string> left = parsed.Uninstantiated
      .Where(u => !instantiate.Contains(u) && !failed.Contains(u))
      .Distinct()
      .Order(StringComparer.Ordinal);
    var model = parsed.Model with
    {
      ExcludedTypes =
      [
        .. parsed.Model.ExcludedTypes ?? [],
        .. failed.Order(StringComparer.Ordinal).Select(f => $"{f}: instantiating the class fails"),
        .. left.Select(u =>
                         $"{u}: not instantiated: a round that instantiates it breaks a header, or the {MaxRounds} rounds ran out"),
      ],
    };
    return WithBrokenMembers(model, headers, prelude ?? [], aliases ?? []);
  }

  // rounds of instantiating what the previous ones' classes use: OCCT's deepest chain takes three
  private const int MaxRounds = 8;

  // caps on chains the headers never make this long, against cycles: typedefs of typedefs, and
  // bases of bases or the class-typed fields of class-typed fields
  private const int MaxSugar = 32;
  private const int MaxNesting = 8;

  // the class template instances' members whose bodies don't compile for them marked broken, and
  // the constructors and copies of those whose vtable names a function that doesn't link ruled out:
  // a parse that instantiates every member (an explicit instantiation of each class), each error
  // laid at the member whose body holds it or its note
  private PackageModel WithBrokenMembers(PackageModel model, IReadOnlyList<string> headers,
                                         IReadOnlyList<string> prelude,
                                         IReadOnlyList<string> aliases)
  {
    List<ClassModel> instances = [.. model.Classes.Where(c => c.Instance is not null)];
    if (instances.Count == 0)
    {
      return model;
    }

    var fileName = $"netocc_members_{model.Name}.cpp";
    List<string> included =
      [.. prelude, .. model.Headers, .. aliases.Where(a => !headers.Contains(a))];
    var source = string.Concat(included.Select(header => $"#include <{header}>\n"))
                 + string.Concat(
                   instances.Select(c => $"template class {c.Instance!.Spelling};\n"));
    using var index = CXIndex.Create();
    using var unsaved = CXUnsavedFile.Create(fileName, source);
    var status = CXTranslationUnit.TryParse(index, fileName, Arguments(), [unsaved],
                                            CXTranslationUnit_Flags
                                              .CXTranslationUnit_IncludeAttributedTypes,
                                            out var handle);
    if (status != CXErrorCode.CXError_Success)
    {
      // without the check, a member that doesn't compile for its instance would reach the native
      // build
      throw new InvalidOperationException(
        $"{model.Name}: libclang failed to parse the instances' members ({status})");
    }

    using var unit = TranslationUnit.GetOrCreate(handle);
    // each error with the places it names (itself, its notes) and the instance whose explicit
    // instantiation led there: the note at that line
    List<(int Instance, List<(string File, uint Line)> Places, string Message)> errors = [];
    foreach (var error in Errors(handle))
    {
      var line = error.Places.Where(p => p.File == fileName && p.Line > included.Count)
        .Select(p => (int)p.Line - included.Count - 1)
        .DefaultIfEmpty(-1)
        .First();
      errors.Add((line, error.Places, error.Message));
    }

    // member (instance spelling, name, parameter spellings) -> the first error its body holds for
    // that instance; instance spelling -> the virtual function its vtable names that doesn't link
    Dictionary<(string, string, string), string> broken = [];
    Dictionary<string, string> vtables = [];
    var collector =
      new Collector(handle, new HashSet<string>(model.Headers, StringComparer.OrdinalIgnoreCase),
                    exports, Path.GetFullPath(includeDirectories[0]), []);
    foreach (var instance in
             unit.TranslationUnitDecl.Decls.OfType<ClassTemplateSpecializationDecl>())
    {
      var spelling = instance.TypeForDecl.CanonicalType.AsString;
      var position = instances.FindIndex(c => c.Instance!.Spelling == spelling);
      if (position < 0)
      {
        continue;
      }

      if (exports is not null
          && instance.Definition is CXXRecordDecl definition
          && collector.UnlinkedVtable(definition) is { } unlinked)
      {
        vtables.TryAdd(spelling, unlinked);
      }

      foreach (var member in instance.Methods.Cast<FunctionDecl>().Concat(instance.Ctors))
      {
        var pattern = member.TemplateInstantiationPattern ?? member;
        pattern.Extent.Start.GetFileLocation(out var file, out var start, out _, out _);
        pattern.Extent.End.GetFileLocation(out _, out var end, out _, out _);
        var path = Path.GetFileName(Consume(file.Name));
        if (errors.FirstOrDefault(e => e.Instance == position
                                       && e.Places.Any(p => p.File == path
                                                            && p.Line >= start
                                                            && p.Line <= end)) is
            { Message: { } error })
        {
          broken.TryAdd(
            (spelling, member is CXXConstructorDecl ? "" : member.Name,
              string.Join(",", member.Parameters.Select(p => Convert(p.Type).Spelling))), error);
        }
      }
    }

    string? Broken(ClassModel c, string name, IEnumerable<ParameterModel> parameters) =>
      broken.GetValueOrDefault((c.Instance!.Spelling, name,
                                 string.Join(",", parameters.Select(p => p.Type.Spelling))));

    ConstructorModel Checked(ClassModel c, ConstructorModel k) =>
      Broken(c, "", k.Parameters) is { } error ? k with { IsCallable = false, Broken = error } :
      k.IsCallable && vtables.TryGetValue(c.Instance!.Spelling, out var unlinked) ? k with
      {
        IsCallable = false, Unlinked = unlinked, ThroughVtable = true
      } : k;

    return model with
    {
      Classes =
      [
        .. model.Classes.Select(c => c.Instance is null
                                  ? c
                                  : c with
                                  {
                                    Constructors = [.. c.Constructors.Select(k => Checked(c, k))],
                                    Methods =
                                    [
                                      .. c.Methods.Select(m => Broken(c, m.Name, m.Parameters) is
                                                            { } error
                                                            ? m with
                                                            {
                                                              IsCallable = false, Broken = error
                                                            }
                                                            : m)
                                    ],
                                    // a copy sets the vtable too
                                    Traits = vtables.ContainsKey(c.Instance.Spelling)
                                      ? c.Traits with { IsCopyable = false }
                                      : c.Traits,
                                  })
      ],
    };
  }

  // a diagnostic's file, as libclang names it, and line
  private static (string Path, uint Line) Location(CXDiagnostic diagnostic)
  {
    diagnostic.Location.GetFileLocation(out var file, out var line, out _, out _);
    return (Consume(file.Name), line);
  }

  /// <summary>
  /// An error of a parse (a fatal one too) and where: its file's name and path, then every place
  /// by file name, the error's own first, then its notes' (instantiations).
  /// </summary>
  private sealed record ParseError(string File, string Path, uint Line, string Message,
                                   bool IsFatal, List<(string File, uint Line)> Places);

  // the parse's errors, libclang's diagnostics disposed
  private static List<ParseError> Errors(CXTranslationUnit handle)
  {
    List<ParseError> errors = [];
    for (uint i = 0; i < handle.NumDiagnostics; i++)
    {
      using var diagnostic = handle.GetDiagnostic(i);
      if (diagnostic.Severity < CXDiagnosticSeverity.CXDiagnostic_Error)
      {
        continue;
      }

      var (path, line) = Location(diagnostic);
      var file = Path.GetFileName(path);
      List<(string File, uint Line)> places = [(file, line)];
      var notes = diagnostic.ChildDiagnostics;
      for (uint j = 0; j < notes.NumDiagnostics; j++)
      {
        using var note = notes.GetDiagnostic(j);
        var (notePath, noteLine) = Location(note);
        places.Add((Path.GetFileName(notePath), noteLine));
      }

      errors.Add(new ParseError(file, path, line, Consume(diagnostic.Spelling),
                                diagnostic.Severity == CXDiagnosticSeverity.CXDiagnostic_Fatal,
                                places));
    }

    return errors;
  }

  /// <param name="Uninstantiated">
  /// The class template instances the parse saw no definition of: aliases, or C++ spellings.
  /// </param>
  /// <param name="Failed">
  /// The indexes of the instances the parse was asked to instantiate that had errors.
  /// </param>
  private sealed record Parsed(PackageModel Model, IReadOnlyList<string> Uninstantiated,
                               IReadOnlyList<int> Failed);

  // clang stops at a fatal error (an include that isn't there) and never looks at the headers after
  // it: the header that led there goes, and the package parses again
  private Parsed ParseAll(string package, IReadOnlyList<string> headers,
                          IReadOnlyList<string> prelude, IReadOnlyCollection<string> valueTypes,
                          IReadOnlyList<string> aliases, IReadOnlyList<string> instantiate,
                          IReadOnlyCollection<string>? classes)
  {
    Dictionary<string, string> fatal = new(StringComparer.OrdinalIgnoreCase);
    while (true)
    {
      List<string> remaining = [.. headers.Where(h => !fatal.ContainsKey(h))];
      List<string> remainingAliases = [.. aliases.Where(h => !fatal.ContainsKey(h))];
      if (remaining.Count + remainingAliases.Count == 0 && fatal.Count > 0)
      {
        var (header, reason) = fatal.First();
        throw new InvalidOperationException(
          $"{package}: no header compiles, e.g. {header}: {reason}");
      }

      if (TryParse(package, remaining, remainingAliases, prelude, valueTypes, instantiate, fatal,
                   classes) is { } parsed)
      {
        return parsed;
      }
    }
  }

  // the package, or null after a fatal error, whose header then is in fatal
  /// <param name="instantiate">
  /// Aliases whose classes the parse instantiates, after the headers (a sizeof each).
  /// </param>
  private Parsed? TryParse(string package, IReadOnlyList<string> headers,
                           IReadOnlyList<string> aliases, IReadOnlyList<string> prelude,
                           IReadOnlyCollection<string> valueTypes,
                           IReadOnlyList<string> instantiate, Dictionary<string, string> fatal,
                           IReadOnlyCollection<string>? classes)
  {
    var fileName = $"netocc_parse_{package}.cpp";
    List<string> own = [.. headers, .. aliases];
    List<string> included = [.. prelude, .. own];
    var source = string.Concat(included.Select(header => $"#include <{header}>\n"))
                 + string.Concat(
                   instantiate.Select(alias =>
                                        $"static_assert(sizeof(::{alias}) != 0, \"netocc: instantiate {alias}\");\n"));
    var arguments = Arguments();

    // function bodies too: an inline function that doesn't compile (an OCCT header bug) breaks
    // every wrapper that includes it. The preprocessing record gives the #include graph.
    using var index = CXIndex.Create();
    using var unsaved = CXUnsavedFile.Create(fileName, source);
    var error = CXTranslationUnit.TryParse(index, fileName, arguments, [unsaved],
                                           CXTranslationUnit_Flags
                                             .CXTranslationUnit_IncludeAttributedTypes
                                           | CXTranslationUnit_Flags
                                             .CXTranslationUnit_DetailedPreprocessingRecord,
                                           out var handle);
    if (error != CXErrorCode.CXError_Success)
    {
      throw new InvalidOperationException($"{package}: libclang failed to parse ({error})");
    }

    using var unit = TranslationUnit.GetOrCreate(handle);
    var errors = Errors(handle);
    if (FatalError(package, handle, errors, fileName, included, own) is var (culprit, cause))
    {
      fatal[culprit] = cause;
      return null;
    }

    // before the broken headers: an instantiation's error in its template's header would count that
    // header broken
    var failed = InstantiationFailures(errors, fileName, included.Count, instantiate.Count);
    if (failed.Count > 0)
    {
      return new Parsed(new PackageModel(package, [], [], [], [], []), [], failed);
    }

    var broken = BrokenHeaders(package, handle, errors, fileName, included, own);
    foreach (var (header, reason) in fatal)
    {
      broken[header] = reason;
    }

    List<string> usable = [.. headers.Where(h => !broken.ContainsKey(h))];
    List<string> usableAliases = [.. aliases.Where(h => !broken.ContainsKey(h))];
    if (usable.Count + usableAliases.Count == 0 && broken.Count > 0)
    {
      var (header, reason) = broken.First();
      throw new InvalidOperationException(
        $"{package}: no header compiles, e.g. {header}: {reason}");
    }

    var collector =
      new Collector(
        handle,
        new HashSet<string>([.. usable, .. usableAliases], StringComparer.OrdinalIgnoreCase),
        exports, Path.GetFullPath(includeDirectories[0]), valueTypes, classes);
    collector.Visit(unit.TranslationUnitDecl.Decls, null);
    collector.CollectInstances();
    var model = new PackageModel(package, usable, collector.Enums, collector.Classes,
                                 collector.Typedefs, collector.Functions, [
                                   .. broken.OrderBy(b => b.Key, StringComparer.Ordinal)
                                     .Select(b => $"{b.Key}: {b.Value}")
                                 ], collector.ExcludedTypes);
    return new Parsed(model, collector.Uninstantiated, []);
  }

  // the instances (indexes into the ones to instantiate) whose static_assert led to an error: at
  // its line, or in a header with the instantiation's note at its line
  private static List<int> InstantiationFailures(IReadOnlyList<ParseError> errors, string fileName,
                                                 int includes, int instances) =>
  [
    .. errors.SelectMany(e => e.Places)
      .Where(p => p.File == fileName && p.Line > includes && p.Line - includes - 1 < instances)
      .Select(p => (int)p.Line - includes - 1)
      .Distinct()
      .Order(),
  ];

  private string[] Arguments() =>
  [
    // no error limit: past 20 errors clang stops with a diagnostic that has no file, which hides
    // the broken headers
    "-x", "c++", "-std=c++17", "-Wno-everything", "-ferror-limit=0",
    .. includeDirectories.Select(directory => $"-I{directory}"),
    .. extraArguments,
  ];

  // clang's first fatal error, if any, and the header (in include order) that led there, with the
  // reason. One in a file outside the include directories is the toolchain's, no header's fault:
  // it would take every header down (libclang on Linux lacks its own stddef.h, which the C++
  // library's <cstddef> includes)
  private (string Header, string Reason)? FatalError(string package, CXTranslationUnit handle,
                                                     IReadOnlyList<ParseError> errors,
                                                     string fileName,
                                                     IReadOnlyList<string> included,
                                                     IReadOnlyList<string> headers)
  {
    foreach (var (name, path, line, message, _, _) in errors.Where(e => e.IsFatal))
    {
      if (name == fileName)
      {
        // the #include of a package header that isn't installed
        var header = line <= included.Count
          ? included[(int)line - 1]
          : throw new InvalidOperationException($"{package}: {message}");
        return headers.Contains(header)
          ? (header, message)
          : throw new InvalidOperationException($"{package}: prelude {header}: {message}");
      }

      if (path.Length > 0
          && !includeDirectories.Any(directory => Path.GetFullPath(path)
                                       .StartsWith(Path.GetFullPath(directory),
                                                   StringComparison.OrdinalIgnoreCase)))
      {
        throw new InvalidOperationException(
          $"{package}: {path}: {message} (a system header: libclang's toolchain is incomplete)");
      }

      var graph = IncludeGraph(handle);
      foreach (var header in headers)
      {
        if (string.Equals(header, name, StringComparison.OrdinalIgnoreCase))
        {
          return (header, message);
        }

        if (Reaches(graph, header, name, []))
        {
          return (header, $"includes {name}, which doesn't compile: {message}");
        }
      }

      throw new InvalidOperationException($"{package}: {name}: {message}");
    }

    return null;
  }

  private static bool Reaches(Dictionary<string, HashSet<string>> graph, string from, string target,
                              HashSet<string> seen) =>
    seen.Add(from)
    && (graph.GetValueOrDefault(from) ?? []).Any(i => string.Equals(i, target,
                                                        StringComparison.OrdinalIgnoreCase)
                                                      || Reaches(graph, i, target, seen));

  // the package headers left out, with the reason: a header that doesn't compile (a missing
  // include, a bug in an inline function), that includes such a file (directly or not), or that
  // isn't installed (its #include in the parse's source fails)
  private static Dictionary<string, string> BrokenHeaders(string package, CXTranslationUnit handle,
                                                          IReadOnlyList<ParseError> errors,
                                                          string fileName,
                                                          IReadOnlyList<string> included,
                                                          IReadOnlyList<string> headers)
  {
    Dictionary<string, string> failing = new(StringComparer.OrdinalIgnoreCase);
    foreach (var (file, _, line, message, _, _) in errors)
    {
      if (file.Length == 0)
      {
        throw new InvalidOperationException($"{package}: libclang: {message}");
      }

      // past the includes: an instantiation the parse asked for, no header's fault
      failing.TryAdd(
        file != fileName ? file : line <= included.Count ? included[(int)line - 1] : fileName,
        message);
    }

    Dictionary<string, string> broken = new(StringComparer.OrdinalIgnoreCase);
    if (failing.Count == 0)
    {
      return broken;
    }

    // backwards from the failing files through the files that include them, so cycles in the
    // include graph can't hide one: a file's reason is the first failing file that reaches it, in
    // the order of clang's errors
    Dictionary<string, List<string>> includedBy = new(StringComparer.OrdinalIgnoreCase);
    foreach (var (file, includes) in IncludeGraph(handle))
    {
      foreach (var include in includes)
      {
        (includedBy.TryGetValue(include, out var list) ? list : includedBy[include] = []).Add(file);
      }
    }

    Dictionary<string, string> reasons = new(failing, StringComparer.OrdinalIgnoreCase);
    Queue<string> pending = new(failing.Keys);
    while (pending.TryDequeue(out var file))
    {
      foreach (var includer in includedBy.GetValueOrDefault(file) ?? [])
      {
        if (reasons.TryAdd(
              includer,
              failing.TryGetValue(file, out var message)
                ? $"includes {file}, which doesn't compile: {message}"
                : reasons[file]))
        {
          pending.Enqueue(includer);
        }
      }
    }

    foreach (var header in headers)
    {
      if (reasons.TryGetValue(header, out var reason))
      {
        broken[header] = reason;
      }
    }

    return broken;
  }

  // file -> the files its #include directives name, for every file of the translation unit (by file
  // name)
  private static unsafe Dictionary<string, HashSet<string>> IncludeGraph(CXTranslationUnit handle)
  {
    List<CXFile> files = [];
    var filesHandle = GCHandle.Alloc(files);
    try
    {
      handle.GetInclusions(&CollectFile, new CXClientData(GCHandle.ToIntPtr(filesHandle)));
    }
    finally
    {
      filesHandle.Free();
    }

    Dictionary<string, HashSet<string>> graph = new(StringComparer.OrdinalIgnoreCase);
    foreach (var file in files)
    {
      HashSet<string> includes = new(StringComparer.OrdinalIgnoreCase);
      var includesHandle = GCHandle.Alloc(includes);
      try
      {
        handle.FindIncludesInFile(
          file,
          new CXCursorAndRangeVisitor
            { context = (void*)GCHandle.ToIntPtr(includesHandle), visit = &CollectInclude });
      }
      finally
      {
        includesHandle.Free();
      }

      graph[Path.GetFileName(Consume(file.Name))] = includes;
    }

    return graph;
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe void CollectFile(void* file, CXSourceLocation* stack, uint depth,
                                         void* data) =>
    ((List<CXFile>)GCHandle.FromIntPtr((IntPtr)data).Target!).Add(new CXFile((IntPtr)file));

  [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
  private static unsafe CXVisitorResult CollectInclude(void* context, CXCursor cursor,
                                                       CXSourceRange range)
  {
    ((HashSet<string>)GCHandle.FromIntPtr((IntPtr)context).Target!).Add(
      Path.GetFileName(Consume(cursor.IncludedFile.Name)));
    return CXVisitorResult.CXVisit_Continue;
  }
}
