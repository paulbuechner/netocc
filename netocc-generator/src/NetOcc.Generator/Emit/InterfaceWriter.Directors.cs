// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

//
using NetOcc.Generator.Mapping;
using NetOcc.Generator.Model;

// static usings
using static NetOcc.Generator.Emit.MemberRules;
using static NetOcc.Generator.Emit.Overloads;

namespace NetOcc.Generator.Emit;

// the part of InterfaceWriter that declares director classes, whose C# subclasses OCCT calls back
// (SWIG directors, netocc-core's Directors.i), and marks the classes around them
internal sealed partial class InterfaceWriter
{
  // the type information every transient declares again (DEFINE_STANDARD_RTTIEXT): C++'s, never a
  // C# override
  private const string TypeInformation = "DynamicType";

  // a virtual member C# may override: the class's own or a base's, public or protected, pure where
  // its nearest declaration is
  private sealed record VirtualMember(MethodModel Model, string Declarer, bool IsPublic);

  /// <summary>A class C# may subclass: its package config lists it in <c>directors</c>.</summary>
  private bool IsDirector(ClassModel c) => directors.Contains(c.Name);

  /// <summary>
  /// A director class or one of its bases: a handle of it may be a C# object, which C# gets back as
  /// itself.
  /// </summary>
  private bool IsDirected(string name) => Directed.Contains(name);

  private HashSet<string> Directed => _directed ??=
  [
    .. directors, .. directors.SelectMany(d => classOf?.Invoke(d)?.Ancestors ?? []),
  ];

  private HashSet<string>? _directed;

  /// <summary>
  /// A concrete class deriving from a director class, or a concrete director class: SWIG counts the
  /// pure virtual members the director declares against it, since the .i leaves out the protected
  /// overrides of classes that aren't directors, and would drop its constructors.
  /// </summary>
  private bool IsConcreteDirected(ClassModel c) =>
    !c.Traits.IsAbstract && (IsDirector(c) || c.Ancestors.Any(directors.Contains));

  /// <summary>
  /// The virtual members of a director class C# may override, the nearest declaration of each
  /// signature: the class's own, then its bases' up to <c>Standard_Transient</c>, whose type
  /// information and reference count stay C++'s. A private override hides the base's member; a
  /// base the run didn't parse adds none.
  /// </summary>
  private List<VirtualMember> VirtualMembers(ClassModel c)
  {
    List<VirtualMember> members = [];
    HashSet<string> seen = [];
    foreach (var owner in c.Ancestors.Prepend(c.Name).TakeWhile(a => a != RootTransient))
    {
      if ((owner == c.Name ? c : classOf?.Invoke(owner)) is not { } model)
      {
        continue;
      }

      var own = model.Methods.ToHashSet();
      foreach (var m in model.Methods.Concat(model.Hidden ?? []))
      {
        if (!m.IsVirtual
            || m.IsStatic
            || m.Name.Length == 0
            || m.Name == TypeInformation
            || !seen.Add(Signature(m)))
        {
          continue;
        }

        var isPublic = own.Contains(m);
        if (isPublic || m.IsProtected)
        {
          members.Add(new VirtualMember(m, owner, isPublic));
        }
      }
    }

    return members;
  }

  // how C++ tells overrides apart: name, parameter types without their top-level const, constness
  private static string Signature(MethodModel m) =>
    $"{m.Name}({string.Join(",", m.Parameters.Select(p => p.Type.WithoutConst().Spelling))}){(m.IsConst ? " const" : "")}";

  /// <summary>
  /// Why OCCT can't call a C# override of <paramref name="m"/>, declared with
  /// <paramref name="declared"/> parameters, or null. Directors.i converts numbers, enums, handles,
  /// structs, classes and strings both ways; the C# override must take every parameter, as the
  /// C++ override does; and SWIG runs code after a callback only for a parameter or a result,
  /// where a C# exception is rethrown in C++.
  /// </summary>
  private string? NotOverridable(MethodModel m, int declared)
  {
    if (declared < m.Parameters.Count)
    {
      return "C# can't take all its parameters";
    }

    var returned = registry.Resolve(m.Return);
    if (m.Parameters.Count == 0 && returned is BuiltinType { Name: "void" })
    {
      return
        "SWIG runs no code after a callback without parameters or result, where a C# exception would be rethrown";
    }

    foreach (var parameter in m.Parameters)
    {
      if (DirectorParameter(registry.Resolve(parameter.Type)) is { } reason)
      {
        return $"parameter {parameter.Name}: {reason}";
      }
    }

    return DirectorReturn(returned) is { } result ? $"its result: {result}" : null;
  }

  // numbers Directors.i converts: those of SWIG's C# director typemaps, not the width typedefs
  // (declared by their names, which the typemaps don't know), C long or size_t
  private static readonly HashSet<string> DirectorNumbers =
  [
    "bool", "char", "signed char", "unsigned char", "short", "unsigned short", "int",
    "unsigned int", "long long", "unsigned long long", "float", "double",
  ];

  private static bool IsDirectorNumber(CppType type) =>
    type is BuiltinType { Written: null } b && DirectorNumbers.Contains(b.Name);

  private string? DirectorParameter(CppType type) => type switch
  {
    _ when IsDirectorNumber(type) => null,
    NamedType { Kind: NamedKind.Enum } => null,
    NamedType n when IsHandle(n) => null,
    NamedType n when registry.Class(n.Name) is
      { Kind: WrapKind.ValueType or WrapKind.ValueClass } => null,
    ReferenceType { IsRValue: false, Referee: var referee } when IsDirectorNumber(referee) => null,
    ReferenceType { IsRValue: false, Referee: NamedType n } when IsHandle(n) => n.IsConst
      ? null
      : "a handle by non-const reference",
    ReferenceType { IsRValue: false, Referee: NamedType { Kind: NamedKind.Enum } n } => n.IsConst
      ? null
      : "an enum by non-const reference",
    ReferenceType
    {
      IsRValue: false,
      Referee: NamedType { Name: "TCollection_AsciiString" or "TCollection_ExtendedString" } s
    } => s.IsConst ? null : "a string by non-const reference",
    ReferenceType { IsRValue: false, Referee: NamedType { Kind: NamedKind.Class } n } when
      registry.Class(n.Name) is { Kind: not WrapKind.Transient } => null,
    PointerType { Pointee: BuiltinType { Name: "char", IsConst: true } } => null,
    _ => $"{type.Spelling} doesn't reach a C# override",
  };

  // what a C# override may give back: by value, as references would point into C# objects
  private string? DirectorReturn(CppType type) => type switch
  {
    BuiltinType { Name: "void" } => null,
    _ when IsDirectorNumber(type) => null,
    NamedType { Kind: NamedKind.Enum } => null,
    NamedType n when IsHandle(n) => null,
    NamedType n when registry.Class(n.Name) is
      { Kind: WrapKind.ValueType or WrapKind.ValueClass } => null,
    _ => $"{type.Spelling} can't come back from a C# override",
  };

  private static bool IsHandle(NamedType n) => n is { Name: "opencascade::handle" };

  /// <summary>
  /// The inherited virtual members a director class declares again, and its protected ones:
  /// SWIG overrides the members the class declares (with <c>dirprot</c>, protected ones too).
  /// Its own public ones <see cref="Methods"/> declares. A member stays out where a nearer class
  /// declares another of its name, which hides it in C++; an override C# can't make is logged
  /// (C# still calls a public one on the base).
  /// </summary>
  private void DirectorMembers(StringBuilder body, ClassModel c, ModuleContext module)
  {
    var members = VirtualMembers(c);
    var own = c.Methods.ToHashSet();
    // the names each class declares, nearest first: a nearer one hides a farther one's overloads
    var names = c.Ancestors.Prepend(c.Name)
      .Select(a => (Class: a,
                Names: ((a == c.Name ? c : classOf?.Invoke(a))?.Methods ?? [])
                .Concat((a == c.Name ? c : classOf?.Invoke(a))?.Hidden ?? [])
                .Select(m => m.Name)
                .ToHashSet()))
      .ToList();

    bool IsHidden(VirtualMember member) =>
      names.TakeWhile(n => n.Class != member.Declarer)
        .Any(n => n.Names.Contains(member.Model.Name));

    List<string> declaredKeys = [];
    StringBuilder publicPart = new(), protectedPart = new();
    foreach (var member in members.Where(m => !own.Contains(m.Model)))
    {
      var label = $"{c.Name}::{member.Model.Name}";
      if (IsHidden(member))
      {
        RequireOverridable(c, member.Model, $"{c.Name} hides it");
        module.Skipped.Add(
          $"{label} ({member.Declarer}'s): C# can't override it: {c.Name} hides it with another {member.Model.Name}");
        continue;
      }

      if (VirtualDeclaration(c, member, module, declaredKeys) is { } declaration)
      {
        (member.IsPublic ? publicPart : protectedPart).AppendLine($"  {declaration};");
      }
    }

    body.Append(publicPart);
    // SWIG wants a director's destructor virtual (Warning 514): Standard_Transient's is
    body.AppendLine($"  virtual ~{c.Name}();");
    var constructors = ProtectedConstructors(c);
    if (constructors.Count == 0 && protectedPart.Length == 0)
    {
      return;
    }

    body.AppendLine("protected:");
    Constructors(body, c, module, constructors, isDirector: true);
    body.Append(protectedPart);
  }

  // a director's protected constructors, which C# subclasses call (the parser names them "")
  private static List<ConstructorModel> ProtectedConstructors(ClassModel c) =>
  [
    .. (c.Hidden ?? []).Where(h => h is { Name: "", IsProtected: true })
    .Select(h => new ConstructorModel(h.Parameters, h.IsDeprecated, h.IsCallable, h.Unlinked,
                                      Broken: h.Broken)),
  ];

  /// <summary>
  /// A director's own public virtual member, which <see cref="Methods"/> declares: whether C# may
  /// override it, logged where not.
  /// </summary>
  private bool Overridable(ClassModel c, MethodModel m, MappedMember member, ModuleContext module)
  {
    if (m.Name == TypeInformation)
    {
      return false;
    }

    if (NotOverridable(m, member.Parameters.Count) is not { } reason)
    {
      return true;
    }

    RequireOverridable(c, m, reason);
    module.Skipped.Add($"{c.Name}::{m.Name}: C# can't override it: {reason}");
    return false;
  }

  // a pure virtual member C# can't override leaves the C++ subclass SWIG writes abstract
  private static void RequireOverridable(ClassModel c, MethodModel m, string reason)
  {
    if (m.IsPure)
    {
      throw new InvalidOperationException(
        $"{c.Name} can't be a director: C# can't override its pure virtual {m.Name} ({reason})");
    }
  }

  /// <summary>
  /// A virtual member's declaration in a director class (<c>virtual</c>, <c>= 0</c> where pure),
  /// or null when it isn't one: C# can't map it (logged by the checks), or can't override it
  /// (logged here). <paramref name="declaredKeys"/> are the C# signatures declared so far.
  /// </summary>
  private string? VirtualDeclaration(ClassModel c, VirtualMember virtualMember,
                                     ModuleContext module, List<string> declaredKeys)
  {
    var m = virtualMember.Model;
    var label = $"{c.Name}::{m.Name}";
    // the overloads C++ picks a call from, in the class that declares the member
    var declarer = virtualMember.Declarer == c.Name ? c : classOf?.Invoke(virtualMember.Declarer);
    List<IReadOnlyList<ParameterModel>> overloads =
    [
      .. (declarer?.Methods ?? []).Concat(declarer?.Hidden ?? [])
      .Where(o => o.Name == m.Name && o.IsStatic == m.IsStatic)
      .Select(o => o.Parameters),
    ];
    if (Declarable(module.Config, label, m.Parameters, overloads, overloads, NotCallable(label, m),
                   module.Skipped) is not { } declared)
    {
      RequireOverridable(c, m, "C++ can't call it as declared");
      return null;
    }

    var returned = mapper.Return(m.Return, m.Parameters, c.Name);
    if (Returned(returned, label, m.Return, m.Parameters, [], module.Skipped) is not { } returnType
        || Map(c.Name, m.Name, declared, module) is not { } member)
    {
      RequireOverridable(c, m, "C# can't map its signature");
      return null;
    }

    if (NotOverridable(m, member.Parameters.Count) is { } reason)
    {
      RequireOverridable(c, m, reason);
      module.Skipped.Add($"{label}: C# can't override it: {reason}");
      return null;
    }

    var key = member.Key(member.Parameters.Count);
    if (declaredKeys.Contains(key))
    {
      module.Skipped.Add($"{label}: C# can't override it: another member has its C# signature");
      return null;
    }

    declaredKeys.Add(key);
    module.Use(returnType);
    var text = Declare(member, member.FirstDefault, returnType.Spelling + " ", module);
    return $"virtual {text.Declaration}{(m.IsConst ? " const" : "")}{(m.IsPure ? " = 0" : "")}";
  }
}
