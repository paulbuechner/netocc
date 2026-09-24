// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;

namespace NetOcc.Tests;

/// <summary>
/// Non-ASCII test text. OCCT reads file paths as UTF-8, while P/Invoke's default <c>char*</c>
/// marshaling is ANSI on Windows: a path with a space and accented letters fails wherever a string
/// crosses without the UTF-8 conversion.
/// </summary>
internal static class NonAscii
{
  public const string Text = "Ünïcödé";

  /// <summary>
  /// <see cref="Text"/> and what Latin-1 lacks: a CJK character (U+8EF8) and one beyond the BMP
  /// (U+20BB7), 3- and 4-byte UTF-8 sequences, and a UTF-16 surrogate pair.
  /// </summary>
  public const string WideText = Text + " 軸 \U00020BB7";

  /// <summary>
  /// A new, empty temp directory <c>netocc Ünïcödé &lt;guid&gt;</c>; the caller deletes it.
  /// </summary>
  public static string CreateTempDirectory()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"netocc {Text} {Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    return directory;
  }
}
