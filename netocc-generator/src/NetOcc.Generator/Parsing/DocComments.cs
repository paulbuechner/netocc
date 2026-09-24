// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

// ClangSharp
using ClangSharp.Interop;

namespace NetOcc.Generator.Parsing;

/// <summary>
/// A Doxygen comment, as libclang parses OCCT's <c>//!</c> lines, in C# XML documentation: the
/// summary (<c>@brief</c> or the first paragraph), parameters, return value, and remarks (further
/// paragraphs, notes, warnings, code). The text is OCCT's: it goes into the XML file the package
/// ships, never into committed files.
/// </summary>
internal static partial class DocComments
{
  /// <summary>The comment's XML documentation, or null when it has no text.</summary>
  public static string? ToXml(CXComment comment)
  {
    if (comment.Kind != CXCommentKind.CXComment_FullComment)
    {
      return null;
    }

    string? brief = null;
    string? returns = null;
    List<string> paragraphs = [];
    List<(string Name, string Text)> parameters = [];
    List<string> remarks = [];
    for (uint i = 0; i < comment.NumChildren; i++)
    {
      var child = comment.GetChild(i);
      switch (child.Kind)
      {
        case CXCommentKind.CXComment_Paragraph when Inline(child) is { Length: > 0 } text:
          paragraphs.Add(text);
          break;
        case CXCommentKind.CXComment_ParamCommand
          when Inline(child.BlockCommandComment_Paragraph) is { Length: > 0 } text:
          parameters.Add((child.ParamCommandComment_ParamName.ToString(), text));
          break;
        case CXCommentKind.CXComment_BlockCommand:
          var body = Inline(child.BlockCommandComment_Paragraph);
          switch (child.BlockCommandComment_CommandName.ToString())
          {
            case "brief" or "short" or "summary":
              brief = body;
              break;
            case "return" or "returns" or "result":
              returns = body;
              break;
            case var command when body.Length > 0:
              remarks.Add(Labels.TryGetValue(command, out var label) ? $"{label}: {body}" : body);
              break;
          }

          break;
        case CXCommentKind.CXComment_VerbatimBlockCommand:
          List<string> lines = [];
          for (uint j = 0; j < child.NumChildren; j++)
          {
            if (child.GetChild(j) is { Kind: CXCommentKind.CXComment_VerbatimBlockLine } line)
            {
              lines.Add(line.VerbatimBlockLineComment_Text.ToString());
            }
          }

          if (lines.Any(l => l.Trim().Length > 0))
          {
            remarks.Add(
              $"<code>{SecurityElement.Escape(string.Join("\n", Dedent(lines)).Trim('\n'))}</code>");
          }

          break;
        case CXCommentKind.CXComment_VerbatimLine
          when child.VerbatimLineComment_Text.ToString().Trim() is { Length: > 0 } text:
          remarks.Add(SecurityElement.Escape(text)!);
          break;
      }
    }

    // the summary: the brief, or the first paragraph; the others are remarks
    var summary = brief ?? paragraphs.FirstOrDefault();
    remarks.InsertRange(0, brief is null ? paragraphs.Skip(1) : paragraphs);
    var xml = new StringBuilder();
    if (summary is { Length: > 0 })
    {
      xml.Append($"<summary>{summary}</summary>");
    }

    foreach (var (name, text) in parameters)
    {
      xml.Append($"<param name=\"{SecurityElement.Escape(name)}\">{text}</param>");
    }

    if (returns is { Length: > 0 })
    {
      xml.Append($"<returns>{returns}</returns>");
    }

    if (remarks.Count > 0)
    {
      xml.Append($"<remarks>{string.Concat(remarks.Select(r => $"<para>{r}</para>"))}</remarks>");
    }

    return xml.Length > 0 ? xml.ToString() : null;
  }

  // how a block command's paragraph reads among the remarks
  private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
  {
    ["note"] = "Note",
    ["warning"] = "Warning",
    ["attention"] = "Attention",
    ["remark"] = "Remark",
    ["remarks"] = "Remark",
    ["deprecated"] = "Deprecated",
    ["sa"] = "See also",
    ["see"] = "See also",
    ["throw"] = "Throws",
    ["throws"] = "Throws",
    ["exception"] = "Throws",
    ["pre"] = "Precondition",
    ["post"] = "Postcondition",
    ["since"] = "Since",
    ["todo"] = "To do",
  };

  // a paragraph's text in one line, XML-escaped: monospaced inline commands (\c, \p) as <c>, others
  // as their words
  private static string Inline(CXComment paragraph)
  {
    if (paragraph.Kind != CXCommentKind.CXComment_Paragraph)
    {
      return "";
    }

    var text = new StringBuilder();
    for (uint i = 0; i < paragraph.NumChildren; i++)
    {
      var child = paragraph.GetChild(i);
      switch (child.Kind)
      {
        case CXCommentKind.CXComment_Text:
          text.Append(SecurityElement.Escape(child.TextComment_Text.ToString())).Append(' ');
          break;
        case CXCommentKind.CXComment_InlineCommand:
          var words = string.Join(" ", Enumerable.Range(0, (int)child.InlineCommandComment_NumArgs)
                                    .Select(a => SecurityElement.Escape(
                                              child.InlineCommandComment_GetArgText((uint)a)
                                                .ToString())));
          text.Append(
              child.InlineCommandComment_RenderKind
              == CXCommentInlineCommandRenderKind.CXCommentInlineCommandRenderKind_Monospaced
                ? $"<c>{words}</c>"
                : words)
            .Append(' ');
          break;
      }
    }

    return Whitespace().Replace(text.ToString(), " ").Trim();
  }

  // code lines without the indentation they share
  private static IEnumerable<string> Dedent(List<string> lines)
  {
    var indent = lines.Where(l => l.Trim().Length > 0)
      .Select(l => l.Length - l.TrimStart().Length)
      .DefaultIfEmpty(0)
      .Min();
    return lines.Select(l => l.Length >= indent ? l[indent..].TrimEnd() : l.Trim());
  }

  [GeneratedRegex(@"\s+")]
  private static partial Regex Whitespace();
}
