// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;

namespace NetOcc.Generator.Tests;

/// <summary>A new directory under the temp path, deleted with its contents on dispose.</summary>
internal sealed class TempDirectory(string prefix) : IDisposable
{
  public string FullName { get; } = Directory
    .CreateDirectory(Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}"))
    .FullName;

  /// <summary>A path below the directory.</summary>
  public string Combine(params string[] parts) => Path.Combine([FullName, .. parts]);

  /// <summary>A directory below the directory, created with its parents.</summary>
  public string CreateSubdirectory(params string[] parts) =>
    Directory.CreateDirectory(Combine(parts)).FullName;

  /// <summary>Writes a file below the directory, creating its parents.</summary>
  public string Write(string relative, string text)
  {
    var path = Combine(relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, text);
    return path;
  }

  public void Dispose() => Directory.Delete(FullName, recursive: true);
}
