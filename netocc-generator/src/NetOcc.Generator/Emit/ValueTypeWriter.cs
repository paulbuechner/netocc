// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

//
using NetOcc.Generator.Config;
using NetOcc.Generator.Mapping;
using NetOcc.Generator.Model;

// static usings
using static NetOcc.Generator.Emit.MemberRules;

namespace NetOcc.Generator.Emit;

/// <summary>
/// A value type's generated parts: its C# struct (layout and native members) and the thunks those
/// call.
/// </summary>
/// <param name="Code">netocc-core <c>src/NetOcc/&lt;Pkg&gt;/&lt;Type&gt;.g.cs</c>.</param>
/// <param name="Thunks">C++ functions for the package's <c>%inline</c> block.</param>
internal sealed record GeneratedStruct(string Name, string Code, string Thunks);

/// <summary>
/// Writes a value type (<c>value_types</c>) or plain data as a C# struct with the C++ layout (plain
/// data: public fields). Every member a hand-written partial of the struct doesn't declare calls a
/// native thunk (<c>NetOcc_&lt;Type&gt;_&lt;Member&gt;</c>) with <c>in this</c> (const) or <c>ref
/// this</c>, a reference in C++; constructors assign <c>theSelf = T(...)</c>, operators apply the
/// C++ operator.
/// </summary>
internal sealed class ValueTypeWriter(TypeRegistry registry, SignatureMapper mapper,
                                      string occtVersion)
{
  // C# operators from C++ member operators; C# derives the compound ones (+=, ...) from these
  private static readonly Dictionary<string, (string Token, string Name)> BinaryOperators = new()
  {
    ["operator+"] = ("+", "Addition"),
    ["operator-"] = ("-", "Subtraction"),
    ["operator*"] = ("*", "Multiply"),
    ["operator/"] = ("/", "Division"),
    ["operator^"] = ("^", "ExclusiveOr"),
  };

  private static readonly Dictionary<string, (string Token, string Name)> UnaryOperators = new()
  {
    ["operator-"] = ("-", "UnaryNegation"),
  };

  // members every C# struct has
  private static readonly HashSet<string> ObjectMembers =
    ["Equals", "GetHashCode", "ToString", "GetType"];

  /// <param name="module">
  /// The module the struct's thunks go into: what they use, and what the struct leaves out.
  /// </param>
  public GeneratedStruct Write(ClassModel type, ModuleContext module, HandWritten handWritten)
  {
    var context = new StructContext(module, type, handWritten);
    // the fields as C++ has them: private in the configured value types (whose partials make the
    // API), public in plain data
    foreach (var field in type.Fields ?? [])
    {
      foreach (var (csType, name, offset) in Flatten(field, field.Name, field.Offset, context))
      {
        context.Fields.AppendLine(
          $"  [FieldOffset({offset})] {(field.IsPublic ? "public" : "private")} {csType} {name};");
      }
    }

    Constructors(context);
    Methods(context);
    Operators(context);
    return new GeneratedStruct(type.Name, Code(context), context.Thunks.ToString());
  }

  private sealed class StructContext(ModuleContext module, ClassModel type, HandWritten handWritten)
  {
    public string Package => module.Package;
    public ClassModel Type { get; } = type;
    public PackageConfig Config => module.Config;
    public HandWritten HandWritten { get; } = handWritten;
    public List<string> Skipped => module.Skipped;
    public string Module => $"{Package}Module";
    public StringBuilder Fields { get; } = new();
    public StringBuilder Members { get; } = new();
    public StringBuilder Thunks { get; } = new();

    /// <summary>Packages whose C# namespaces the struct needs.</summary>
    public HashSet<string> Packages { get; } = [];

    /// <summary>
    /// Generated C# signatures, normalized like <see cref="Emit.HandWritten"/>: no two members may
    /// share one.
    /// </summary>
    public HashSet<string> Signatures { get; } = [];

    public void Use(TypeUse use)
    {
      module.Uses.Add(use);
      Packages.Add(use.Package);
    }

    /// <summary>Records what a type in a thunk needs: the types it uses, its typemaps.</summary>
    public void Use(MappedType type)
    {
      module.Use(type);
      Packages.UnionWith(type.Uses.Select(u => u.Package));
    }
  }

  // the struct fields for a C++ field: builtins, enums and value types as they are; arrays element
  // by element (name_0, name_1, ...) and fields of other classes member by member (name_member)
  private IEnumerable<(string CsType, string Name, long Offset)> Flatten(
    FieldModel field, string name, long offset, StructContext context)
  {
    switch (field.Type)
    {
      // C# bool isn't blittable (marshaled as 4 bytes)
      case BuiltinType { Name: "bool" }:
        yield return ("byte", name, offset);
        break;
      case BuiltinType builtin when SignatureMapper.CsBuiltin(builtin.Name) is { } cs:
        yield return (cs, name, offset);
        break;
      case NamedType { Kind: NamedKind.Enum } e when registry.Enum(e.Name) is { } known:
        context.Use(known.Use);
        yield return (e.Name, name, offset);
        break;
      case NamedType { TemplateArguments.Count: 0 } n when registry.Class(n.Name) is
        { Kind: WrapKind.ValueType } known:
        context.Use(known.Use);
        yield return (n.Name, name, offset);
        break;
      case ArrayType array:
        var elementSize = field.Size / array.Length;
        for (var i = 0; i < array.Length; i++)
        {
          foreach (var element in Flatten(field with { Type = array.Element, Size = elementSize },
                                          $"{name}_{i}", offset + i * elementSize, context))
          {
            yield return element;
          }
        }

        break;
      case NamedType when field.Members is { Count: > 0 } members:
        foreach (var member in members)
        {
          foreach (var inner in Flatten(member, $"{name}_{member.Name}", offset + member.Offset,
                                        context))
          {
            yield return inner;
          }
        }

        break;
      default:
        throw new InvalidOperationException(
          $"value type {context.Type.Name}: field {field.Name} ({field.Type.Spelling}) can't be part of a C# struct");
    }
  }

  private void Constructors(StructContext context)
  {
    var type = context.Type;
    var label = $"{type.Name}::{type.Name}";
    var overloads = type.Constructors.Select(o => o.Parameters).ToList();
    for (var i = 0; i < type.Constructors.Count; i++)
    {
      var ctor = type.Constructors[i];
      if (Declarable(context.Config, label, ctor.Parameters, overloads,
                     overloads.Concat(HiddenConstructors(type)), NotCallable(label, ctor),
                     context.Skipped) is not { } declared
          || Map(context, label, declared, mayKeep: true) is not { } signature)
      {
        continue;
      }

      var thunk = $"NetOcc_{type.Name}_New" + (type.Constructors.Count > 1 ? $"_{i}" : "");
      var emitted = false;
      foreach (var parameters in signature.Overloads())
      {
        if (!Claim(context, ".ctor", parameters))
        {
          continue;
        }

        emitted = true;
        Obsolete(context, ctor.IsDeprecated);
        context.Members
          .AppendLine(CsLayout.Call($"  public {type.Name}", CsParameters(parameters), ""))
          .AppendLine("  {")
          .AppendLine("    this = default;")
          .AppendLine(CsLayout.Call($"    {context.Module}.{thunk}",
                                    Arguments("ref this", parameters), ";"))
          .AppendLine("  }")
          .AppendLine();
      }

      if (emitted)
      {
        Use(context, signature);
        context.Thunks.AppendLine(
          $"inline void {thunk}({Join($"{type.Name}& theSelf", signature.Declaration)}) {{ theSelf = {type.Name}({signature.Call}); }}");
      }
    }
  }

  private void Methods(StructContext context)
  {
    var type = context.Type;
    foreach (var group in type.Methods.GroupBy(m => m.Name))
    {
      var overloads = group.ToList();
      for (var i = 0; i < overloads.Count; i++)
      {
        var m = overloads[i];
        var label = $"{type.Name}::{m.Name}";
        var twins = overloads.Where(o => o.IsStatic == m.IsStatic).ToList();
        var clash = m.Name == type.Name || ObjectMembers.Contains(m.Name)
          ? "name clashes with the C# struct"
          : null;
        if (Declarable(context.Config, label, m.Parameters, twins.Select(o => o.Parameters),
                       overloads.Select(o => o.Parameters).Concat(Hidden(type, m.Name)),
                       NotCallable(label, m), context.Skipped, clash: clash) is not { } declared)
        {
          continue;
        }

        // returning its own struct for chaining: void, as for proxies
        var returned =
          !m.IsStatic && SignatureMapper.IsChaining(registry.Resolve(m.Return), type.Name)
            ? mapper.Return(new BuiltinType("void"))
            : mapper.Return(m.Return);
        if (Returned(returned, label, m.Return, m.Parameters,
                     twins.Select(o => (o.Return, o.Parameters)), context.Skipped) is not
            { } returnType)
        {
          continue;
        }

        // this is a C# struct, pinned for the call only: the GC may move it right after
        if (!m.IsStatic && m.Return is PointerType or ReferenceType { Referee: PointerType })
        {
          context.Skipped.Add(
            $"{label}: returns a pointer into the struct, which the GC may move after the call");
          continue;
        }

        if (Map(context, label, declared, MayKeep(type.Name, m.Name)) is not { } signature)
        {
          continue;
        }

        var thunk = $"NetOcc_{type.Name}_{m.Name}" + (overloads.Count > 1 ? $"_{i}" : "");
        var (self, selfArgument, modifier) = m.IsStatic ? ("", "", "static ") :
          m.IsConst ? ($"const {type.Name}& theSelf", "in this", "readonly ") :
          ($"{type.Name}& theSelf", "ref this", "");
        var emitted = false;
        foreach (var parameters in signature.Overloads())
        {
          if (!Claim(context, m.Name, parameters))
          {
            continue;
          }

          emitted = true;
          AppendMember(context, m.IsDeprecated,
                       CsLayout.ExpressionMember($"  public {modifier}{returnType.CsType} {m.Name}",
                                                 CsParameters(parameters),
                                                 $"{context.Module}.{thunk}",
                                                 Arguments(selfArgument, parameters)));
        }

        if (emitted)
        {
          Use(context, signature, returnType);
          var call = m.IsStatic
            ? $"{type.Name}::{m.Name}({signature.Call})"
            : $"theSelf.{m.Name}({signature.Call})";
          context.Thunks.AppendLine(
            $"inline {returnType.Spelling} {thunk}({Join(self, signature.Declaration)}) "
            + $"{{ {(returnType.Spelling == "void" ? "" : "return ")}{call}; }}");
        }
      }
    }
  }

  private void Operators(StructContext context)
  {
    var type = context.Type;
    foreach (var group in (type.Operators ?? []).Where(o => !IsCoveredOperator(o.Name, type))
             .GroupBy(o => o.Name))
    {
      var overloads = group.ToList();
      for (var i = 0; i < overloads.Count; i++)
      {
        var o = overloads[i];
        var label = $"{type.Name}::{o.Name}";
        var table =
          o.Parameters.Count switch { 1 => BinaryOperators, 0 => UnaryOperators, _ => null };
        if (o.IsStatic || !o.IsConst || table is null || !table.TryGetValue(o.Name, out var op))
        {
          context.Skipped.Add(
            $"{label}: C# structs get the const operators +, -, *, / and ^ (and unary -), which take their operands by value");
          continue;
        }

        var twins = overloads.Where(t => t.Parameters.Count == o.Parameters.Count)
          .Select(t => t.Parameters);
        if (Declarable(context.Config, label, o.Parameters, twins,
                       overloads.Select(t => t.Parameters), NotCallable(label, o),
                       context.Skipped) is null)
        {
          continue;
        }

        var returned = mapper.Return(o.Return);
        if (returned.Type is null || returned.Type.Spelling == "void")
        {
          context.Skipped.Add($"{label}: {returned.Skip ?? "returns void"}");
          continue;
        }

        if (Map(context, label, o.Parameters) is not { } signature)
        {
          continue;
        }

        // C# operators take their operands by value or in
        var parameters = signature.Parameters;
        if (parameters.Any(p => p.CsType.StartsWith("ref ", StringComparison.Ordinal)))
        {
          context.Skipped.Add($"{label}: C# operators can't take ref parameters");
          continue;
        }

        List<CsParameter> operands = [new("theSelf", $"in {type.Name}"), .. parameters];
        if (!Claim(context, $"operator{op.Token}", operands))
        {
          continue;
        }

        Use(context, signature, returned.Type);
        var thunk = $"NetOcc_{type.Name}_op_{op.Name}" + (overloads.Count > 1 ? $"_{i}" : "");
        var expression = parameters.Count == 1
          ? $"theSelf {op.Token} {signature.Call}"
          : $"{op.Token}theSelf";
        context.Thunks.AppendLine(
          $"inline {returned.Type.Spelling} {thunk}({Join($"const {type.Name}& theSelf", signature.Declaration)}) {{ return {expression}; }}");
        AppendMember(context, o.IsDeprecated,
                     CsLayout.ExpressionMember(
                       $"  public static {returned.Type.CsType} operator {op.Token}",
                       CsParameters(operands), $"{context.Module}.{thunk}",
                       Arguments("", operands)));
      }
    }
  }

  // a member OCCT deprecates is [Obsolete] in C#
  private static void Obsolete(StructContext context, bool deprecated)
  {
    if (deprecated)
    {
      context.Members.AppendLine($"  {CsObsolete}");
    }
  }

  // an expression-bodied member: one-liners together, one over several lines set off by blank
  // lines, as the formatter keeps them
  private static void AppendMember(StructContext context, bool deprecated, string member)
  {
    var members = context.Members;
    var multiline = member.Contains('\n');
    var blank = Environment.NewLine + Environment.NewLine;
    if (multiline
        && members.Length > 0
        && (members.Length < blank.Length
            || members.ToString(members.Length - blank.Length, blank.Length) != blank))
    {
      members.AppendLine();
    }

    Obsolete(context, deprecated);
    members.AppendLine(member);
    if (multiline)
    {
      members.AppendLine();
    }
  }

  // the member is the struct's to generate: the partial doesn't declare it, and no other generated
  // member has its C# signature. Neither is a skip: the partial covers it, or a C# call reaches the
  // other member.
  private static bool Claim(StructContext context, string name,
                            IReadOnlyList<CsParameter> parameters)
  {
    List<string> types = [.. parameters.Select(p => p.CsType)];
    return !context.HandWritten.Declares(name, types)
           && context.Signatures.Add(HandWritten.Signature(name, types));
  }

  private sealed record CsParameter(string Name, string CsType);

  /// <param name="Declaration">The thunk's parameters, C++ (defaults included).</param>
  /// <param name="Call">The arguments the thunk passes on.</param>
  /// <param name="FirstOptional">
  /// From this parameter on, each has a default: SWIG writes one overload per arity.
  /// </param>
  /// <param name="Types">
  /// The parameters' mappings: what the member uses, recorded once it's emitted
  /// (<see cref="Use"/>).
  /// </param>
  private sealed record MemberSignature(List<CsParameter> Parameters, string Declaration,
                                        string Call, int FirstOptional,
                                        IReadOnlyList<MappedType> Types)
  {
    public IEnumerable<List<CsParameter>> Overloads() =>
      Enumerable.Range(FirstOptional, Parameters.Count - FirstOptional + 1)
        .Select(n => Parameters.Take(n).ToList());
  }

  // an emitted member's types: its module imports and includes them (a member the partial covers
  // adds nothing)
  private static void Use(StructContext context, MemberSignature signature,
                          MappedType? returned = null)
  {
    foreach (var type in signature.Types)
    {
      context.Use(type);
    }

    if (returned is not null)
    {
      context.Use(returned);
    }
  }

  // the parameters, mapped, or null (the reason logged). mayKeep: the callee may keep a pointer
  // (MayKeep).
  private MemberSignature? Map(StructContext context, string label,
                               IReadOnlyList<ParameterModel> parameters, bool mayKeep = false)
  {
    if (MapParameters(mapper, label, parameters, mayKeep, context.Skipped) is not { } mapped)
    {
      return null;
    }

    HashSet<string> taken = ["theSelf"];
    var initializers =
      Initializers([.. mapped.Select(m => m.Parameter)], 0, label, context.Skipped);
    List<CsParameter> cs = [];
    List<string> declared = [];
    for (var i = 0; i < mapped.Count; i++)
    {
      var (parameter, type) = mapped[i];
      var name = SafeName(parameter.Name, i, taken);
      cs.Add(new CsParameter(name, type.CsType));
      declared.Add($"{type.Declare(name)}{initializers[i]}");
    }

    var firstOptional = initializers.Count;
    while (firstOptional > 0 && initializers[firstOptional - 1].Length > 0)
    {
      firstOptional--;
    }

    return new MemberSignature(cs, string.Join(", ", declared),
                               string.Join(", ", cs.Select(p => p.Name)), firstOptional,
                               [.. mapped.Select(m => m.Type)]);
  }

  private static List<string> CsParameters(IEnumerable<CsParameter> parameters) =>
    [.. parameters.Select(p => $"{p.CsType} {p.Name}")];

  // "in x" and "ref x" pass on their modifier; self first, if any
  private static List<string> Arguments(string self, IEnumerable<CsParameter> parameters) =>
  [
    .. self.Length == 0 ? [] : (string[])[self],
    .. parameters.Select(p => p.CsType.StartsWith("in ", StringComparison.Ordinal)
                           ?
                           $"in {p.Name}"
                           : p.CsType.StartsWith("ref ", StringComparison.Ordinal)
                             ? $"ref {p.Name}"
                             : p.Name),
  ];

  private static string Join(string first, string rest) =>
    first.Length == 0 ? rest : rest.Length == 0 ? first : $"{first}, {rest}";

  private string Code(StructContext context)
  {
    var type = context.Type;
    var layout = type.Layout
                 ?? throw new InvalidOperationException(
                   $"{context.Package}: no layout for value type {type.Name}");
    var text = new StringBuilder(GeneratedText.Header)
      .AppendLine(
        $"// {type.Name}: generated by netocc-gen from OCCT {occtVersion}. Don't edit; change the generator or its config.")
      .AppendLine(
        $"// Managed members go into {type.Name}.cs, a partial of this struct; the generator leaves those out.")
      .AppendLine()
      .AppendLine("using System.Runtime.InteropServices;")
      .AppendLine()
      .AppendLine($"namespace OCC.Core.{context.Package};")
      .AppendLine();
    // inside the namespace, as SWIG has them: an imported type beats a sibling namespace of its
    // name (the class BRepGraph, not the namespace OCC.Core.BRepGraph, seen from
    // OCC.Core.BRepGraphInc)
    var others = context.Packages.Where(p => p != context.Package)
      .Order(StringComparer.Ordinal)
      .ToList();
    foreach (var package in others)
    {
      text.AppendLine($"using OCC.Core.{package};");
    }

    if (others.Count > 0)
    {
      text.AppendLine();
    }

    if (type.IsDeprecated)
    {
      text.AppendLine(CsObsolete);
    }

    text.AppendLine($"[StructLayout(LayoutKind.Explicit, Size = {layout.Size})]")
      .AppendLine($"public partial struct {type.Name}")
      .AppendLine("{")
      .Append(context.Fields);
    var members = context.Members.ToString().TrimEnd();
    if (members.Length > 0)
    {
      text.AppendLine().AppendLine(members);
    }

    return GeneratedText.Lf(text.AppendLine("}").ToString());
  }
}
