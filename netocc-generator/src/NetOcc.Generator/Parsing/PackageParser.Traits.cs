// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// ClangSharp
using ClangSharp;
using ClangSharp.Interop;

// alias directives
using ClangBuiltinType = ClangSharp.BuiltinType;
using ClangType = ClangSharp.Type;

namespace NetOcc.Generator.Parsing;

internal sealed partial class PackageParser
{
  // the Collector's class traits: what a class allows (new and delete, copies, a default
  // constructor, handles)
  private sealed partial class Collector
  {
    // "new T" and "delete p" use the class's own operator new/delete once it or a base declares
    // any; NCollection-allocated nodes declare only placement forms (with an allocator), so the
    // shim can neither create nor delete them. Through a protected or private base
    // (Message_LazyProgressScope), the base's operators are out of reach.
    private static bool HasUsualOperator(CXXRecordDecl r, CX_OverloadedOperatorKind kind)
    {
      var declared = r.Methods.Where(m => m.OverloadedOperator == kind).ToList();
      if (declared.Count > 0)
      {
        // operator new(size_t), operator delete(void*) or the sized operator delete(void*, size_t)
        return declared.Any(m => m.Access == CX_CXXAccessSpecifier.CX_CXXPublic
                                 && (m.Parameters.Count == 1
                                     || (kind == CX_OverloadedOperatorKind.CX_OO_Delete
                                         && m.Parameters.Count == 2
                                         && m.Parameters[1].Type
                                           .CanonicalType is ClangBuiltinType)));
      }

      foreach (var b in r.Bases)
      {
        if (BaseDefinition(b) is { } baseDecl
            && DeclaresOperator(baseDecl, kind)
            && (b.AccessSpecifier != CX_CXXAccessSpecifier.CX_CXXPublic
                || !HasUsualOperator(baseDecl, kind)))
        {
          return false;
        }
      }

      return true;
    }

    private static bool DeclaresOperator(CXXRecordDecl r, CX_OverloadedOperatorKind kind) =>
      r.Methods.Any(m => m.OverloadedOperator == kind)
      || r.Bases.Any(b => BaseDefinition(b) is { } d && DeclaresOperator(d, kind));

    private static CXXRecordDecl? BaseDefinition(CXXBaseSpecifier b)
    {
      if (b.Type.AsCXXRecordDecl is { } record)
      {
        return record.Definition;
      }

      try
      {
        return (b.Referenced as CXXRecordDecl)?.Definition;
      }
      catch (InvalidCastException)
      {
        // ClangSharp can't type the cursor of some dependent bases (inside templates): unknown
        return null;
      }
    }

    // a copy compiles: a user-provided copy constructor that is public and not deleted, or a
    // defaulted one (implicit or "= default") whose bases and fields copy. An NCollection container
    // copies its elements: a field NCollection_Sequence<CSLib_Class2d> doesn't compile,
    // CSLib_Class2d's copy constructor is deleted.
    private bool IsCopyable(CXXRecordDecl r, HashSet<string> visiting)
    {
      var copy = r.Ctors.FirstOrDefault(c => c.IsCopyConstructor);
      if (copy is { IsDefaulted: false })
      {
        return copy.Access == CX_CXXAccessSpecifier.CX_CXXPublic
               && !copy.IsDeleted
               && IsCallable(copy, IsExported(r))
               && (r is not ClassTemplateSpecializationDecl specialization
                   || !specialization.Name.StartsWith("NCollection_", StringComparison.Ordinal)
                   || specialization.TemplateArgs.All(a => a.Kind
                                                           != CXTemplateArgumentKind
                                                             .CXTemplateArgumentKind_Type
                                                           || IsCopyable(a.AsType, visiting)));
      }

      // deleted (implicitly too), or suppressed by a user-declared move
      if (copy is { IsDeleted: true } || (copy is null && r.HasUserDeclaredMoveOperation))
      {
        return false;
      }

      return !visiting.Add(r.QualifiedName)
             || (r.Bases.All(b => BaseDefinition(b) is not { } d || IsCopyable(d, visiting))
                 && r.Fields.All(f => IsCopyable(f.Type, visiting)));
    }

    private bool IsCopyable(ClangType type, HashSet<string> visiting) => type.CanonicalType switch
    {
      RecordType { Decl: CXXRecordDecl { Definition: { } definition } } => IsCopyable(
        definition, visiting),
      ConstantArrayType array => IsCopyable(array.ElementType, visiting),
      _ => true,
    };

    // "new T()" compiles: a public, non-deleted constructor callable without arguments, or the
    // implicit one (none declared), which exists when every base and field can be
    // default-constructed
    private static bool HasDefaultConstructor(CXXRecordDecl r, HashSet<string> visiting)
    {
      if (r.HasUserDeclaredConstructor)
      {
        return r.Ctors.Any(c => c.Access == CX_CXXAccessSpecifier.CX_CXXPublic
                                && !c.IsDeleted
                                && !c.IsCopyOrMoveConstructor
                                && c.Parameters.All(p => p.HasDefaultArg));
      }

      if (!visiting.Add(r.Name))
      {
        return true;
      }

      return r.Bases.All(b => BaseDefinition(b) is not { } d || HasDefaultConstructor(d, visiting))
             && r.Fields.All(f => f.InClassInitializer is not null
                                  || f.Type.CanonicalType switch
                                  {
                                    LValueReferenceType or RValueReferenceType => false,
                                    { IsLocalConstQualified: true } => false,
                                    RecordType
                                    {
                                      Decl: CXXRecordDecl { Definition: { } definition }
                                    } => HasDefaultConstructor(definition, visiting),
                                    _ => true,
                                  });
    }

    private static bool IsTransient(CXXRecordDecl r)
    {
      if (r.Name == "Standard_Transient")
      {
        return true;
      }

      return r.Bases.Any(b => BaseDefinition(b) is { } baseDecl && IsTransient(baseDecl));
    }
  }
}
