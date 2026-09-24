// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Microsoft.CodeAnalysis.CSharp
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetOcc.Generator.Emit;

/// <summary>
/// The members a hand-written partial of a value-type struct declares (netocc-core
/// <c>src/NetOcc/&lt;Pkg&gt;/&lt;Type&gt;.cs</c>): managed math the generator doesn't emit. Members
/// match by name and parameter types; <c>in</c> doesn't count, since C# can't tell <c>f(in T)</c>
/// from <c>f(T)</c> at a call.
/// </summary>
internal sealed class HandWritten
{
  private readonly HashSet<string> _signatures;

  private HandWritten(HashSet<string> signatures) => _signatures = signatures;

  public static HandWritten None { get; } = new([]);

  public static HandWritten Load(string path)
  {
    if (!File.Exists(path))
    {
      return None;
    }

    var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
    HashSet<string> signatures = [];
    foreach (var type in root.DescendantNodes()
               .OfType<TypeDeclarationSyntax>()
               .Where(t => t.ParameterList is not null))
    {
      // a primary constructor
      signatures.Add(
        Signature(".ctor", type.ParameterList!.Parameters.Select(p => $"{p.Modifiers} {p.Type}")));
    }

    foreach (var member in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
    {
      var name = member switch
      {
        MethodDeclarationSyntax m => m.Identifier.Text,
        ConstructorDeclarationSyntax => ".ctor",
        OperatorDeclarationSyntax o => $"operator{o.OperatorToken.Text}",
        _ => null,
      };
      if (name is not null)
      {
        signatures.Add(
          Signature(name, member.ParameterList.Parameters.Select(p => $"{p.Modifiers} {p.Type}")));
      }
    }

    return new HandWritten(signatures);
  }

  /// <summary>
  /// Whether the partial declares the member: <c>.ctor</c>, <c>operator+</c> or a method name, with
  /// C# parameter types.
  /// </summary>
  public bool Declares(string name, IEnumerable<string> parameterTypes) =>
    _signatures.Contains(Signature(name, parameterTypes));

  /// <summary>
  /// A member as C# calls tell members apart: <c>in gp_Pnt</c> and <c>gp_Pnt</c> are one, <c>ref
  /// double</c> stays; name(type,type) without spaces.
  /// </summary>
  internal static string Signature(string name, IEnumerable<string> parameterTypes) =>
    $"{name}({string.Join(",", parameterTypes.Select(Normalize))})";

  private static string Normalize(string parameterType)
  {
    var text = parameterType.Trim();
    if (text.StartsWith("in ", StringComparison.Ordinal))
    {
      text = text[3..];
    }

    return string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
  }
}
