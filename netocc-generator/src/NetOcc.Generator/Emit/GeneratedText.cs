// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

namespace NetOcc.Generator.Emit;

/// <summary>
/// What every generated file shares: the license header, and LF line ends on every OS.
/// </summary>
internal static class GeneratedText
{
  /// <summary>
  /// The SPDX lines of a generated C++, C# or SWIG file, and the blank line after them.
  /// </summary>
  public const string Header =
    "// SPDX-FileCopyrightText: 2026 Paul Büchner\n// SPDX-License-Identifier: MIT\n\n";

  /// <summary>The text with LF line ends: StringBuilder.AppendLine writes the platform's.</summary>
  public static string Lf(string text) => text.Replace("\r\n", "\n");
}
