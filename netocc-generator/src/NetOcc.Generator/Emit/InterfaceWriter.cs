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
using static NetOcc.Generator.Emit.ClassRules;
using static NetOcc.Generator.Emit.MemberRules;

namespace NetOcc.Generator.Emit;

/// <summary>
/// Writes one package's SWIG interface in netocc-core's conventions (declarations only,
/// deterministic).
/// </summary>
/// <param name="preludeOf">
/// A package's configured prelude: headers its own headers need first.
/// </param>
/// <param name="handWrittenOf">
/// The hand-written partial of a value-type struct (package, type), whose members aren't generated.
/// </param>
/// <param name="classOf">
/// Any parsed class by name: the bases of an argument's class in other packages
/// (<see cref="IsKept"/>), what a returned class refers to (<see cref="View"/>), the virtual
/// members a director class inherits.
/// </param>
/// <param name="directorClasses">
/// The classes C# may subclass, every package's <c>directors</c>: SWIG directors, and the bases
/// and derived classes around them.
/// </param>
internal sealed partial class InterfaceWriter(TypeRegistry registry, SignatureMapper mapper,
                                              string occtVersion,
                                              Func<string, IReadOnlyList<string>>? preludeOf = null,
                                              Func<string, string, HandWritten>? handWrittenOf =
                                                null, Func<string, ClassModel?>? classOf = null,
                                              IReadOnlyCollection<string>? directorClasses = null)
{
  private readonly ValueTypeWriter _structs = new(registry, mapper, occtVersion);

  private readonly HashSet<string> directors = [.. directorClasses ?? []];

  // members every SWIG C# proxy already has. Not GetType: OCCT's (GeomAdaptor_Curve::GetType) hides
  // object's, which isn't virtual, and the proxy's own code never calls it
  private static readonly HashSet<string> ProxyMembers = ["Dispose", "Finalize", "MemberwiseClone"];

  // the root of the handle hierarchy: handle typemaps, but no base class and no DownCast
  private const string RootTransient = "Standard_Transient";

  // the attribute as a SWIG string, for %csattributes and the csattributes typemap
  internal static readonly string SwigObsolete = $"\"{CsObsolete.Replace("\"", "\\\"")}\"";

  /// <param name="hasExtras">
  /// netocc-core has a hand-written companion, <c>src/SWIG_files/extras/&lt;Pkg&gt;.i</c>, for what
  /// the generator can't derive (lifetime typemaps, value-type thunks, %extend). It is included
  /// after the enums, before the classes.
  /// </param>
  /// <param name="basesOf">
  /// The other packages a package's classes derive from, which orders the imports.
  /// </param>
  public GeneratedModule Write(PackageModel package, PackageConfig config,
                               Func<string, IReadOnlyList<string>> importsOf,
                               bool hasExtras = false,
                               Func<string, IReadOnlyCollection<string>>? basesOf = null)
  {
    var own = package.Name;
    var module = new ModuleContext(own, config);
    module.Skipped.AddRange([
      .. (package.ExcludedHeaders ?? []).Select(h => $"header {h}"), .. package.ExcludedTypes ?? []
    ]);
    var uses = module.Uses;
    var valueTypes = ValueTypes(package, config);
    var classes = Classes(package, config);
    var macros = ClassMacros(valueTypes, classes);
    var enums = EnumDeclarations(package);

    var body = new StringBuilder();
    foreach (var c in classes)
    {
      WriteClass(body, c, module);
    }

    // value types: C# structs whose native members call thunks, declared after the classes their
    // signatures may use
    List<GeneratedStruct> structs =
    [
      .. valueTypes.Select(v => _structs.Write(v, module,
                                               handWrittenOf?.Invoke(own, v.Name)
                                               ?? HandWritten.None))
    ];
    var thunks = string.Concat(structs.Select(s => s.Thunks));
    if (thunks.Length > 0)
    {
      body.AppendLine($"// the native members of the C# structs (src/NetOcc/{own}/*.g.cs)")
        .AppendLine("%inline %{")
        .Append(thunks)
        .AppendLine("%}")
        .AppendLine();
    }

    var functions = NamespaceFunctions(package, module);
    // what the members use, before the package's own collections add their elements and bases
    List<KnownInstantiation> used =
    [
      .. uses.Select(u => u.Collection)
        .OfType<KnownInstantiation>()
        .Distinct()
        .OrderBy(i => i.Spelling, StringComparer.Ordinal)
    ];
    var collections = Collections(own, module);

    var direct = uses.Select(u => u.Package).ToHashSet();
    // handle-managed classes and collections: Standard_Transient's typemaps (DownCast takes a
    // Handle(Standard_Transient))
    if (classes.Any(c => Kind(c) == WrapKind.Transient)
        || registry.RequestedIn(own).Any(i => i.Template.IsTransient))
    {
      direct.Add("Standard");
    }

    var imports = ImportOrder.Closure(own, direct, importsOf, basesOf);

    var text = new StringBuilder();
    text.Append(GeneratedText.Header)
      .AppendLine(
        $"/* {own}: generated by netocc-gen from OCCT {occtVersion}. Don't edit; change the generator or its config. */")
      // a module with director classes: SWIG's director support, protected members included
      .AppendLine(classes.Any(IsDirector)
                    ? $"%module(directors=\"1\", dirprot=\"1\") {own}Module"
                    : $"%module {own}Module")
      .AppendLine()
      .AppendLine("%include \"NetOcc.i\"")
      .AppendLine()
      .AppendLine("%{")
      .AppendLine($"#include <{own}_module.hxx>")
      .AppendLine("%}")
      .AppendLine();
    // one flat list: a module SWIG imports doesn't import again, so SWIG meets the packages in this
    // order, the bases of a class before it
    if (imports.Count > 0)
    {
      text.AppendLine("#ifndef SWIGIMPORTED");
      foreach (var import in imports)
      {
        text.AppendLine($"%import \"{import}.i\"");
      }

      text.AppendLine("#endif");
    }

    // C# usings: the namespaces of the imported modules (after the imports, whose own calls this
    // one overrides)
    text.AppendLine(
        $"%netocc_csimports({string.Join(" ", imports.Select(i => $"using OCC.Core.{i};"))})")
      .AppendLine();
    // the macros its declarations need (address typemaps, range-for enumerators, constructor
    // keeps): before every declaration that uses them
    foreach (var typemap in module.Typemaps)
    {
      macros.AppendLine(typemap);
    }

    if (macros.Length > 0)
    {
      text.Append(macros).AppendLine();
    }

    if (valueTypes.Count > 0)
    {
      text.Append(LayoutGuards(own, valueTypes)).AppendLine();
    }

    text.Append(enums);
    if (hasExtras)
    {
      text.AppendLine($"%include \"extras/{own}.i\"").AppendLine();
    }

    text.Append(collections).Append(body).Append(functions);
    // the preludes of the packages it uses too: their headers miss the same includes here
    List<string> preludes =
    [
      .. config.Prelude,
      .. uses.Select(u => u.Package)
        .Where(p => p != own)
        .Distinct()
        .Order(StringComparer.Ordinal)
        .SelectMany(p => preludeOf?.Invoke(p) ?? [])
    ];
    var nested = ModuleHeaders.Nested(package);
    var header = ModuleHeaders.Module(package, preludes,
                                      uses.Where(u => u.Package != own).Select(u => u.Header),
                                      nested is not null);
    return new GeneratedModule(GeneratedText.Lf(text.ToString().TrimEnd()) + "\n",
                               GeneratedText.Lf(header), imports, module.Skipped, structs, used,
                               module.BasePackages, module.Namespaces,
                               nested is null ? null : GeneratedText.Lf(nested));
  }

  private WrapKind Kind(ClassModel c) => registry.Class(c.Name)?.Kind ?? WrapKind.Plain;

  // the typemap macros and features of the package's structs and classes, which go before every
  // declaration
  private StringBuilder ClassMacros(List<ClassModel> valueTypes, List<ClassModel> classes)
  {
    var macros = new StringBuilder();
    foreach (var v in valueTypes)
    {
      macros.AppendLine($"%occt_valuetype({v.Name})");
    }

    foreach (var c in classes)
    {
      var kind = Kind(c);
      if (kind == WrapKind.Transient)
      {
        macros.AppendLine(c.Name == RootTransient
                            ? $"%occt_handle({c.Name}, {c.Name})"
                            : $"%occt_transient({c.Name})");
        // a director class or one of its bases: a handle of it may be a C# subclass's object,
        // which C# gets back as itself and keeps alive while OCCT holds it (Directors.i)
        if (IsDirected(c.Name))
        {
          macros.AppendLine($"%netocc_directed({c.Name})");
        }

        if (IsDirector(c))
        {
          macros.AppendLine($"%netocc_director({c.Name})");
        }
      }
      // a director its proxy owns: its callbacks run while C# calls it
      else if (IsDirector(c))
      {
        macros.AppendLine($"%netocc_director_owned({c.Name})");
      }

      if (IsConcreteDirected(c))
      {
        macros.AppendLine($"%feature(\"notabstract\") {c.Name};");
      }
      // a const& return is a copy, whatever the class declares: a static member may return one
      // without an object (Graphic3d_CubeMapOrder::Default returns a
      // Graphic3d_ValidatedCubeMapOrder, which C# only gets that way)
      else if (kind == WrapKind.ValueClass)
      {
        macros.AppendLine($"%occt_valueclass({c.Name})");
      }

      if (c.IsDeprecated)
      {
        macros.AppendLine($"%typemap(csattributes) {c.Name} {SwigObsolete}");
      }

      if (!c.Traits.HasPublicDestructor)
      {
        macros.AppendLine($"%nodefaultdtor {c.Name};");
      }

      // SWIG gives a class that declares no constructor a default one, and default-constructs
      // by-value results; without a usable T(), it must know (the value wrapper copies results
      // instead)
      if (!c.Traits.HasDefaultConstructor)
      {
        macros.AppendLine($"%nodefaultctor {c.Name};")
          .AppendLine($"%feature(\"valuewrapper\") {c.Name};");
      }
      // a move-only class's by-value results go through the value wrapper, which moves them
      else if (IsMoveOnly(c))
      {
        macros.AppendLine($"%feature(\"valuewrapper\") {c.Name};");
      }
      else if (c.Traits.IsAbstract || !c.Traits.IsCreatable)
      {
        macros.AppendLine($"%nodefaultctor {c.Name};");
      }
    }

    return macros;
  }

  private static StringBuilder EnumDeclarations(PackageModel package)
  {
    var enums = new StringBuilder();
    foreach (var e in package.Enums)
    {
      // the C# enum has C++'s underlying type (structs hold it)
      if (e.Underlying is { } underlying && SignatureMapper.CsBuiltin(underlying) is { } csBase)
      {
        enums.AppendLine($"%typemap(csbase) {e.Name} \"{csBase}\"");
      }

      // a nested enum's constants are only unique in its scope: scoped in the .i, where SWIG sees
      // all of them
      enums.AppendLine($"enum {(e.QualifiedName is null ? "" : "class ")}{e.Name} {{");
      foreach (var constant in e.Constants)
      {
        enums.AppendLine($"  {constant.Name} = {constant.Value},");
      }

      enums.AppendLine("};").AppendLine();
    }

    return enums;
  }

  // the package's proxied classes, bases before derived classes
  private List<ClassModel> Classes(PackageModel package, PackageConfig config)
  {
    List<ClassModel> selected =
    [
      .. package.Classes.Where(c => IsSelected(c, config)
                                    && !IsCovered(c)
                                    && Kind(c) != WrapKind.ValueType)
    ];

    List<ClassModel> ordered = [];
    HashSet<string> placed = [];

    void Place(ClassModel c)
    {
      if (!placed.Add(c.Name))
      {
        return;
      }

      foreach (var ancestor in c.Ancestors)
      {
        if (selected.FirstOrDefault(s => s.Name == ancestor) is { } local)
        {
          Place(local);
        }
      }

      ordered.Add(c);
    }

    foreach (var c in selected)
    {
      Place(c);
    }

    return ordered;
  }

  // every field type below the fields (array elements, members of nested classes)
  private static IEnumerable<CppType> FieldTypes(IEnumerable<FieldModel> fields) =>
    fields.SelectMany(f => FieldTypes(f.Members ?? [])
                        .Prepend(f.Type is ArrayType array ? array.Innermost : f.Type));

  // libclang's layout, checked by the C++ compiler; the managed layout tests compare the C# structs
  // with NetOcc_SizeOf_*
  private string LayoutGuards(string own, List<ClassModel> valueTypes)
  {
    var text = new StringBuilder().AppendLine("%{")
      .AppendLine(
        $"// Layout guards for the C# structs in src/NetOcc/{own}/: a mismatch fails the native build.");
    foreach (var v in valueTypes)
    {
      var layout = v.Layout
                   ?? throw new InvalidOperationException(
                     $"{own}: no layout for value type {v.Name}");
      text.AppendLine(
        $"static_assert(sizeof({v.Name}) == {layout.Size} && alignof({v.Name}) == {layout.Align} && std::is_trivially_copyable<{v.Name}>::value, \"{v.Name} layout\");");
    }

    // the structs hold enums as C# enums (their underlying type, int by default) and bool as byte
    foreach (var e in valueTypes.SelectMany(v => FieldTypes(v.Fields ?? []))
               .OfType<NamedType>()
               .Where(t => t.Kind == NamedKind.Enum)
               .Select(t => t.Name)
               .Distinct()
               .Order(StringComparer.Ordinal))
    {
      var underlying = registry.Enum(e)?.Underlying ?? "int";
      text.AppendLine(
        $"static_assert(sizeof({e}) == sizeof({underlying}), \"{e}: a{(underlying == "int" ? "n" : "")} {SignatureMapper.CsBuiltin(underlying) ?? underlying} in C#\");");
    }

    if (valueTypes.SelectMany(v => FieldTypes(v.Fields ?? []))
        .Any(t => t is BuiltinType { Name: "bool" }))
    {
      text.AppendLine("static_assert(sizeof(bool) == 1, \"bool: a byte in C#\");");
    }

    text.AppendLine("%}")
      .AppendLine()
      .AppendLine("%inline %{")
      .AppendLine("// sizes for the managed layout tests");
    foreach (var v in valueTypes)
    {
      text.AppendLine($"inline int NetOcc_SizeOf_{v.Name}() {{ return (int)sizeof({v.Name}); }}");
    }

    return text.AppendLine("%}").ToString();
  }

  // the NCollection instantiations this package's aliases name and signatures use (the registry's
  // requests), the handle-managed ones after their bases. They go before the classes: SWIG applies
  // typemaps (the %occt_valueclass copies, the handle conversions) to the declarations after them
  // only, and resolves element classes declared later.
  private string Collections(string own, ModuleContext module)
  {
    var uses = module.Uses;
    var text = new StringBuilder();
    foreach (var instantiation in registry.RequestedIn(own))
    {
      var template = instantiation.Template;
      var types = instantiation.ArgumentTypes
                  ?? throw new InvalidOperationException(
                    $"{own}: {instantiation.Alias} has no parsed arguments");
      var (arguments, skip) = mapper.Arguments(template, types);
      if (arguments is null)
      {
        throw new InvalidOperationException(
          $"{own}: {instantiation.Alias} was requested, but {skip}");
      }

      uses.UnionWith(arguments.Uses);
      module.Typemaps.UnionWith(arguments.Typemaps);
      if (registry.BaseOf(instantiation) is { } baseCollection)
      {
        uses.Add(new TypeUse(baseCollection.Package, baseCollection.Header, baseCollection));
      }

      // an Array1 of numbers or structs: bulk copies to and from C# arrays
      var macro = template.HasBlittableVariant && arguments.IsBlittable
        ? $"{template.Macro}_blittable"
        : template.Macro;
      // SWIG macro arguments end at a comma, template brackets or not: one that holds commas goes
      // through %arg
      var cppArguments = instantiation.Arguments.Select(a => a.Contains(',') ? $"%arg({a})" : a);
      text.AppendLine(
        $"%{macro}({instantiation.Alias}, {string.Join(", ", cppArguments)}, {string.Join(", ", arguments.CsTypes)})");
    }

    return text.Length > 0 ? text.AppendLine().ToString() : "";
  }
}
