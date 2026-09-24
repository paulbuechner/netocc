// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NetOcc.Generator.Model;

/// <summary>
/// A C++ type, desugared to what the mapping needs: OCCT typedefs (<c>Standard_Real</c>,
/// <c>occ::handle</c>, deprecated collection aliases) resolve to their target, except the
/// fixed-width and size typedefs, whose canonical form differs per platform.
/// </summary>
internal abstract record CppType
{
  public abstract bool IsConst { get; }

  /// <summary>C++ spelling for SWIG declarations and C++ code.</summary>
  public string Spelling => Declare("");

  /// <summary>
  /// A declaration of <paramref name="name"/> with this type, as C++ writes it: <c>double*
  /// theValues</c>, <c>double theMatrix[3][3]</c>, <c>int (*theFunction)(double)</c>. An empty name
  /// gives the type's spelling.
  /// </summary>
  public abstract string Declare(string name);

  public sealed override string ToString() => Spelling;

  /// <summary>
  /// A named or builtin type without its const (<c>const gp_Pnt</c> is <c>gp_Pnt</c>); any other
  /// type as it is.
  /// </summary>
  public CppType WithoutConst() => this switch
  {
    NamedType n => n with { Const = false },
    BuiltinType b => b with { Const = false },
    _ => this,
  };

  // a declarator after a type name: T* x, T& x and T[3] stay together, a name gets a space
  private protected static string After(string type, string declarator) =>
    declarator.Length == 0 ? type :
    declarator[0] is '*' or '&' or '[' ? type + declarator : $"{type} {declarator}";

  // a pointer or reference to a function or an array needs parentheses: int (*f)(double), double
  // (&a)[3]
  private protected static bool BindsLoosely(CppType type) => type is FunctionType or ArrayType;
}

/// <summary>
/// A builtin (<c>int</c>, <c>double</c>, <c>void</c>, ...) or a width typedef (<c>size_t</c>,
/// <c>int64_t</c>).
/// </summary>
/// <param name="Name">
/// The builtin, or the width typedef's type on every platform (<c>int64_t</c> is <c>long long</c>).
/// </param>
/// <param name="Written">
/// The width typedef as written (<c>int64_t</c>, <c>intptr_t</c>): declarations spell it, since a
/// pointer to it converts to no other type on the platforms where it's something else
/// (<c>int64_t</c> is <c>long</c> on Linux).
/// </param>
internal sealed record BuiltinType(string Name, bool Const = false, string? Written = null)
  : CppType
{
  public override bool IsConst => Const;

  public override string Declare(string name) =>
    After(IsConst ? $"const {Written ?? Name}" : Written ?? Name, name);
}

internal enum NamedKind
{
  Class,
  Enum,
}

/// <summary>A class, struct, enum or class template specialization, by its C++ name.</summary>
/// <param name="DeclaredOnly">
/// C++'s qualified name when the translation unit only declares the class (<c>class
/// TNaming_Node;</c>). Another package may define it; if none does, a pointer to it is an opaque
/// address, and C++ knows it by this name only.
/// </param>
internal sealed partial record NamedType(string Name, NamedKind Kind,
                                         IReadOnlyList<CppType> TemplateArguments,
                                         bool Const = false, string? DeclaredOnly = null) : CppType
{
  public override bool IsConst => Const;

  public override string Declare(string name)
  {
    var type = TemplateArguments.Count == 0
      ? Name
      : $"{Name}<{string.Join(", ", TemplateArguments.Select(a => a.Spelling))}>";
    return After(IsConst ? $"const {type}" : type, name);
  }

  public bool Equals(NamedType? other) =>
    other is not null
    && Name == other.Name
    && Kind == other.Kind
    && Const == other.Const
    && TemplateArguments.SequenceEqual(other.TemplateArguments);

  public override int GetHashCode() => (Name, Kind, Const, TemplateArguments.Count).GetHashCode();

  /// <summary>
  /// The name of a class template instance no alias names: the template and its arguments,
  /// flattened (<c>BRepGraph_MutGuard&lt;BRepGraphInc_VertexDef&gt;</c> is
  /// <c>BRepGraph_MutGuard_BRepGraphInc_VertexDef</c>, a handle argument <c>Handle_T</c>). The
  /// package that owns it declares it as a C++ alias. Past <see cref="MaxInstanceName"/>
  /// characters, the arguments are a hash of the spelling: SWIG writes a file per class.
  /// </summary>
  public string InstanceName
  {
    get
    {
      var flat = NonWord()
        .Replace(string.Join("_", TemplateArguments.Select(Flat).Prepend(Name)), "_")
        .Trim('_');
      return flat.Length <= MaxInstanceName
        ? flat
        : $"{NonWord().Replace(Name, "_")}_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Spelling)))[..12]}";
    }
  }

  /// <summary>The longest <see cref="InstanceName"/> spelled out.</summary>
  public const int MaxInstanceName = 100;

  /// <summary>
  /// The package of a class template instance that names no other package's type (every argument a
  /// number or a constant: <c>NCollection_DynamicArray&lt;int&gt;</c>): its template's, by OCCT's
  /// naming; otherwise null.
  /// </summary>
  public string? TemplatePackage =>
    TemplateArguments.Count > 0
    && TemplateArguments.All(a => a is BuiltinType or ConstantArgument)
    && Name.IndexOf('_') is var end and > 0
      ? Name[..end]
      : null;

  private static string Flat(CppType argument) => argument switch
  {
    NamedType { Name: "opencascade::handle", TemplateArguments: [var target] } =>
      $"Handle_{Flat(target)}",
    NamedType { TemplateArguments.Count: > 0 } n => n.InstanceName,
    NamedType n => n.IsConst ? $"const_{n.Name}" : n.Name,
    BuiltinType b => b.IsConst ? $"const_{b.Name}" : b.Name,
    PointerType p => $"{Flat(p.Pointee)}_ptr",
    _ => argument.Spelling,
  };

  [GeneratedRegex(@"\W+")]
  private static partial Regex NonWord();
}

internal sealed record PointerType(CppType Pointee, bool Const = false) : CppType
{
  public override bool IsConst => Const;

  public override string Declare(string name)
  {
    var pointer = IsConst ? "* const" : "*";
    // a pointer or reference to this one stays together (int**, int*&), a name gets a space (int*
    // x)
    var declarator = name.Length == 0 ? pointer :
      name[0] is '*' or '&' ? pointer + name : $"{pointer} {name}";
    return BindsLoosely(Pointee) ? Pointee.Declare($"({declarator})") : Pointee.Declare(declarator);
  }
}

internal sealed record ReferenceType(CppType Referee, bool IsRValue = false) : CppType
{
  public override bool IsConst => false;

  public override string Declare(string name)
  {
    var reference = IsRValue ? "&&" : "&";
    var declarator = name.Length == 0 ? reference : $"{reference} {name}";
    return Referee.Declare(BindsLoosely(Referee) ? $"({declarator})" : declarator);
  }
}

/// <summary>
/// A C array; <c>double myMat[3][3]</c> is an array of 3 arrays of 3. A parameter declared
/// <c>double theCoeffs[]</c> has length 0 (unknown).
/// </summary>
internal sealed record ArrayType(CppType Element, long Length) : CppType
{
  public override bool IsConst => Element.IsConst;

  public override string Declare(string name) =>
    Element.Declare($"{name}[{(Length > 0 ? Length.ToString(CultureInfo.InvariantCulture) : "")}]");

  /// <summary>The element type below every array level.</summary>
  public CppType Innermost => Element is ArrayType inner ? inner.Innermost : Element;
}

/// <summary>
/// A function type, the pointee of a function pointer: <c>int (*)(double)</c> points to <c>int
/// (double)</c>.
/// </summary>
internal sealed record FunctionType(CppType Return, IReadOnlyList<CppType> Parameters,
                                    bool IsVariadic = false) : CppType
{
  public override bool IsConst => false;

  public override string Declare(string name)
  {
    List<string> parameters = [.. Parameters.Select(p => p.Spelling)];
    if (IsVariadic)
    {
      parameters.Add("...");
    }

    return Return.Declare($"{name}({string.Join(", ", parameters)})");
  }

  public bool Equals(FunctionType? other) =>
    other is not null
    && Return.Equals(other.Return)
    && IsVariadic == other.IsVariadic
    && Parameters.SequenceEqual(other.Parameters);

  public override int GetHashCode() => (Return, Parameters.Count, IsVariadic).GetHashCode();
}

/// <summary>
/// A non-type template argument as C++ spells it: <c>3</c>, <c>true</c>, an enumerator
/// (<c>BRepGraph_NodeId_Kind::Face</c>).
/// </summary>
internal sealed record ConstantArgument(string Text) : CppType
{
  public override bool IsConst => false;

  public override string Declare(string name) => name.Length == 0 ? Text : $"{Text} {name}";
}

/// <summary>
/// Anything the mapping never wraps (dependent types, platform-specific ones); keeps the spelling
/// for the skip log.
/// </summary>
internal sealed record UnsupportedType(string Text) : CppType
{
  public override bool IsConst => false;

  public override string Declare(string name) => name.Length == 0 ? Text : $"{Text} {name}";
}
