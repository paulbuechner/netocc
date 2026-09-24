// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// ClangSharp
using ClangSharp;
using ClangSharp.Interop;

namespace NetOcc.Generator.Parsing;

/// <summary>
/// A documented member: its name (<c>#ctor</c> for a constructor), its parameters' names, its XML
/// documentation.
/// </summary>
internal sealed record MemberDocs(string Name, IReadOnlyList<string> Parameters, string Xml);

/// <summary>A type's documentation: its own, and its documented members'.</summary>
internal sealed class TypeDocs
{
  public string? Xml { get; set; }

  public List<MemberDocs> Members { get; } = [];
}

// the part of PackageParser that reads OCCT's documentation comments (netocc-gen docs)
internal sealed partial class PackageParser
{
  /// <summary>
  /// The documentation comments in a package's headers, by type: the C++ name without template
  /// arguments (the name OCCT's reference manual documents), a namespace for its functions, a
  /// typedef's own name. A parse of its own, without function bodies; a header whose error is fatal
  /// (a missing include, which hides the headers after it) is left out, and a package none of whose
  /// headers compiles fails, as for the generator.
  /// </summary>
  public Dictionary<string, TypeDocs> ParseDocs(string package, IReadOnlyList<string> headers,
                                                IReadOnlyList<string> prelude,
                                                IReadOnlyList<string> aliases)
  {
    List<string> own = [.. headers, .. aliases];
    (string Header, string Reason)? first = null;
    while (own.Count > 0)
    {
      var fileName = $"netocc_docs_{package}.cpp";
      List<string> included = [.. prelude, .. own];
      using var index = CXIndex.Create();
      using var unsaved =
        CXUnsavedFile.Create(
          fileName, string.Concat(included.Select(header => $"#include <{header}>\n")));
      var error = CXTranslationUnit.TryParse(index, fileName, Arguments(), [unsaved],
                                             CXTranslationUnit_Flags
                                               .CXTranslationUnit_IncludeAttributedTypes
                                             | CXTranslationUnit_Flags
                                               .CXTranslationUnit_DetailedPreprocessingRecord
                                             | CXTranslationUnit_Flags
                                               .CXTranslationUnit_SkipFunctionBodies,
                                             out var handle);
      if (error != CXErrorCode.CXError_Success)
      {
        throw new InvalidOperationException($"{package}: libclang failed to parse ({error})");
      }

      using var unit = TranslationUnit.GetOrCreate(handle);
      var fatal = FatalError(package, handle, Errors(handle), fileName, included, own);
      if (fatal is var (culprit, reason))
      {
        first ??= (culprit, reason);
        own.Remove(culprit);
        continue;
      }

      Dictionary<string, TypeDocs> docs = new(StringComparer.Ordinal);
      CollectDocs(unit.TranslationUnitDecl.Decls,
                  new HashSet<string>(own, StringComparer.OrdinalIgnoreCase), docs);
      return docs;
    }

    if (first is var (header, why))
    {
      throw new InvalidOperationException($"{package}: no header compiles, e.g. {header}: {why}");
    }

    return [];
  }

  // NCollection_UBTree<int, Bnd_Box>::TreeNode as NCollection_UBTree::TreeNode
  internal static string WithoutTemplateArguments(string spelling)
  {
    var text = new StringBuilder();
    var depth = 0;
    foreach (var c in spelling)
    {
      depth += c switch { '<' => 1, '>' => -1, _ => 0 };
      if (depth == 0 && c != '>')
      {
        text.Append(c);
      }
    }

    return text.ToString();
  }

  // the documented declarations of the package's own headers
  private static void CollectDocs(IEnumerable<Decl> decls, HashSet<string> headers,
                                  Dictionary<string, TypeDocs> docs)
  {
    foreach (var decl in decls)
    {
      switch (decl)
      {
        case NamespaceDecl scope:
          CollectDocs(scope.Decls, headers, docs);
          break;
        case LinkageSpecDecl linkage:
          CollectDocs(linkage.Decls, headers, docs);
          break;
        case ClassTemplateDecl
        {
          TemplatedDecl: CXXRecordDecl { IsThisDeclarationADefinition: true } pattern
        } template when IsIn(template, headers):
          RecordDocs(pattern, template.Handle.ParsedComment, docs);
          break;
        case CXXRecordDecl { IsThisDeclarationADefinition: true } record
          when record is not ClassTemplateSpecializationDecl && IsIn(record, headers):
          RecordDocs(record, record.Handle.ParsedComment, docs);
          break;
        case EnumDecl e when IsIn(e, headers):
          EnumDocs(e, docs);
          break;
        case FunctionDecl { DeclContext: NamespaceDecl scope } function when IsIn(function, headers)
          && DocComments.ToXml(function.Handle.ParsedComment) is { } xml:
          Of(docs, scope.QualifiedName)
            .Members.Add(new MemberDocs(function.Name, ParameterNames(function), xml));
          break;
        // an alias's own comment (a collection's, TColgp_Array1OfPnt): the type C# names after it
        case TypedefNameDecl { DeclContext: TranslationUnitDecl } typedef
          when IsIn(typedef, headers) && DocComments.ToXml(typedef.Handle.ParsedComment) is { } xml:
          Of(docs, typedef.Name).Xml ??= xml;
          break;
      }
    }
  }

  // a class, its public members, and the classes and enums inside it
  private static void RecordDocs(CXXRecordDecl record, CXComment comment,
                                 Dictionary<string, TypeDocs> docs)
  {
    var type = Of(docs, WithoutTemplateArguments(record.QualifiedName));
    type.Xml ??= DocComments.ToXml(comment);
    foreach (var decl in record.Decls)
    {
      if (decl.Access is not (CX_CXXAccessSpecifier.CX_CXXPublic
          or CX_CXXAccessSpecifier.CX_CXXInvalidAccessSpecifier))
      {
        continue;
      }

      switch (decl)
      {
        case CXXMethodDecl method when DocComments.ToXml(method.Handle.ParsedComment) is { } xml:
          type.Members.Add(new MemberDocs(method is CXXConstructorDecl ? "#ctor" : method.Name,
                                          ParameterNames(method), xml));
          break;
        case FieldDecl field when DocComments.ToXml(field.Handle.ParsedComment) is { } xml:
          type.Members.Add(new MemberDocs(field.Name, [], xml));
          break;
        case ClassTemplateDecl
        {
          TemplatedDecl: CXXRecordDecl { IsThisDeclarationADefinition: true } pattern
        } template:
          RecordDocs(pattern, template.Handle.ParsedComment, docs);
          break;
        case CXXRecordDecl { IsThisDeclarationADefinition: true } nested
          when nested is not ClassTemplateSpecializationDecl && nested.Name.Length > 0:
          RecordDocs(nested, nested.Handle.ParsedComment, docs);
          break;
        case EnumDecl e:
          EnumDocs(e, docs);
          break;
      }
    }
  }

  private static void EnumDocs(EnumDecl e, Dictionary<string, TypeDocs> docs)
  {
    if (e.Name.Length == 0)
    {
      return;
    }

    var type = Of(docs, WithoutTemplateArguments(e.QualifiedName));
    type.Xml ??= DocComments.ToXml(e.Handle.ParsedComment);
    foreach (var constant in e.Enumerators)
    {
      if (DocComments.ToXml(constant.Handle.ParsedComment) is { } xml)
      {
        type.Members.Add(new MemberDocs(constant.Name, [], xml));
      }
    }
  }

  private static TypeDocs Of(Dictionary<string, TypeDocs> docs, string name)
  {
    if (!docs.TryGetValue(name, out var type))
    {
      docs[name] = type = new TypeDocs();
    }

    return type;
  }

  // declared in one of these headers (a macro expansion counts where it is written)
  private static bool IsIn(Decl decl, HashSet<string> headers)
  {
    decl.Location.GetFileLocation(out var file, out _, out _, out _);
    return headers.Contains(Path.GetFileName(Consume(file.Name)));
  }
}
