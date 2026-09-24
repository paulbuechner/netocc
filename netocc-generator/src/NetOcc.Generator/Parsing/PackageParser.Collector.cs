// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

// ClangSharp
using ClangSharp;
using ClangSharp.Interop;

//
using NetOcc.Generator.Mapping;
using NetOcc.Generator.Model;

// alias directives
using ClangBuiltinType = ClangSharp.BuiltinType;
using ClangPointerType = ClangSharp.PointerType;
using ClangReferenceType = ClangSharp.ReferenceType;
using ClangType = ClangSharp.Type;
using CppBuiltinType = NetOcc.Generator.Model.BuiltinType;
using CppPointerType = NetOcc.Generator.Model.PointerType;
using CppReferenceType = NetOcc.Generator.Model.ReferenceType;

namespace NetOcc.Generator.Parsing;

// the part of PackageParser that turns a translation unit's declarations into the package model
internal sealed partial class PackageParser
{
  /// <param name="occtInclude">
  /// OCCT's include directory, full path: calls into it must link, system and third-party ones do.
  /// </param>
  /// <param name="valueTypes">
  /// Classes whose fields are recorded with the members of class-typed ones.
  /// </param>
  /// <param name="classes">
  /// The only file-scope classes to take, with the value types; null for all (see
  /// <see cref="Parse"/>).
  /// </param>
  private sealed partial class Collector(CXTranslationUnit handle, HashSet<string> headers,
                                         LibraryExports? exports, string occtInclude,
                                         IReadOnlyCollection<string> valueTypes,
                                         IReadOnlyCollection<string>? classes = null)
  {
    private bool IsTaken(string name) =>
      classes is null || classes.Contains(name) || valueTypes.Contains(name);

    private readonly Dictionary<CXCursor, string?> _unlinkedInBody = [];

    // the inline bodies UnlinkedInBody is searching, by depth: a call cycle leads back to one
    private readonly Dictionary<CXCursor, int> _searching = [];
    private readonly Dictionary<string, bool> _occtFiles = new(StringComparer.OrdinalIgnoreCase);

    // class template instances public signatures use, by C++'s spelling (Track), and classes in
    // them
    private readonly Dictionary<string, ClassTemplateSpecializationDecl> _instances = [];
    private readonly Dictionary<string, CXXRecordDecl> _nested = [];

    // the worklist of CollectInstances: tracked instances and classes in them, in the order they
    // were found
    private readonly Queue<string> _pendingInstances = [];
    private readonly Queue<string> _pendingNested = [];

    // enums in class template instances public signatures use, by their C++ spelling
    private readonly Dictionary<string, EnumDecl> _nestedEnums = [];

    // public typedefs in classes that name class template instances (ShapePersistent_Geom::Curve),
    // by the instance's spelling: its flat name (null where a file-scope name has it: the class
    // ShapePersistent_Geom_Curve) and C++'s, which reach an instance of a protected template
    private readonly Dictionary<string, (string? Flat, string Qualified)> _memberAliases = [];

    public List<EnumModel> Enums { get; } = [];
    public List<ClassModel> Classes { get; } = [];
    public List<TypedefModel> Typedefs { get; } = [];
    public List<FunctionModel> Functions { get; } = [];
    public List<string> ExcludedTypes { get; } = [];

    /// <summary>
    /// The class template instances the parse saw no definition of, by alias or C++ spelling (to
    /// instantiate: Parse).
    /// </summary>
    public List<string> Uninstantiated { get; } = [];

    public void Visit(IEnumerable<Decl> decls, string? ns)
    {
      foreach (var decl in decls)
      {
        switch (decl)
        {
          // an anonymous namespace holds internal helpers (Extrema_GGenExtCC_comp), not API
          case NamespaceDecl { Name: "std" or "opencascade" or "" }:
            break;
          case NamespaceDecl nested:
            Visit(nested.Decls, ns is null ? nested.Name : $"{ns}::{nested.Name}");
            break;
          case LinkageSpecDecl linkage:
            Visit(linkage.Decls, ns);
            break;
          // anonymous enums get a descriptive name ("(unnamed enum at ...)"), not an empty one
          case EnumDecl { IsComplete: true } e when IsFileOrNamespaceScope(e)
                                                    && Header(e) is { } header
                                                    && IsNamed(e.Name)
                                                    && IsFlatNameFree(e):
            Enums.Add(Enum(e, header));
            break;
          case ClassTemplateSpecializationDecl:
            break;
          // not a nested class defined out of line ("class BRepGraph::ShapesView { ... };" at file
          // scope): that one comes with its class
          case CXXRecordDecl { IsThisDeclarationADefinition: true, DescribedClassTemplate: null } r
            when IsFileOrNamespaceScope(r)
                 && Header(r) is { } header
                 && IsNamed(r.Name)
                 && !r.IsUnion
                 && IsTaken(FlatName(r.QualifiedName))
                 && IsFlatNameFree(r):
            Classes.Add(Class(r, header));
            VisitNested(r);
            break;
          case TypedefNameDecl t when ns is null && Header(t) is { } header:
            Typedefs.Add(new TypedefModel(t.Name, header, Convert(t.UnderlyingType)));
            // collections are netocc-core's macros' (math_Vector)
            if (t.UnderlyingType.CanonicalType is RecordType
                {
                  Decl: ClassTemplateSpecializationDecl instance
                }
                && InstanceAlias(instance) == t.Name
                && CollectionTemplate.Named(FlatName(TemplateName(instance.QualifiedName))) is null
                && IsTaken(t.Name))
            {
              Instance(t.Name, instance, header);
            }

            break;
          case FunctionDecl { DescribedFunctionDecl: null } f when ns is not null
            && decl is not CXXMethodDecl
            && Header(f) is not null
            && !f.IsOverloadedOperator:
            var unlinked = Unlinked(f, classExported: false);
            Track(f.ReturnType);
            Functions.Add(new FunctionModel(ns, f.Name, Convert(f.ReturnType),
                                            Parameters(f, track: true), f.IsDeprecated,
                                            unlinked is null, unlinked));
            break;
        }
      }
    }

    // declared at file or namespace scope semantically too, not a member of a class
    private static bool IsFileOrNamespaceScope(Decl decl) =>
      decl.DeclContext is TranslationUnitDecl or LinkageSpecDecl or NamespaceDecl;

    // the public classes and enums declared in a class, defined there or out of line, and theirs
    private void VisitNested(CXXRecordDecl outer)
    {
      foreach (var decl in outer.Decls)
      {
        if (decl.Access != CX_CXXAccessSpecifier.CX_CXXPublic)
        {
          continue;
        }

        switch (decl)
        {
          case EnumDecl { IsComplete: true } e
            when Header(e) is { } header && IsNamed(e.Name) && IsFlatNameFree(e):
            Enums.Add(Enum(e, header));
            break;
          // a pair's alias in a class (XSAlgo_ShapeProcessor::ProcessingData) names it as a
          // file-scope one does, under its flat name: a synthesized pair name is long. Not other
          // collections': file-scope aliases (TColgp_Array1OfPnt) come first, and traits' typedefs
          // (ContainerType) say less than a synthesized name. Other instances are classes
          // (MemberAlias).
          case TypedefNameDecl
          {
            UnderlyingType.CanonicalType: RecordType
            {
              Decl: ClassTemplateSpecializationDecl instance
            }
          } alias when FlatName(TemplateName(instance.QualifiedName)) == "std::pair":
            if (Header(alias) is { } aliasHeader && IsFlatNameFree(alias))
            {
              Typedefs.Add(new TypedefModel(FlatName(alias.QualifiedName), aliasHeader,
                                            Convert(alias.UnderlyingType)));
            }

            break;
          case TypedefNameDecl
          {
            UnderlyingType.CanonicalType: RecordType
            {
              Decl: ClassTemplateSpecializationDecl instance
            }
          } alias:
            MemberAlias(instance, alias.QualifiedName, depth: 0);
            break;
          case ClassTemplateSpecializationDecl:
            break;
          case CXXRecordDecl { DescribedClassTemplate: null, Definition: { } r }
            when Header(r) is { } header
                 && IsNamed(r.Name)
                 && !r.IsUnion
                 && !r.IsAnonymousStructOrUnion
                 && Classes.All(c => c.QualifiedName != r.QualifiedName)
                 && IsFlatNameFree(r):
            Classes.Add(Class(r, header));
            VisitNested(r);
            break;
        }
      }
    }

    // how deep typedefs in instances name further instances in OCCT
    // (ShapePersistent_BRep::TVertex::pTObjectT)
    private const int MaxMemberAliasDepth = 2;

    // a public typedef in a class naming a class template instance, and the ones in that instance
    // and its bases, which reach deeper instances through it
    // (ShapePersistent_BRep::TVertex::pTObjectT, declared in a base of TVertex)
    private void MemberAlias(ClassTemplateSpecializationDecl instance, string qualified, int depth)
    {
      var flat = FlatName(qualified);
      _memberAliases.TryAdd(instance.TypeForDecl.CanonicalType.AsString,
                            (IsFileScopeName(instance, flat) ? null : flat, qualified));
      if (depth >= MaxMemberAliasDepth || instance.Definition is not CXXRecordDecl definition)
      {
        return;
      }

      foreach (var member in WithBases(definition, 0)
                 .SelectMany(r => r.Decls.OfType<TypedefNameDecl>())
                 .Where(t => t.Access is CX_CXXAccessSpecifier.CX_CXXPublic
                          or CX_CXXAccessSpecifier.CX_CXXInvalidAccessSpecifier))
      {
        if (member.UnderlyingType.CanonicalType is RecordType
            {
              Decl: ClassTemplateSpecializationDecl inner
            })
        {
          MemberAlias(inner, $"{qualified}::{member.Name}", depth + 1);
        }
      }
    }

    // a class and its public bases, recursively
    private static IEnumerable<CXXRecordDecl> WithBases(CXXRecordDecl r, int depth) =>
      depth > MaxNesting
        ? []
        : r.Bases.Where(b => b.AccessSpecifier == CX_CXXAccessSpecifier.CX_CXXPublic)
          .Select(BaseDefinition)
          .OfType<CXXRecordDecl>()
          .SelectMany(b => WithBases(b, depth + 1))
          .Prepend(r);

    // a class template instance an alias names (see InstanceAlias): a class of the package under
    // that name, where the headers instantiate it
    private void Instance(string alias, ClassTemplateSpecializationDecl instance, string header)
    {
      if (instance.Definition is not { } definition)
      {
        ExcludedTypes.Add(
          $"{alias}: {instance.TypeForDecl.CanonicalType.AsString} is never instantiated in the headers");
        Uninstantiated.Add(alias);
        return;
      }

      Classes.Add(Class(definition, header, alias));
    }

    // an enum's underlying type as its declaration fixes it (enum X : uint8_t, every enum class).
    // Without, the ABI picks one (MSVC int, the Itanium ABI unsigned int when no value is
    // negative), and the wrappers pass it as the int MSVC gives it, whichever host generates
    private EnumModel Enum(EnumDecl e, string header)
    {
      var integer = e.IntegerType.CanonicalType;
      var isFixed = IsFixed(e);
      var underlying = isFixed ? integer.AsString : "int";
      // InitVal sign-extends: 0xF000 of an enum on uint16_t reads -4096
      var unsigned = isFixed
                     && integer is ClangBuiltinType
                     {
                       Kind: CXTypeKind.CXType_Bool or CXTypeKind.CXType_Char_U
                       or CXTypeKind.CXType_UChar
                       or CXTypeKind.CXType_Char16 or CXTypeKind.CXType_Char32
                       or CXTypeKind.CXType_UShort
                       or CXTypeKind.CXType_UInt or CXTypeKind.CXType_ULong
                       or CXTypeKind.CXType_ULongLong
                       or CXTypeKind.CXType_WChar
                     };
      return new EnumModel(FlatName(e.QualifiedName), header, [
                             .. e.Enumerators.Select(c => new EnumConstant(
                                                       c.Name,
                                                       unsigned
                                                         ? checked((long)c.UnsignedInitVal)
                                                         : c.InitVal))
                           ],
                           e.QualifiedName.Contains("::", StringComparison.Ordinal)
                             ? e.QualifiedName
                             : null,
                           underlying == "int" ? null : underlying);
    }

    // the declaration writes an underlying type: every enum class, an unscoped enum with ": type"
    // before its body
    private bool IsFixed(EnumDecl e) =>
      e.IsScoped || Spellings(e.Extent).FirstOrDefault(s => s is "{" or ":") == ":";

    // the spellings of the tokens in an extent, libclang's tokens and strings freed
    private List<string> Spellings(CXSourceRange extent)
    {
      var tokens = handle.Tokenize(extent);
      try
      {
        List<string> spellings = [];
        foreach (var token in tokens)
        {
          spellings.Add(Consume(token.GetSpelling(handle)));
        }

        return spellings;
      }
      finally
      {
        handle.DisposeTokens(tokens);
      }
    }

    // a nested or namespace type's flat name must not name something else at file scope
    // (BVH::RadixSorter next to the class template BVH_RadixSorter): such a type stays out, logged
    private bool IsFlatNameFree(NamedDecl decl)
    {
      if (FlatName(decl.QualifiedName) is var flat
          && (flat == decl.QualifiedName || !IsFileScopeName(decl, flat)))
      {
        return true;
      }

      ExcludedTypes.Add($"{decl.QualifiedName}: its flat name {flat} is a file-scope name too");
      return false;
    }

    /// <param name="alias">
    /// The name of a class template instance, which C++ declares (a typedef at file scope).
    /// </param>
    private ClassModel Class(CXXRecordDecl r, string header, string? alias = null)
    {
      var exported = IsExported(r);
      // an inline constructor or copy of a polymorphic class the libraries don't export emits the
      // vtable in the shim
      var vtable = exported ? null : UnlinkedVirtual(r);

      // the destructor must link too: OCCT doesn't export a few (Storage_Bucket), or defines them
      // inline over internals
      var traits = new ClassTraits(IsTransient: IsTransient(r), IsAbstract: r.IsAbstract,
                                   HasPublicDestructor: (r.Destructor is null
                                                         || (r.Destructor.Access
                                                             == CX_CXXAccessSpecifier.CX_CXXPublic
                                                             && (r.Destructor.IsDefaulted
                                                                 || IsCallable(
                                                                   r.Destructor, exported))))
                                                        && HasUsualOperator(r,
                                                          CX_OverloadedOperatorKind.CX_OO_Delete),
                                   IsCopyable: IsCopyable(r, [])
                                               && (vtable is null || !HasInlineCopy(r)),
                                   IsCreatable: HasUsualOperator(
                                     r, CX_OverloadedOperatorKind.CX_OO_New),
                                   HasDefaultConstructor: HasDefaultConstructor(r, []),
                                   IsMovable: r.Ctors.Any(c => c.IsMoveConstructor
                                                               && c.Access
                                                               == CX_CXXAccessSpecifier.CX_CXXPublic
                                                               && !c.IsDeleted
                                                               && IsCallable(c, exported)),
                                   DeclaresDestructor: r.Destructor is { } destructor
                                                       && !destructor.IsImplicit());

      // CXXRecordDecl.Methods leaves out the constructors, which come from Ctors
      List<ConstructorModel> constructors =
      [
        .. r.Ctors
          .Where(c => c.Access == CX_CXXAccessSpecifier.CX_CXXPublic
                      && !c.IsDeleted
                      && !c.IsImplicit()
                      && !c.IsCopyOrMoveConstructor)
          .Select(c => Constructor(c, exported, vtable))
      ];

      // a class that declares no constructor has an implicit default one where T() compiles
      // (BRep_Builder): inline, it sets the vtable and calls the bases' and members' default
      // constructors
      if (!r.HasUserDeclaredConstructor && traits.HasDefaultConstructor && !r.IsAbstract)
      {
        var unlinked = vtable ?? UnlinkedDefault(r, []);
        constructors.Add(new ConstructorModel([], false, unlinked is null, unlinked,
                                              ThroughVtable: vtable is not null));
      }

      var name = alias ?? FlatName(r.QualifiedName);
      List<MethodModel> methods = [];
      List<MethodModel> operators = [];
      // non-public members take part in C++ overload resolution: a public Perform() next to a
      // private Perform(const Message_ProgressRange& = {}) makes the call ambiguous. Constructors
      // go by an empty name (MemberRules.HiddenConstructors): an alias may rename the class later.
      List<MethodModel> hidden =
      [
        .. r.Ctors
          .Where(c => c.Access != CX_CXXAccessSpecifier.CX_CXXPublic
                      && !c.IsDeleted
                      && !c.IsImplicit()
                      && !c.IsCopyOrMoveConstructor)
          .Select(c => Method(c, exported) with { Name = "" })
      ];
      foreach (var decl in r.Decls)
      {
        // "using Base::Member;" makes the base's overloads members of this class: OCCT re-exposes
        // protected bases this way
        var (method, access, classExported) = decl switch
        {
          CXXMethodDecl own => (own, own.Access, exported),
          UsingShadowDecl { UnderlyingDecl: CXXMethodDecl { Parent: { } owner } target } shadow => (
            target, shadow.Access, IsExported(owner)),
          _ => (null, CX_CXXAccessSpecifier.CX_CXXInvalidAccessSpecifier, false),
        };
        if (method is null
            || method.IsDeleted
            || method.IsImplicit()
            || method is CXXConstructorDecl or CXXDestructorDecl or CXXConversionDecl)
        {
          continue;
        }

        var isPublic = access == CX_CXXAccessSpecifier.CX_CXXPublic;
        var bucket = !isPublic ? hidden : method.IsOverloadedOperator ? operators : methods;
        bucket.Add(Method(method, classExported, track: isPublic));
      }

      List<string> ancestors = [];
      Ancestors(r, ancestors);
      var type = r.TypeForDecl.Handle;
      var plainData = IsPlainData(r, []);
      List<FieldModel> fields =
        [.. r.Fields.Select(f => Field(f, deep: plainData || valueTypes.Contains(name), depth: 0))];
      List<CppType> held = [];
      Held(r, held, [], depth: 0);
      return new ClassModel(name, header, ancestors, traits, constructors, methods,
                            new ClassLayout(type.SizeOf, type.AlignOf), fields, operators, hidden,
                            r.IsDeprecated,
                            alias is null && name != r.QualifiedName ? r.QualifiedName : null,
                            plainData, Held: held, IsStruct: r.IsStruct,
                            Template: DocumentedTemplate(r));
    }

    // what OCCT's reference manual documents for a class template instance, or a class in one: the
    // template, or the class around a non-public one (the manual has public members only); null for
    // other classes
    private static string? DocumentedTemplate(CXXRecordDecl r)
    {
      if (!IsInInstance(r))
      {
        return null;
      }

      var name = WithoutTemplateArguments(r.TypeForDecl.CanonicalType.AsString);
      var access =
        r is ClassTemplateSpecializationDecl instance && Template(instance) is { } template
          ? template.Access
          : r.Access;
      return access is CX_CXXAccessSpecifier.CX_CXXProtected or CX_CXXAccessSpecifier.CX_CXXPrivate
             && name.LastIndexOf("::", StringComparison.Ordinal) is var outer and > 0
        ? name[..outer]
        : name;
    }

    // a class template instance, or a class in one
    private static bool IsInInstance(Decl decl) =>
      decl is ClassTemplateSpecializationDecl
      || (decl.DeclContext is Decl outer && IsInInstance(outer));

    // the types of the pointer and reference fields of a class and of its bases, whatever their
    // access: what the object refers to. Not a collection's (their pointers are the storage they
    // own) nor the standard library's.
    private static void Held(CXXRecordDecl r, List<CppType> into, HashSet<string> visited,
                             int depth)
    {
      if (depth > MaxNesting
          || !visited.Add(r.TypeForDecl.CanonicalType.AsString)
          || r.QualifiedName.StartsWith("std::", StringComparison.Ordinal)
          || (r is ClassTemplateSpecializationDecl instance
              && CollectionTemplate.Named(FlatName(TemplateName(instance.QualifiedName))) is not
                null))
      {
        return;
      }

      into.AddRange(
        r.Fields.Where(f => f.Type.CanonicalType is ClangPointerType or LValueReferenceType)
          .Select(f => Convert(f.Type)));
      foreach (var b in r.Bases)
      {
        if (BaseDefinition(b) is { } baseDecl)
        {
          Held(baseDecl, into, visited, depth + 1);
        }
      }
    }

    // public data only, all of it numbers, enums, value types and plain data: a C# struct copies it
    // byte for byte
    private bool IsPlainData(CXXRecordDecl r, HashSet<string> visiting)
    {
      if (!visiting.Add(r.QualifiedName)
          || r.Bases.Count > 0
          || r.Fields.Count == 0
          || r.Methods.Any(m => m.IsVirtual)
          || (r.Destructor is { } destructor && !destructor.IsImplicit())
          || r.Ctors.Any(c => c.IsCopyOrMoveConstructor && !c.IsImplicit()))
      {
        return false;
      }

      // a bit field has no C# field to mirror it (MeshVS_TwoColors packs six bytes into two ints)
      return r.Fields.All(f => f.Access == CX_CXXAccessSpecifier.CX_CXXPublic
                               && !f.IsBitField
                               && IsPlainValue(f.Type, visiting));
    }

    // as written, not canonical: the sugar tells a size_t from the unsigned long long it is here
    private bool IsPlainValue(ClangType type, HashSet<string> visiting) => IsFixedSize(type)
      && Desugared(type) switch
      {
        ConstantArrayType array => IsPlainValue(array.ElementType, visiting),
        ClangBuiltinType => true,
        EnumType e => IsFixedSize(e.Decl.IntegerType),
        RecordType { Decl: CXXRecordDecl { Definition: { } record } } =>
          valueTypes.Contains(FlatName(record.QualifiedName)) || IsPlainData(record, visiting),
        _ => false,
      };

    // a data member; deep: with the members of a class-typed one (or an array of them), recursively
    private static FieldModel Field(FieldDecl field, bool deep, int depth)
    {
      // a std::array as the C array it holds (MSVC's _Elems, libstdc++'s _M_elems): Roots_0, not
      // Roots__Elems_0
      var type = StdArrayElements(field.Type) ?? field.Type;
      IReadOnlyList<FieldModel>? members = null;
      var element = type.CanonicalType;
      while (element is ConstantArrayType array)
      {
        element = array.ElementType.CanonicalType;
      }

      if (deep
          && depth < MaxNesting
          && element is RecordType { Decl: CXXRecordDecl { Definition: { } record } })
      {
        members = [.. record.Fields.Select(m => Field(m, deep, depth + 1))];
      }

      return new FieldModel(field.Name, Convert(type), field.Handle.OffsetOfField / 8,
                            field.Type.Handle.SizeOf, members,
                            field.Access == CX_CXXAccessSpecifier.CX_CXXPublic);
    }

    /// <param name="vtable">
    /// A virtual function of the class that doesn't link, when the libraries don't export the
    /// class.
    /// </param>
    private ConstructorModel Constructor(CXXConstructorDecl constructor, bool classExported,
                                         string? vtable)
    {
      // an inline delegating constructor makes MSVC reference the vtable without emitting it: it
      // takes it from the libraries when they export the class's virtual functions, and they may
      // not export the vtable (Select3D_SensitiveCircle's deprecated constructor)
      if (constructor.Definition is not null
          && (vtable is not null
              || (constructor.IsDelegatingConstructor && IsVtableUnexported(constructor.Parent!))))
      {
        return new ConstructorModel(Parameters(constructor, track: true), constructor.IsDeprecated,
                                    false, vtable, ThroughVtable: true);
      }

      var unlinked = Unlinked(constructor, classExported);
      return new ConstructorModel(Parameters(constructor, track: true), constructor.IsDeprecated,
                                  unlinked is null, unlinked);
    }

    // what an implicit default constructor calls that doesn't link: the default constructors of the
    // bases and of the members without an initializer, recursively through the implicit ones
    private string? UnlinkedDefault(CXXRecordDecl r, HashSet<string> visiting)
    {
      if (!visiting.Add(r.Name))
      {
        return null;
      }

      IEnumerable<CXXRecordDecl> parts =
      [
        .. r.Bases.Select(BaseDefinition).OfType<CXXRecordDecl>(),
        .. r.Fields.Where(f => f.InClassInitializer is null)
          .Select(f => f.Type.CanonicalType is RecordType
          {
            Decl: CXXRecordDecl { Definition: { } definition }
          }
            ? definition
            : null)
          .OfType<CXXRecordDecl>(),
      ];
      foreach (var part in parts)
      {
        var unlinked = part.HasUserDeclaredConstructor
          ? part.Ctors.FirstOrDefault(c => !c.IsDeleted
                                           && !c.IsCopyOrMoveConstructor
                                           && c.Parameters.All(p => p.HasDefaultArg)) is { } ctor
            ? Unlinked(ctor, IsExported(part))
            : null
          : UnlinkedDefault(part, visiting);
        if (unlinked is not null)
        {
          return unlinked;
        }
      }

      return null;
    }

    // the copy constructor is compiled into the shim: implicit, defaulted, or defined in the header
    private static bool HasInlineCopy(CXXRecordDecl r) =>
      r.Ctors.FirstOrDefault(c => c.IsCopyConstructor) is not { } copy
      || copy.IsImplicit()
      || copy.Definition is not null;

    /// <param name="track">
    /// A public member: the class template instances its signature uses are classes too (Track).
    /// </param>
    private MethodModel Method(CXXMethodDecl method, bool classExported, bool track = false)
    {
      var unlinked = Unlinked(method, classExported);
      if (track)
      {
        Track(method.ReturnType);
      }

      return new MethodModel(method.Name, Convert(method.ReturnType), Parameters(method, track),
                             method.IsStatic, method.IsConst, method.IsVirtual, method.IsDeprecated,
                             unlinked is null, unlinked);
    }

    // a class template instance a public signature uses, through pointers, references and handles:
    // a class of the package under its flat name, when it is an OCCT template's and no alias names
    // it. Not the collections, which netocc-core's macros declare, nor the range-for iterators,
    // which are IEnumerable in C#.
    private void Track(ClangType type)
    {
      var canonical = type.CanonicalType;
      while (canonical is ClangPointerType or ClangReferenceType)
      {
        canonical = canonical is ClangPointerType pointer
          ? pointer.PointeeType.CanonicalType
          : ((ClangReferenceType)canonical).PointeeType.CanonicalType;
      }

      // an enum in an instance is an enum of its own
      if (canonical is EnumType { Decl: var nestedEnum }
          && NestedInstanceName(nestedEnum) is not null
          && IsOcct(nestedEnum)
          && IsAccessible(nestedEnum))
      {
        _nestedEnums.TryAdd(nestedEnum.TypeForDecl.CanonicalType.AsString, nestedEnum);
        return;
      }

      // a class in an instance (NCollection_Map<T>::Iterator) is a class of its own too
      if (canonical is RecordType { Decl: CXXRecordDecl nested }
          && NestedInstanceName(nested) is not null
          && IsOcct(nested)
          && !nested.QualifiedName.StartsWith("std::", StringComparison.Ordinal))
      {
        if (_nested.TryAdd(nested.TypeForDecl.CanonicalType.AsString, nested))
        {
          _pendingNested.Enqueue(nested.TypeForDecl.CanonicalType.AsString);
        }

        return;
      }

      if (canonical is not RecordType { Decl: ClassTemplateSpecializationDecl instance })
      {
        return;
      }

      var template = FlatName(TemplateName(instance.QualifiedName));
      if (template.Contains('<'))
      {
        return;
      }

      if (template == "opencascade::handle")
      {
        foreach (var argument in instance.TemplateArgs.Where(a => a.Kind
                                                               == CXTemplateArgumentKind
                                                                 .CXTemplateArgumentKind_Type))
        {
          Track(argument.AsType);
        }

        return;
      }

      if (CollectionTemplate.Named(template) is null
          && template != "NCollection_ForwardRangeIterator"
          && InstanceAlias(instance) is null
          && !template.StartsWith("std::", StringComparison.Ordinal)
          && IsOcct(instance))
      {
        if (_instances.TryAdd(instance.TypeForDecl.CanonicalType.AsString, instance))
        {
          _pendingInstances.Enqueue(instance.TypeForDecl.CanonicalType.AsString);
        }
      }
    }

    // C++ names it outside its classes: it and every class around it are public members, and so are
    // the classes among its template arguments (ShapePersistent_Geom::geometryBase is protected)
    private static bool IsAccessible(Decl decl)
    {
      // an implicit instance has no access of its own: its member template's counts
      if (decl is ClassTemplateSpecializationDecl
          {
            SpecializationKind: CX_TemplateSpecializationKind.CX_TSK_ImplicitInstantiation
          } implicitInstance
          && Template(implicitInstance) is { } memberTemplate
          && !IsAccessible(memberTemplate))
      {
        return false;
      }

      for (var inner = decl; inner.DeclContext is CXXRecordDecl outer; inner = outer)
      {
        if (inner.Access is not (CX_CXXAccessSpecifier.CX_CXXPublic
            or CX_CXXAccessSpecifier.CX_CXXInvalidAccessSpecifier))
        {
          return false;
        }
      }

      return decl is not ClassTemplateSpecializationDecl instance
             || instance.TemplateArgs
               .Where(a => a.Kind == CXTemplateArgumentKind.CXTemplateArgumentKind_Type)
               .All(a => a.AsType.CanonicalType is not TagType { Decl: var tag }
                         || IsAccessible(tag));
    }

    /// <summary>
    /// The tracked class template instances as classes of the package, under their flat names
    /// (members may track more); the ones without a definition in the parse go to
    /// <see cref="Uninstantiated"/>. Instances of arguments that differ per platform (size_t is
    /// unsigned long on Linux) stay out: a C++ alias can't spell them for every platform.
    /// </summary>
    public void CollectInstances()
    {
      // a class's members may track more instances and classes in them: until none is left,
      // instances first
      while (true)
      {
        if (_pendingInstances.TryDequeue(out var spelling))
        {
          CollectInstance(spelling, _instances[spelling]);
        }
        else if (_pendingNested.TryDequeue(out var nestedSpelling))
        {
          CollectNested(nestedSpelling, _nested[nestedSpelling]);
        }
        else
        {
          // enums in instances, under their flat names, which the nested header declares by C++'s
          // spelling
          foreach (var (enumSpelling, e) in _nestedEnums)
          {
            Enums.Add(Enum(e, FileNameOf((ClassTemplateSpecializationDecl)e.DeclContext!)) with
            {
              Name = NestedInstanceName(e)!, QualifiedName = enumSpelling
            });
          }

          return;
        }
      }
    }

    private void CollectInstance(string spelling, ClassTemplateSpecializationDecl instance)
    {
      if (Convert(instance.TypeForDecl) is not NamedType type)
      {
        return;
      }

      if (IsPlatformSpelled(type))
      {
        ExcludedTypes.Add($"{spelling}: a template argument differs per platform");
        return;
      }

      // a public typedef in a class names it, where C++ can't name the instance itself
      (string? Flat, string Qualified)? alias =
        _memberAliases.TryGetValue(spelling, out var memberAlias) ? memberAlias : null;
      if (alias is null && !IsAccessible(instance))
      {
        ExcludedTypes.Add(
          $"{spelling}: a protected or private class, which C++ can't name outside its class");
        return;
      }

      if (instance.Definition is not CXXRecordDecl definition)
      {
        Uninstantiated.Add(alias?.Qualified ?? spelling);
        return;
      }

      var model = Class(definition, FileNameOf(instance), alias?.Flat ?? type.InstanceName);
      Classes.Add(model with
      {
        Instance =
        new ClassInstance(
          spelling, [.. Includes(instance).Distinct().Order(StringComparer.Ordinal)], type,
          alias?.Qualified),
      });
    }

    // a class in an instance, named after its instance
    private void CollectNested(string spelling, CXXRecordDecl nested)
    {
      var outer = (ClassTemplateSpecializationDecl)nested.DeclContext!;
      if (Convert(outer.TypeForDecl) is not NamedType outerType)
      {
        return;
      }

      if (IsPlatformSpelled(outerType))
      {
        ExcludedTypes.Add($"{spelling}: a template argument differs per platform");
        return;
      }

      if (!IsAccessible(nested))
      {
        ExcludedTypes.Add(
          $"{spelling}: a protected or private class, which C++ can't name outside its class");
        return;
      }

      if (nested.Definition is not CXXRecordDecl definition)
      {
        Uninstantiated.Add(spelling);
        return;
      }

      var name = NestedInstanceName(nested)!;
      Classes.Add(Class(definition, FileNameOf(outer), name) with
      {
        Instance =
        new ClassInstance(spelling, [.. Includes(outer).Distinct().Order(StringComparer.Ordinal)],
                          new NamedType(name, NamedKind.Class, [])),
      });
    }

    private static bool IsPlatformSpelled(CppType type) => type switch
    {
      CppBuiltinType
      {
        Name: "long long" or "unsigned long long" or "size_t" or "long" or "unsigned long"
        or "wchar_t"
      } => true,
      NamedType n => n.TemplateArguments.Any(IsPlatformSpelled),
      _ => false,
    };

    // the OCCT headers that declare a class template instance's template and argument types,
    // recursively
    private IEnumerable<string> Includes(ClassTemplateSpecializationDecl instance)
    {
      yield return FileNameOf(instance);
      foreach (var argument in instance.TemplateArgs.Where(a => a.Kind
                                                                == CXTemplateArgumentKind
                                                                  .CXTemplateArgumentKind_Type))
      {
        switch (argument.AsType.CanonicalType)
        {
          case RecordType { Decl: ClassTemplateSpecializationDecl nested } when IsOcct(nested):
            foreach (var header in Includes(nested))
            {
              yield return header;
            }

            break;
          case TagType { Decl: var tag } when IsOcct(tag):
            yield return FileNameOf(tag);
            break;
        }
      }
    }

    private static string FileNameOf(Decl decl) => Path.GetFileName(FileOf(decl));

    // the template an instance instantiates; ClangSharp throws for partial specializations (std
    // internals)
    private static ClassTemplateDecl? Template(ClassTemplateSpecializationDecl instance)
    {
      try
      {
        return instance.SpecializedTemplate;
      }
      catch (InvalidCastException)
      {
        return null;
      }
    }

    private static void Ancestors(CXXRecordDecl r, List<string> into)
    {
      foreach (var b in r.Bases.Where(b => b.AccessSpecifier == CX_CXXAccessSpecifier.CX_CXXPublic))
      {
        if (BaseDefinition(b) is not { } baseDecl
            || into.Contains(FlatName(baseDecl.QualifiedName)))
        {
          continue;
        }

        into.Add(FlatName(baseDecl.QualifiedName));
        Ancestors(baseDecl, into);
      }
    }

    /// <param name="track">
    /// A public function: the class template instances the parameters use are classes too (Track).
    /// </param>
    private List<ParameterModel> Parameters(FunctionDecl function, bool track = false)
    {
      if (track)
      {
        foreach (var parameter in function.Parameters)
        {
          Track(parameter.Type);
        }
      }

      // a class template instance's members keep their default arguments uninstantiated until a
      // call uses them
      return
      [
        .. function.Parameters.Select((p, i) => new ParameterModel(
                                        ParameterName(p.Name, i), Convert(p.Type),
                                        !p.HasDefaultArg
                                          ? null
                                          :
                                          (p.DefaultArg ?? p.UninstantiatedDefaultArg) is { } expr
                                            ?
                                            DefaultText(expr, p.Type)
                                            : ""))
      ];
    }

    // a default argument as written, except an enumerator of a nested or namespace enum:
    // Kind::Solid, written in BRepGraph_NodeId, is BRepGraph_NodeId_Kind::Solid, which also
    // resolves outside the class (value-type thunks). A macro body's tokens aren't where the
    // default is (DEFINE_STANDARD_EXCEPTION's theMessage = "", INT_MAX): a constant's value, for a
    // parameter it fits (Evaluated), else empty.
    private string DefaultText(Expr expr, ClangType type)
    {
      var inner = expr;
      while (inner is ImplicitCastExpr or ParenExpr or ConstantExpr)
      {
        inner = inner switch
        {
          ImplicitCastExpr cast => cast.SubExpr,
          ParenExpr paren => paren.SubExpr,
          ConstantExpr constant => constant.SubExpr,
          _ => inner,
        };
      }

      if (inner is DeclRefExpr { Decl: EnumConstantDecl { DeclContext: EnumDecl owner } enumerator }
          && owner.QualifiedName.Contains("::", StringComparison.Ordinal))
      {
        return $"{FlatName(owner.QualifiedName)}::{enumerator.Name}";
      }

      if (InMacroBody(expr.Extent.Start) || InMacroBody(expr.Extent.End))
      {
        return Evaluated(expr, type) ?? "";
      }

      // "= {}", which SWIG can't parse: the type's value-initialization, which both it and C++
      // (shims, thunks) take
      var text = Text(expr);
      return text.TrimStart('=').Trim() == "{}" ? ValueInitialized(type) : text;
    }

    private static string ValueInitialized(ClangType type)
    {
      var converted = Convert(type);
      var value = converted is CppReferenceType reference ? reference.Referee : converted;
      return value switch
      {
        CppPointerType => "nullptr",
        NamedType or CppBuiltinType => $"{value.WithoutConst().Spelling}()",
        _ => "{}",
      };
    }

    // spelled in a macro's definition: tokenizing the extent would span from there to the expansion
    private static bool InMacroBody(CXSourceLocation location)
    {
      location.GetSpellingLocation(out var spellingFile, out _, out _, out var spellingOffset);
      location.GetFileLocation(out var file, out _, out _, out var offset);
      return spellingFile.Handle != file.Handle || spellingOffset != offset;
    }

    // a constant default as a C++ literal, for a number, bool or C string parameter, where a
    // literal of its value always fits (shims and thunks compile the default); null for anything
    // else
    private static string? Evaluated(Expr expr, ClangType type)
    {
      var canonical = type.CanonicalType;
      var isString = canonical is ClangPointerType
      {
        PointeeType.CanonicalType: ClangBuiltinType { Kind: CXTypeKind.CXType_Char_S }
      };
      var result = expr.Handle.Evaluate;
      try
      {
        return result.Kind switch
        {
          CXEvalResultKind.CXEval_Int when canonical is ClangBuiltinType => result.IsUnsignedInt
            ? result.AsUnsigned.ToString(CultureInfo.InvariantCulture)
            : result.AsLongLong.ToString(CultureInfo.InvariantCulture),
          CXEvalResultKind.CXEval_Float when canonical is ClangBuiltinType
                                             && double.IsFinite(result.AsDouble) => result.AsDouble
            .ToString("R", CultureInfo.InvariantCulture),
          CXEvalResultKind.CXEval_StrLiteral when isString
                                                  && result.AsStr.All(c => c is >= ' ' and <= '~')
            => $"\"{result.AsStr.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
          _ => null,
        };
      }
      finally
      {
        result.Dispose();
      }
    }

    private string? Header(Decl decl)
    {
      decl.Location.GetFileLocation(out var file, out _, out _, out _);
      var name = Path.GetFileName(Consume(file.Name));
      return headers.Contains(name) ? name : null;
    }

    // source text of an expression from its tokens: default arguments go into the .i as written
    private string Text(Expr expr)
    {
      var text = new StringBuilder();
      string? previous = null;
      foreach (var spelling in Spellings(expr.Extent))
      {
        if (previous is not null && IsWord(previous) && IsWord(spelling))
        {
          text.Append(' ');
        }

        text.Append(spelling);
        previous = spelling;
      }

      return text.ToString();
    }

    private static bool IsNamed(string name) =>
      name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_');

    private static bool IsWord(string token) => char.IsLetterOrDigit(token[^1]) || token[^1] == '_';
  }
}

file static class CursorExtensions
{
  // declared by the compiler, not in the header: defaulted without "= default"
  public static bool IsImplicit(this FunctionDecl function) =>
    function.IsDefaulted && !function.IsExplicitlyDefaulted;
}
