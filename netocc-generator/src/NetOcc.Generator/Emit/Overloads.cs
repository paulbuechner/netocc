// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

//
using NetOcc.Generator.Mapping;
using NetOcc.Generator.Model;

// static usings
using static NetOcc.Generator.Emit.MemberRules;

namespace NetOcc.Generator.Emit;

/// <param name="Signature">
/// Name and parameters, defaults included: the member for SWIG features, all its default overloads.
/// </param>
/// <param name="Declaration">
/// Return type, name and parameters, without qualifiers or the trailing ';'.
/// </param>
/// <param name="Arguments">The parameter names, for forwarding calls.</param>
internal sealed record MemberText(string Signature, string Declaration,
                                  IReadOnlyList<string> Arguments);

/// <summary>
/// A member whose parameters map: what <see cref="Overloads.Claim"/> weighs and
/// <see cref="Overloads.Declare"/> writes.
/// </summary>
/// <param name="Parameters">
/// The parameters a declaration keeps (<see cref="MemberRules.WithoutProgressDefaults"/>) with
/// their mappings.
/// </param>
internal sealed record MappedMember(string Label, string Name,
                                    List<(ParameterModel Parameter, MappedType Type)> Parameters)
{
  /// <summary>
  /// The fewest arguments a call takes: SWIG makes a C# overload for each arity from here on.
  /// </summary>
  public int FirstDefault =>
    Parameters.FindIndex(p => p.Parameter.Default is not null) is var index and >= 0
      ? index
      : Parameters.Count;

  /// <summary>
  /// The C# signature of the overload taking the first <paramref name="arity"/> arguments.
  /// </summary>
  public string Key(int arity) =>
    $"{Name}({string.Join(",", Parameters.Take(arity).Select(p => p.Type.CsKey))})";

  /// <summary>
  /// Parameters C# passes as UTF-16 strings: exact, where a <c>const char*</c> overload may copy
  /// bytes as Latin-1.
  /// </summary>
  public int Utf16 =>
    Parameters.Count(p => (p.Parameter.Type is ReferenceType reference
                         ? reference.Referee
                         : p.Parameter.Type) is NamedType { Name: "TCollection_ExtendedString" }
                       or PointerType { Pointee: BuiltinType { Name: "char16_t" } });
}

/// <summary>
/// Overloads that meet in C#: which C# gets (<see cref="Claim"/>), and how a claimed one is
/// declared.
/// </summary>
internal static class Overloads
{
  /// <summary>
  /// The members C# gets, in declaration order, each with the arity its defaults start at.
  /// Overloads that map to one C# signature (const twins, string encodings, a pointer next to a
  /// reference) are one call in C#, which reaches the first claimant: by <paramref name="rank"/>
  /// (<see cref="TwinRank"/>), then the one with more UTF-16 strings, then the first declared. A
  /// member whose full signature is claimed is covered, not logged; one whose shorter default
  /// overloads are claimed keeps its defaults past those (such calls reach the other member).
  /// </summary>
  public static List<(T Item, int FirstDefault)> Claim<T>(List<T> candidates,
                                                          Func<T, MappedMember> member,
                                                          Func<T, int> rank)
  {
    HashSet<string> signatures = [];
    SortedDictionary<int, int> claimed = [];
    foreach (var index in Enumerable.Range(0, candidates.Count)
               .OrderBy(i => rank(candidates[i]))
               .ThenByDescending(i => member(candidates[i]).Utf16))
    {
      var m = member(candidates[index]);
      var count = m.Parameters.Count;
      if (signatures.Contains(m.Key(count)))
      {
        continue;
      }

      var first = m.FirstDefault;
      for (var arity = count - 1; arity >= first; arity--)
      {
        if (signatures.Contains(m.Key(arity)))
        {
          first = arity + 1;
          break;
        }
      }

      for (var arity = first; arity <= count; arity++)
      {
        signatures.Add(m.Key(arity));
      }

      claimed[index] = first;
    }

    return [.. claimed.Select(p => (candidates[p.Key], p.Value))];
  }

  /// <summary>
  /// Which of two twins C# gets first (0 before 1): one returning a C# ref to a value (a number,
  /// enum or struct in the object), which reads as well as writes; not a pointer slot (ref IntPtr,
  /// and a const char*&amp;, which Strings.i returns as one), which reads worse than the value.
  /// Class returns keep declaration order: neither an owned copy nor a borrowed proxy is safe for
  /// every class (a view's copy holds a raw pointer to its graph; an iterator's borrowed item
  /// outlives neither the iterator nor the list).
  /// </summary>
  public static int TwinRank(MappedType returned) =>
    returned.CsType.StartsWith("ref ", StringComparison.Ordinal)
    && returned.CsType != $"ref {SignatureMapper.CsAddress}"
      ? 0
      : 1;

  // a claimed member's declaration, its defaults from firstDefault on; records the types it uses
  public static MemberText Declare(MappedMember member, int firstDefault, string prefix,
                                   ModuleContext module)
  {
    var names = new HashSet<string>();
    var initializers = Initializers([.. member.Parameters.Select(p => p.Parameter)], firstDefault,
                                    member.Label, module.Skipped);
    List<string> arguments = [];
    List<string> declared = [];
    for (var i = 0; i < member.Parameters.Count; i++)
    {
      var (parameter, type) = member.Parameters[i];
      module.Use(type);
      var parameterName = SafeName(parameter.Name, i, names);
      arguments.Add(parameterName);
      declared.Add($"{type.Declare(parameterName)}{initializers[i]}");
    }

    var signature = $"{member.Name}({string.Join(", ", declared)})";
    return new MemberText(signature, prefix + signature, arguments);
  }
}
