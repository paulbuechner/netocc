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
using ClangType = ClangSharp.Type;

namespace NetOcc.Generator.Parsing;

internal sealed partial class PackageParser
{
  // the Collector's link checks: whether the shim can call a function (the export tables, the
  // inline bodies, the vtables)
  private sealed partial class Collector
  {
    private bool IsCallable(FunctionDecl function, bool classExported) =>
      Unlinked(function, classExported) is null;

    /// <summary>
    /// A class whose vtable the libraries don't export while they export its virtual functions
    /// (<c>Standard_EXPORT</c>): where MSVC doesn't emit the vtable in the shim (an inline
    /// delegating constructor), it doesn't link. Windows only: the export tables tell.
    /// </summary>
    private bool IsVtableUnexported(CXXRecordDecl r) =>
      exports is not null
      && r is not ClassTemplateSpecializationDecl
      && r.Methods.Any(m => m.IsVirtual && !m.IsPure && IsExportedSymbol(m))
      && VtableSymbol(r) is { } vtable
      && !exports.Contains(vtable);

    // MSVC's name of a class's vtable, from a constructor's: ??0<class>@@... is ??_7<class>@@6B@
    private static string? VtableSymbol(CXXRecordDecl r) =>
      r.Ctors.SelectMany(Manglings)
        .Where(m => m.StartsWith("??0", StringComparison.Ordinal))
        .Select(m => m.IndexOf("@@", 3, StringComparison.Ordinal) is var end and > 3
                  ? $"??_7{m[3..(end + 2)]}6B@"
                  : null)
        .FirstOrDefault(v => v is not null);

    /// <summary>
    /// The virtual function an instance's vtable names that doesn't link, where the libraries don't
    /// export the class: a check parse instantiates every member of a class template instance,
    /// whose bodies then name their callees.
    /// </summary>
    public string? UnlinkedVtable(CXXRecordDecl r) => IsExported(r) ? null : UnlinkedVirtual(r);

    // the vtable of a class names the final overrider of every virtual function: the first one (not
    // pure) that doesn't link, or null. The destructor has its own check (HasPublicDestructor). A
    // class without virtual functions has none.
    private string? UnlinkedVirtual(CXXRecordDecl r)
    {
      HashSet<CXCursor> overridden = [];

      string? Visit(CXXRecordDecl record)
      {
        foreach (var method in record.Methods.Where(m => m.IsVirtual && m is not CXXDestructorDecl))
        {
          var isFinal = !overridden.Contains(method.Handle);
          foreach (var overriddenMethod in method.OverriddenMethods)
          {
            overridden.Add(overriddenMethod.Handle);
          }

          if (isFinal && !method.IsPure && Unlinked(method, IsExported(record)) is { } unlinked)
          {
            return unlinked;
          }
        }

        foreach (var b in record.Bases)
        {
          if (BaseDefinition(b) is { } baseDecl && Visit(baseDecl) is { } unlinked)
          {
            return unlinked;
          }
        }

        return null;
      }

      return Visit(r);
    }

    // what a call from the shim needs that doesn't link, or null: callable are a pure virtual
    // (through the vtable), a function exported from the OCCT libraries, and one defined in a
    // header (inline, or in an .lxx) whose calls link. With the export tables the symbol itself is
    // looked up: OCCT declares a few members Standard_EXPORT and never defines them, and
    // StepFile_ReadData's inline destructor calls ClearRecorder, which isn't exported. Without, the
    // attribute has to do (on Windows OCCT 8's Standard_EXPORT is __declspec(dllexport) on both
    // sides).
    private string? Unlinked(FunctionDecl function, bool classExported)
    {
      if (function.IsPure)
      {
        return null;
      }

      if (function.Definition is { } definition)
      {
        return exports is null ? null : UnlinkedInBody(definition);
      }

      // a member of BVH_PrimitiveSet<double, 3> nobody instantiated yet ("using
      // BVH_PrimitiveSet3d::Box;"): the shim instantiates it where its template defines it in the
      // headers; otherwise the libraries must export it
      if (IsTemplateInstance(function)
          && (function.TemplateInstantiationPattern ?? function.InstantiatedFromMemberFunction) is
          not { Definition: null })
      {
        return null;
      }

      return exports is null
        ?
        classExported || IsExported(function) ? null : QualifiedName(function)
        : IsExportedSymbol(function)
          ? null
          : QualifiedName(function);
    }

    // the first OCCT function an inline definition calls that doesn't link (system and third-party
    // calls link on their own): a callee defined in the headers counts with its own calls, a pure
    // or virtual one goes through the vtable, a template instance is instantiated in the shim,
    // anything else must be exported
    private string? UnlinkedInBody(FunctionDecl definition) => UnlinkedInBody(definition, out _);

    // lowest: the shallowest search still running that the answer relied on (a call cycle),
    // int.MaxValue for none. Such an answer ("links") may change once that search ends, so it's
    // cached only then; a callee that doesn't link is final at once
    private string? UnlinkedInBody(FunctionDecl definition, out int lowest)
    {
      lowest = int.MaxValue;
      if (_unlinkedInBody.TryGetValue(definition.Handle, out var known))
      {
        return known;
      }

      if (_searching.TryGetValue(definition.Handle, out var running))
      {
        lowest = running;
        return null;
      }

      var depth = _searching.Count;
      _searching[definition.Handle] = depth;
      string? unlinked = null;
      foreach (var callee in Callees(definition.Body))
      {
        if (!IsOcct(callee))
        {
          continue;
        }

        var relied = int.MaxValue;
        unlinked = callee switch
        {
          { IsPure: true } or CXXMethodDecl { IsVirtual: true } => null,
          { Definition: { } calleeDefinition } => UnlinkedInBody(calleeDefinition, out relied),
          _ when IsTemplateInstance(callee) => null,
          _ => IsExportedSymbol(callee) ? null : QualifiedName(callee),
        };
        lowest = Math.Min(lowest, relied);
        if (unlinked is not null)
        {
          break;
        }
      }

      _searching.Remove(definition.Handle);
      if (unlinked is not null || lowest >= depth)
      {
        _unlinkedInBody[definition.Handle] = unlinked;
        lowest = int.MaxValue;
      }

      return unlinked;
    }

    // a function template instance or a member of a class template specialization: the shim
    // instantiates it
    private static bool IsTemplateInstance(FunctionDecl function) =>
      function.TemplateSpecializationKind != CX_TemplateSpecializationKind.CX_TSK_Undeclared
      || function.InstantiatedFromMemberFunction is not null
      || function.DeclContext is ClassTemplateSpecializationDecl;

    private static IEnumerable<FunctionDecl> Callees(Stmt? body)
    {
      if (body is null)
      {
        yield break;
      }

      Stack<Stmt> pending = new([body]);
      while (pending.TryPop(out var stmt))
      {
        switch (stmt)
        {
          case CallExpr { DirectCallee: { } callee }:
            yield return callee;
            break;
          case CXXConstructExpr { Constructor: { } constructor }:
            yield return constructor;
            break;
        }

        foreach (var child in stmt.Children)
        {
          if (child is not null)
          {
            pending.Push(child);
          }
        }
      }
    }

    // declared in an OCCT header (the include directory, .hxx or .lxx)
    private bool IsOcct(Decl function)
    {
      function.Location.GetFileLocation(out var file, out _, out _, out _);
      var path = Consume(file.Name);
      if (path.Length == 0)
      {
        return false;
      }

      if (!_occtFiles.TryGetValue(path, out var occt))
      {
        occt = _occtFiles[path] = Path.GetFullPath(path)
          .StartsWith(occtInclude, StringComparison.OrdinalIgnoreCase);
      }

      return occt;
    }

    private static string QualifiedName(FunctionDecl function) =>
      function is CXXMethodDecl { Parent: { } owner }
        ? $"{owner.Name}::{function.Name}"
        : function.Name;

    private bool IsExportedSymbol(FunctionDecl function) =>
      Manglings(function).Any(exports!.Contains);

    // the symbols a call can link to. Constructors and destructors have several; for an MSVC
    // destructor getMangling gives the ??_D "vbase destructor", while the DLLs export the ??1 one.
    private static unsafe List<string> Manglings(FunctionDecl function)
    {
      if (function is CXXConstructorDecl or CXXDestructorDecl
          && function.Handle.CXXManglings is var set
          && set != null)
      {
        List<string> names = [];
        for (var i = 0; i < set->Count; i++)
        {
          names.Add(set->Strings[i].ToString());
        }

        clang.disposeStringSet(set);
        return names;
      }

      using var mangling = function.Handle.Mangling;
      return [mangling.ToString()];
    }

    private static bool IsExported(Decl decl) =>
      decl.Attrs.Any(a => a.Kind is CX_AttrKind.CX_AttrKind_DLLExport
                       or CX_AttrKind.CX_AttrKind_DLLImport);
  }
}
