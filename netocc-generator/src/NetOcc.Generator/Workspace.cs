// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

//
using NetOcc.Generator.Config;
using NetOcc.Generator.Parsing;

namespace NetOcc.Generator;

/// <summary>
/// What generate, docs and dump share: config/modules.yaml, OCCT's source tree as
/// config/toolkits.yaml lays it out, the packages the config names, each package's headers, and the
/// parallel parse.
/// </summary>
internal sealed class Workspace
{
  /// <param name="occtInclude">
  /// OCCT's installed headers, which stand in for a source tree without src/ (see
  /// <see cref="OcctSource"/>).
  /// </param>
  public Workspace(string configPath, string occtSource, string occtInclude)
  {
    ConfigDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
    Config = GeneratorConfig.Load(configPath);
    Source = new OcctSource(occtSource, Path.Combine(ConfigDirectory, "toolkits.yaml"),
                            occtInclude);
    Configured = Expand([.. Config.Generate, .. Config.AliasPackages.Keys]);
  }

  public string ConfigDirectory { get; }

  public GeneratorConfig Config { get; }

  public OcctSource Source { get; }

  /// <summary>The packages the config generates, alias packages last.</summary>
  public List<string> Configured { get; }

  /// <summary>
  /// Config entries (packages, toolkits, modules, alias packages) as packages, each once, in the
  /// order they come.
  /// </summary>
  public List<string> Expand(IEnumerable<string> entries) =>
  [
    .. entries.SelectMany(e => Config.AliasPackages.ContainsKey(e) ? [e] : Source.Expand(e))
      .Where(p => !Config.ExcludePackages.Contains(p))
      .Distinct()
  ];

  /// <summary>
  /// The headers a package's parse includes, and the Windows-only ones left out (generated on
  /// Windows, built everywhere: OSD_WNT.hxx includes windows.h). An alias package has only its
  /// collection aliases.
  /// </summary>
  public (IReadOnlyList<string> Headers, IReadOnlyList<string> WindowsOnly) Headers(string package)
  {
    IReadOnlyList<string> all = Config.AliasPackages.ContainsKey(package)
      ? []
      : Source.Headers(package);
    return ([.. all.Where(h => !IsWindowsOnly(h))], [.. all.Where(IsWindowsOnly)]);
  }

  /// <summary>
  /// <paramref name="parse"/> over the packages in parallel (each parse has its own libclang
  /// index), the results in package order. A package whose parse throws gets its error instead;
  /// progress goes to stderr.
  /// </summary>
  public static (T? Result, string? Error)[] ParseAll<T>(IReadOnlyList<string> packages,
                                                         Func<string, T> parse) where T : class
  {
    var results = new (T? Result, string? Error)[packages.Count];
    var done = 0;
    Parallel.For(0, packages.Count,
                 new ParallelOptions
                   { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) }, i =>
                 {
                   try
                   {
                     results[i] = (parse(packages[i]), null);
                   }
                   catch (Exception e)
                   {
                     // the parse's own errors say what failed; anything else is a bug there, named
                     // by its type
                     results[i] = (null,
                       e is InvalidOperationException or IOException
                         ? e.Message
                         : $"{packages[i]}: {e.GetType().Name}: {e.Message}");
                   }

                   Console.Error.WriteLine(
                     $"parsed {packages[i]} ({Interlocked.Increment(ref done)}/{packages.Count})");
                 });
    return results;
  }

  // OCCT names its Windows-only headers after WNT (Windows NT): OSD_WNT.hxx, the WNT package
  internal static bool IsWindowsOnly(string header) =>
    header.StartsWith("WNT_", StringComparison.Ordinal)
    || header.Contains("_WNT", StringComparison.Ordinal);
}
