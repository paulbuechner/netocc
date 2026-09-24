// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace NetOcc.Generator.Parsing;

/// <summary>
/// The symbols OCCT's Windows DLLs export. OCCT declares a few members Standard_EXPORT and never
/// defines them; the headers can't tell, the export tables can, so those members are skipped
/// instead of failing to link.
/// </summary>
internal sealed class LibraryExports
{
  private readonly HashSet<string> _symbols;

  internal LibraryExports(HashSet<string> symbols, int libraries)
  {
    _symbols = symbols;
    Libraries = libraries;
  }

  public int Libraries { get; }

  public int Count => _symbols.Count;

  public bool Contains(string symbol) => _symbols.Contains(symbol);

  /// <summary>
  /// Exports of the OCCT toolkit DLLs (TK*.dll) in <paramref name="directory"/>; null when it has
  /// none.
  /// </summary>
  public static LibraryExports? Load(string directory)
  {
    if (!Directory.Exists(directory))
    {
      return null;
    }

    HashSet<string> symbols = new(StringComparer.Ordinal);
    var libraries = 0;
    foreach (var file in Directory.EnumerateFiles(directory, "TK*.dll"))
    {
      ReadExportNames(file, symbols);
      libraries++;
    }

    return libraries == 0 ? null : new LibraryExports(symbols, libraries);
  }

  // PE export directory: NumberOfNames at offset 24, AddressOfNames (RVAs of the names) at 32
  private static void ReadExportNames(string path, HashSet<string> into)
  {
    using var stream = File.OpenRead(path);
    using var pe = new PEReader(stream);
    var directory = pe.PEHeaders.PEHeader?.ExportTableDirectory ?? default;
    if (directory.Size == 0)
    {
      return;
    }

    var header = pe.GetSectionData(directory.RelativeVirtualAddress).GetReader();
    header.Offset = 24;
    var count = header.ReadInt32();
    header.Offset = 32;
    var names = pe.GetSectionData(header.ReadInt32()).GetReader();
    for (var i = 0; i < count; i++)
    {
      var name = pe.GetSectionData(names.ReadInt32()).GetReader();
      into.Add(name.ReadUTF8(name.IndexOf(0)));
    }
  }
}
