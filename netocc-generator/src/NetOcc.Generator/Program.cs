// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;

// YamlDotNet
using YamlDotNet.Core;

//
using NetOcc.Generator;

const string Usage = """
  netocc-gen <command> [options]

  commands:
    bootstrap --occt-src <dir> [--out config/toolkits.yaml]
        toolkit -> package map from OCCT src/MODULES.cmake, TOOLKITS.cmake, PACKAGES.cmake
    dump      --occt-src <dir> --occt-include <dir> [--occt-lib <dir>] [--config config/modules.yaml] <package>
        what the parser sees in one package, parsed as generate parses it (development aid)
    generate  --occt-src <dir> --occt-include <dir> --core <netocc-core> [--occt-lib <dir>] [--config config/modules.yaml] [--check] [names...]
        writes <netocc-core>/src/SWIG_files/{wrapper,headers} for the named packages, toolkits or modules (default: config's generate list) and
        src/SWIG_files/{modules,classes}.json; skipped members go to log/skips/<package>.txt. --occt-lib: OCCT's DLLs
        (default <occt-include>/../../bin), whose exports tell which declared members exist. --check: write nothing,
        exit 1 if netocc-core's files differ from the output. A package that doesn't parse fails the run, which then writes nothing.
    docs      --occt-src <dir> --occt-include <dir> --core <netocc-core> --assembly <NetOcc.dll> --out <NetOcc.xml> [--xml <compiler's xml>] [--config config/modules.yaml]
        OCCT's documentation comments as NetOcc's XML documentation file (IntelliSense), merged with the compiler's
        (--xml, the hand-written members'). Without src/ in --occt-src, the headers come from --occt-include by name.
  """;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
  Console.WriteLine(Usage);
  return 0;
}

var defaultConfig = Path.Combine("config", "modules.yaml");
try
{
  switch (args[0])
  {
    case "bootstrap":
    {
      var line = CommandLine.Parse("bootstrap", args[1..], ["--occt-src", "--out"]);
      return Bootstrap.Run(line.Required("--occt-src"),
                           line.Option("--out") ?? Path.Combine("config", "toolkits.yaml"),
                           Console.Out);
    }

    case "dump":
    {
      var line = CommandLine.Parse("dump", args[1..],
                                   ["--occt-src", "--occt-include", "--occt-lib", "--config"]);
      var package = line.Names is [var name]
        ? name
        : throw new ArgumentException("netocc-gen dump: name one package");
      var include = line.Required("--occt-include");
      return Dump.Run(
        new Workspace(line.Option("--config") ?? defaultConfig, line.Required("--occt-src"),
                      include), include, line.Option("--occt-lib") ?? OcctLibraries(include),
        package, Console.Out);
    }

    case "generate":
    {
      var line = CommandLine.Parse("generate", args[1..], [
        "--occt-src", "--occt-include", "--occt-lib", "--core", "--config"
      ], ["--check"]);
      var include = line.Required("--occt-include");
      return Generate.Run(
        new Workspace(line.Option("--config") ?? defaultConfig, line.Required("--occt-src"),
                      include), include, line.Option("--occt-lib") ?? OcctLibraries(include),
        line.Required("--core"), line.Names, Console.Out, line.Flag("--check"));
    }

    case "docs":
    {
      var line = CommandLine.Parse("docs", args[1..], [
        "--occt-src", "--occt-include", "--core", "--config", "--assembly", "--xml", "--out"
      ]);
      var include = line.Required("--occt-include");
      return Docs.Run(
        new Workspace(line.Option("--config") ?? defaultConfig, line.Required("--occt-src"),
                      include), include, line.Required("--core"), line.Required("--assembly"),
        line.Option("--xml"), line.Required("--out"), Console.Out);
    }

    default:
      Console.Error.WriteLine($"netocc-gen: unknown command '{args[0]}'");
      return 1;
  }
}
catch (Exception e) when (UserErrors(e) is { Count: > 0 } messages)
{
  foreach (var message in messages)
  {
    Console.Error.WriteLine(message);
  }

  return 2;
}

// OCCT's DLLs next to its headers in an install tree: include/opencascade -> bin
static string OcctLibraries(string occtInclude) => Path.Combine(occtInclude, "..", "..", "bin");

// the messages of errors a user can fix (arguments, configuration, input files); none for a bug,
// which keeps its stack trace
static List<string> UserErrors(Exception e) => e switch
{
  AggregateException aggregate when
    aggregate.InnerExceptions.Select(UserErrors).ToList() is var inner
    && inner.All(m => m.Count > 0) => [.. inner.SelectMany(m => m)],
  ArgumentException or InvalidOperationException or IOException or KeyNotFoundException
    or InvalidDataException or YamlException or JsonException or XmlException => [e.Message],
  _ => [],
};
