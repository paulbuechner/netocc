// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

//
using NetOcc.Generator.Config;
using NetOcc.Generator.Mapping;
using NetOcc.Generator.Model;

namespace NetOcc.Generator.Emit;

/// <summary>
/// Whether and how a member is declared: the rules proxies (InterfaceWriter) and structs
/// (ValueTypeWriter) share.
/// </summary>
internal static class MemberRules
{
  private static readonly HashSet<string> CSharpKeywords =
  [
    "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
    "const", "continue",
    "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
    "false", "finally",
    "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
    "internal", "is", "lock", "long",
    "namespace", "new", "null", "object", "operator", "out", "override", "params", "private",
    "protected", "public",
    "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
    "string", "struct", "switch",
    "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
    "using", "virtual", "void",
    "volatile", "while",
  ];

  /// <summary>
  /// What C# puts on a class or member OCCT deprecates (<c>Standard_DEPRECATED</c>): it is wrapped,
  /// and callers get the compiler warning C++ callers get. Our own text: OCCT's message stays out
  /// of the generated files.
  /// </summary>
  internal const string CsObsolete = "[global::System.Obsolete(\"Deprecated in OCCT.\")]";

  private const string AmbiguousCall =
    "every call is ambiguous in C++: for each number of arguments, another overload takes them and defaults the rest";

  /// <summary>
  /// The parameters a member is declared with (<see cref="Declared"/>), or null. A move twin is
  /// covered: its call reaches the twin. A member is logged when it doesn't compile or link
  /// (<paramref name="notCallable"/>), the config excludes it, its name clashes
  /// (<paramref name="clash"/>), or C++ can make none of its calls; so are calls it leaves out.
  /// </summary>
  /// <param name="twins">
  /// The overloads a moving one's call reaches instead: those of its name (and staticness).
  /// </param>
  /// <param name="overloads">
  /// The overloads C++ picks a call from: those of its name, non-public ones too
  /// (<see cref="Hidden"/>).
  /// </param>
  internal static List<ParameterModel>? Declarable(PackageConfig config, string label,
                                                   IReadOnlyList<ParameterModel> parameters,
                                                   IEnumerable<IReadOnlyList<ParameterModel>> twins,
                                                   IEnumerable<IReadOnlyList<ParameterModel>>
                                                     overloads, string? notCallable,
                                                   List<string> skipped, string? clash = null)
  {
    if (IsMoveTwin(parameters, twins))
    {
      return null;
    }

    var reason = notCallable
                 ?? (config.ExcludeMethods.Contains(label) ? "excluded in the config" : null)
                 ?? clash;
    if (reason is not null)
    {
      skipped.Add($"{label}: {reason}");
      return null;
    }

    var (declared, left) = Declared(parameters, overloads);
    if (declared is null)
    {
      skipped.Add($"{label}: {AmbiguousCall}");
    }
    else if (left.Count > 0)
    {
      skipped.Add(
        $"{label}: its calls with {string.Join(" or ", left)} argument{(left[^1] == 1 ? "" : "s")} are left out: C++ takes them, but "
        + $"not the one with {left[^1] + 1}, and a declaration's defaults run to its end");
    }

    return declared;
  }

  /// <summary>
  /// An overload that moves an argument (<c>T&amp;&amp;</c>) where another takes it by <c>const
  /// T&amp;</c> or by value, the rest the same: C# has no moves, and its call reaches the other
  /// one.
  /// </summary>
  private static bool IsMoveTwin(IReadOnlyList<ParameterModel> parameters,
                                 IEnumerable<IReadOnlyList<ParameterModel>> overloads)
  {
    static string Plain(CppType type) => type.WithoutConst().Spelling;

    static bool Takes(CppType other, CppType type) => type is ReferenceType { IsRValue: true } moved
      ? Plain(other is ReferenceType { IsRValue: false } reference ? reference.Referee : other)
        == Plain(moved.Referee)
        && other is not ReferenceType { IsRValue: true }
      : other.Spelling == type.Spelling;

    // the member itself never matches: it moves where Takes wants another overload's copy
    return parameters.Any(p => p.Type is ReferenceType { IsRValue: true })
           && overloads.Any(o => o.Count == parameters.Count
                                 && o.Zip(parameters)
                                   .All(pair => Takes(pair.First.Type, pair.Second.Type)));
  }

  // why a constructor, method or namespace function can't be called, or null: its body doesn't
  // compile for its class template instance, or it doesn't link (a constructor's inline definition
  // may set a vtable naming what doesn't)
  internal static string? NotCallable(string label, ConstructorModel ctor) =>
    ctor.IsCallable
      ? null
      :
      ctor.ThroughVtable && ctor.Unlinked is null
        ?
        "its inline definition delegates to another constructor and sets the vtable, which MSVC takes from the OCCT libraries, and they don't export it"
        : ctor.ThroughVtable
          ? $"its inline definition sets the vtable, which names {ctor.Unlinked}; the OCCT libraries don't export that"
          : NotCallable(label, ctor.Unlinked, ctor.Broken);

  internal static string? NotCallable(string label, MethodModel method) => method.IsCallable
    ? null
    : NotCallable(label, method.Unlinked, method.Broken);

  internal static string? NotCallable(string label, FunctionModel function) =>
    function.IsCallable ? null : NotLinked(label, function.Unlinked);

  private static string NotCallable(string label, string? unlinked, string? broken) =>
    broken is not null
      ? $"its body doesn't compile for this class template instance ({broken})"
      : NotLinked(label, unlinked);

  // why a member doesn't link: it isn't exported itself, or its inline definition calls something
  // that isn't
  private static string NotLinked(string label, string? unlinked) =>
    unlinked is null
    || label == unlinked
    || label.EndsWith($"::{unlinked}", StringComparison.Ordinal)
      ? "declared, but neither defined in the headers nor exported by the OCCT libraries"
      : $"its inline definition calls {unlinked}, which the OCCT libraries don't export";

  // the parameter lists of a class's non-public overloads of a name
  internal static IEnumerable<IReadOnlyList<ParameterModel>> Hidden(ClassModel c, string name) =>
    (c.Hidden ?? []).Where(h => h.Name == name).Select(h => h.Parameters);

  // the parameter lists of a class's non-public constructors, which the parser names "": an alias
  // may rename the class
  internal static IEnumerable<IReadOnlyList<ParameterModel>> HiddenConstructors(ClassModel c) =>
    Hidden(c, "");

  // the parameters a declaration keeps: trailing Message_ProgressRange defaults go, C# callers
  // don't pass progress (yet)
  internal static List<ParameterModel> WithoutProgressDefaults(
    IReadOnlyList<ParameterModel> parameters)
  {
    var kept = parameters.ToList();
    while (kept.Count > 0 && IsProgressRange(kept[^1].Type) && kept[^1].Default is not null)
    {
      kept.RemoveAt(kept.Count - 1);
    }

    return kept;
  }

  /// <summary>
  /// The parameters a declaration keeps, defaults from the fewest arguments a C++ call of this
  /// member alone takes, or null when no call is. A call SWIG writes (these arguments, or fewer
  /// where they have defaults) is ambiguous when an overload with other parameters takes the same
  /// arguments first and defaults the rest (<c>IntPolyh_Array(int = 256)</c> next to
  /// <c>IntPolyh_Array(int, int = 256)</c>: one argument). Such arities go, as C++ can't make those
  /// calls either; overloads that differ only in const aren't ambiguous. Trailing
  /// <c>Message_ProgressRange</c> defaults are left to C++ where the rest of the call stays
  /// unambiguous (C# callers don't pass progress yet): not next to a <c>Perform()</c>
  /// (<c>BOPAlgo_ParallelAlgo</c>, whose non-public one counts: C++ checks access after overloads).
  /// Defaults run to the declaration's end, so a callable arity below an ambiguous one is left out
  /// (<c>Left</c>).
  /// </summary>
  private static (List<ParameterModel>? Parameters, List<int> Left) Declared(
    IReadOnlyList<ParameterModel> parameters, IEnumerable<IReadOnlyList<ParameterModel>> overloads)
  {
    var candidates = overloads.ToList();
    var spellings = parameters.Select(p => p.Type.Spelling).ToList();

    bool IsUnambiguous(int arity) => candidates
                                       .Where(o => o.Count >= arity
                                                   && o.Take(arity)
                                                     .Select(p => p.Type.Spelling)
                                                     .SequenceEqual(spellings.Take(arity))
                                                   && o.Skip(arity).All(p => p.Default is not null))
                                       .Select(o => string.Join(
                                                 ",", o.Select(p => p.Type.Spelling)))
                                       .Distinct()
                                       .Count()
                                     <= 1;

    var firstDefault = parameters.TakeWhile(p => p.Default is null).Count();
    List<int> callable =
      [.. Enumerable.Range(firstDefault, parameters.Count - firstDefault + 1).Where(IsUnambiguous)];
    if (callable.Count == 0)
    {
      return (null, []);
    }

    var kept = WithoutProgressDefaults(parameters).Count;
    var max = callable.Where(n => n <= kept).DefaultIfEmpty(callable.Min()).Max();
    var min = max;
    while (callable.Contains(min - 1))
    {
      min--;
    }

    return ([.. parameters.Take(max).Select((p, i) => i < min ? p with { Default = null } : p)],
      [.. callable.Where(n => n < min)]);
  }

  /// <summary>
  /// An operator that's no skip: copy and move assignment (C# assignment copies a struct, or the
  /// reference to a proxy), a compound assignment (<c>operator+=</c>) C# derives from the binary
  /// operator the class has, and the allocation functions, which the shim uses when it creates an
  /// object.
  /// </summary>
  internal static bool IsCoveredOperator(string name, ClassModel c) =>
    name == "operator="
    || name.StartsWith("operator new", StringComparison.Ordinal)
    || name.StartsWith("operator delete", StringComparison.Ordinal)
    || (name is "operator+=" or "operator-=" or "operator*=" or "operator/=" or "operator^="
        && (c.Operators ?? []).Any(o => o.Name == name[..^1]
                                        && o.IsConst
                                        && !o.IsStatic
                                        && o.Parameters.Count == 1));

  /// <summary>
  /// The callee may keep a pointer it's given: a constructor (named as its class) or a setter. Such
  /// a pointer can't be a C# array, which is pinned for the call only.
  /// </summary>
  internal static bool MayKeep(string owner, string name) =>
    name == owner || name.StartsWith("Set", StringComparison.Ordinal);

  /// <summary>
  /// A member's parameters with their C# types, or null when C# can't pass one (logged). One C++
  /// may default instead, it and every one after it having defaults, ends the declaration (logged:
  /// C# calls get OCCT's defaults).
  /// </summary>
  /// <param name="mayKeep">
  /// The callee may keep a pointer it's given (<see cref="MayKeep"/>).
  /// </param>
  /// <param name="keptStream">
  /// Why a stream can't be lent to this member, if it can't (it may keep the stream).
  /// </param>
  internal static List<(ParameterModel Parameter, MappedType Type)>? MapParameters(
    SignatureMapper mapper, string label, IReadOnlyList<ParameterModel> parameters, bool mayKeep,
    List<string> skipped, string? keptStream = null)
  {
    List<(ParameterModel Parameter, MappedType Type)> mapped = [];
    for (var i = 0; i < parameters.Count; i++)
    {
      var parameter = parameters[i];
      var result = mapper.Parameter(parameter.Type, mayKeep);
      var reason = result.Type is null ? result.Skip :
        keptStream is not null && result.Type.CsType == SignatureMapper.CsStream ? keptStream :
        null;
      if (reason is null)
      {
        mapped.Add((parameter, result.Type!));
        continue;
      }

      if (parameters.Skip(i).All(p => p.Default is not null))
      {
        skipped.Add($"{label}: parameter {parameter.Name} left to its default: {reason}");
        break;
      }

      skipped.Add($"{label}: parameter {parameter.Name}: {reason}");
      return null;
    }

    return mapped;
  }

  /// <summary>
  /// A member's return in C#, or null: logged, unless a const twin covers the member
  /// (<see cref="HasConstTwin"/>).
  /// </summary>
  /// <param name="twins">The overloads of its name (and staticness), with their returns.</param>
  internal static MappedType? Returned(TypeMapping returned, string label, CppType type,
                                       IReadOnlyList<ParameterModel> parameters,
                                       IEnumerable<(CppType Return, IReadOnlyList<ParameterModel>
                                         Parameters)> twins, List<string> skipped)
  {
    if (returned.Type is null && !HasConstTwin(type, parameters, twins))
    {
      skipped.Add($"{label}: {returned.Skip}");
    }

    return returned.Type;
  }

  /// <summary>
  /// A member returning a mutable reference next to a twin that takes and returns the same types
  /// const (<c>TopoDS::Face(TopoDS_Shape&amp;)</c> beside <c>Face(const TopoDS_Shape&amp;)</c>), or
  /// returns the value (<c>Image_ColorRGB::r()</c> beside <c>r() const</c>): C# reaches the twin,
  /// not a skip.
  /// </summary>
  internal static bool HasConstTwin(CppType returned, IReadOnlyList<ParameterModel> parameters,
                                    IEnumerable<(CppType Return, IReadOnlyList<ParameterModel>
                                      Parameters)> overloads)
  {
    static string Plain(CppType type) =>
      (type is ReferenceType { IsRValue: false } reference ? reference.Referee : type)
      .WithoutConst()
      .Spelling;

    // the member itself never matches: it returns the mutable reference the twin mustn't
    return returned is ReferenceType { IsRValue: false, Referee: { IsConst: false } }
           && overloads.Any(o =>
                              o.Return is ReferenceType { IsRValue: false, Referee.IsConst: true }
                                or not ReferenceType
                              && Plain(o.Return) == Plain(returned)
                              && o.Parameters.Count == parameters.Count
                              && o.Parameters.Zip(parameters)
                                .All(p => Plain(p.First.Type) == Plain(p.Second.Type)));
  }

  /// <summary>
  /// The parameters' defaults as a declaration writes them (<c> = value</c>, or empty), from
  /// <paramref name="firstDefault"/> on. One SWIG can't take is dropped, logged: empty (a macro
  /// that isn't a constant) or braces. So are the defaults before it, which a parameter without one
  /// can't follow.
  /// </summary>
  internal static List<string> Initializers(IReadOnlyList<ParameterModel> parameters,
                                            int firstDefault, string label, List<string> skipped)
  {
    var dropped = -1;
    for (var i = parameters.Count - 1; i >= firstDefault && dropped < 0; i--)
    {
      if (parameters[i].Default is { } value && (value.Length == 0 || value.Contains('{')))
      {
        dropped = i;
      }
    }

    if (dropped >= 0)
    {
      var (name, value) = (parameters[dropped].Name, parameters[dropped].Default!);
      var before = parameters.Take(dropped).Skip(firstDefault).Any(p => p.Default is not null)
        ? ", and the defaults before it"
        : "";
      skipped.Add(
        $"{label}: default of {name} dropped ({(value.Length == 0 ? "from a macro" : value)}){before}");
    }

    var start = Math.Max(firstDefault, dropped + 1);
    return
    [
      .. parameters.Select((p, i) => i >= start && p.Default is { } value ? $" = {value}" : "")
    ];
  }

  private static bool IsProgressRange(CppType type) =>
    type is ReferenceType { Referee: NamedType { Name: "Message_ProgressRange" } }
      or NamedType { Name: "Message_ProgressRange" };

  // a C# parameter name: a keyword gets a trailing _, a name taken already arg<index>
  internal static string SafeName(string name, int index, HashSet<string> taken)
  {
    var safe = CSharpKeywords.Contains(name) ? $"{name}_" : name;
    if (!taken.Add(safe))
    {
      safe = $"arg{index}";
      taken.Add(safe);
    }

    return safe;
  }
}
