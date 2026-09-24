// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Text;

namespace NetOcc.Generator.Emit;

/// <summary>
/// Generated C# in the monorepo's C# style (.editorconfig: OCCT's clang-format style), as the
/// formatter lays out the simple shapes the struct writer emits: 2-space indents, and a line past
/// 100 columns wrapped after a comma, the parameters or arguments filling their lines, aligned
/// after the parenthesis.
/// </summary>
internal static class CsLayout
{
  public const int Limit = 100;

  /// <summary>
  /// <c>head(items)tail</c> on one line if it fits, else the items filling lines aligned after the
  /// parenthesis.
  /// </summary>
  public static string Call(string head, IReadOnlyList<string> items, string tail)
  {
    var text = new StringBuilder(head).Append('(');
    var column = text.Length;
    var lineStart = 0;
    for (var i = 0; i < items.Count; i++)
    {
      var item = items[i] + (i == items.Count - 1 ? ")" + tail : ",");
      if (i > 0)
      {
        // the item and the space before it fit, or it goes onto a new line under the first
        if (text.Length - lineStart + 1 + item.Length <= Limit)
        {
          text.Append(' ');
        }
        else
        {
          text.Append('\n');
          lineStart = text.Length;
          text.Append(' ', column);
        }
      }

      text.Append(item);
    }

    return items.Count == 0 ? $"{head}(){tail}" : text.ToString();
  }

  /// <summary>
  /// An expression-bodied member, <c>head(parameters) => call(arguments);</c>: on one line if it
  /// fits, else the call on the next line, one indent deeper, each part laid out as
  /// <see cref="Call"/>.
  /// </summary>
  public static string ExpressionMember(string head, IReadOnlyList<string> parameters, string call,
                                        IReadOnlyList<string> arguments)
  {
    var line =
      $"{head}({string.Join(", ", parameters)}) => {call}({string.Join(", ", arguments)});";
    if (line.Length <= Limit)
    {
      return line;
    }

    var indent = new string(' ', head.Length - head.TrimStart().Length + 2);
    return $"{Call(head, parameters, " =>")}\n{Call(indent + call, arguments, ";")}";
  }
}
