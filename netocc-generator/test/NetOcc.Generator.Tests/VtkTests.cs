// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

// NUnit
using NUnit.Framework;

//
using NetOcc.Generator.Config;
using NetOcc.Generator.Emit;
using NetOcc.Generator.Mapping;
using NetOcc.Generator.Model;

// static usings
using static NetOcc.Generator.Tests.Types;

namespace NetOcc.Generator.Tests;

/// <summary>
/// Modules on NetVtk (NetOcc.Vtk's, config/vtk.yaml): NetVtk's objects through NetVtkInterop.i,
/// and classes deriving from VTK's.
/// </summary>
[TestFixture]
public class VtkTests
{
  private TypeRegistry _registry = null!;

  [SetUp]
  public void CreateRegistry()
  {
    _registry = Registry();
    foreach (var name in new[]
               { "vtkObjectBase", "vtkRenderer", "vtkLookupTable", "vtkPolyDataAlgorithm" })
    {
      _registry.AddClass(
        new KnownClass(name, TypeRegistry.NetVtkPackage, WrapKind.Vtk, $"{name}.h"));
    }
  }

  [Test]
  public void Parameter_NetVtkObject_IsItsProxy()
  {
    // Arrange
    var mapper = new SignatureMapper(_registry);

    // Act
    var mapped = mapper.Parameter(Pointer(Class("vtkRenderer")));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("vtkRenderer*"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("global::Vtk.vtkRenderer"));
      Assert.That(mapped.Type?.Typemap, Is.EqualTo("%netvtk_object(vtkRenderer)"));
      Assert.That(mapped.Type?.Uses,
                  Is.EqualTo(new[] { new TypeUse(TypeRegistry.NetVtkPackage, "vtkRenderer.h") }));
    }
  }

  [Test]
  public void Return_NetVtkObject_GetsAReference()
  {
    // Arrange (T* const: the wrapper adds a reference for the proxy, NetVtkInterop.i)
    var mapper = new SignatureMapper(_registry);

    // Act
    var mapped = mapper.Return(Pointer(Class("vtkRenderer")));

    // Assert
    Assert.That(mapped.Type?.Spelling, Is.EqualTo("vtkRenderer* const"));
  }

  [Test]
  public void Return_SmartPointerOfNetVtkObject_IsItsProxy()
  {
    // Arrange
    var mapper = new SignatureMapper(_registry);

    // Act
    var mapped = mapper.Return(Template("vtkSmartPointer", "vtkLookupTable"));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("vtkSmartPointer<vtkLookupTable>"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("global::Vtk.vtkLookupTable"));
    }
  }

  [Test]
  public void Parameter_NetVtkObjectByReference_IsSkipped()
  {
    // Arrange
    var mapper = new SignatureMapper(_registry);

    // Act
    var mapped = mapper.Parameter(Ref(Class("vtkRenderer")));

    // Assert
    Assert.That(mapped.Skip, Does.Contain("a VTK object by value or reference"));
  }

  [Test]
  public void Write_DerivesAVtkClassFromNetVtksProxy()
  {
    // Arrange (as IVtkTools_ShapeDataSource: a vtkPolyDataAlgorithm, New(), vtkTypeMacro's members)
    var source = new ClassModel("Demo_Source", "Demo_Source.hxx", [
      "vtkPolyDataAlgorithm", "vtkAlgorithm", "vtkObject", "vtkObjectBase"
    ], TransientTraits with { IsTransient = false }, [], [
      Method("New", Pointer(Class("Demo_Source"))) with { IsStatic = true },
      Method("NewInstance", Pointer(Class("Demo_Source"))) with
      {
        IsConst = true
      },
      Method("SafeDownCast", Pointer(Class("Demo_Source")),
             new ParameterModel("o", Pointer(Class("vtkObjectBase")), null)) with
      {
        IsStatic = true
      },
      Method("SetRenderer", Builtin("void"),
             new ParameterModel("theRenderer", Pointer(Class("vtkRenderer")), null)),
    ]);

    // Act
    var module = Write(source);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("%include \"NetVtkInterop.i\""));
      Assert.That(module.Interface,
                  Does.Contain("%netvtk_derived(Demo_Source, global::Vtk.vtkPolyDataAlgorithm)"));
      Assert.That(module.Interface, Does.Contain("%netvtk_new(Demo_Source)"));
      Assert.That(module.Interface, Does.Contain("%netvtk_object(vtkRenderer)"));
      Assert.That(module.Interface, Does.Contain("class Demo_Source {\npublic:\n"));
      Assert.That(module.Interface,
                  Does.Contain("  static Demo_Source* const SafeDownCast(vtkObjectBase* o);"));
      Assert.That(module.Interface, Does.Contain("  void SetRenderer(vtkRenderer* theRenderer);"));
      Assert.That(module.Interface, Does.Not.Contain("New("));
      Assert.That(module.Interface, Does.Not.Contain("%occt_transient"));
      Assert.That(module.Imports, Does.Not.Contain(TypeRegistry.NetVtkPackage));
      Assert.That(module.ModuleHeader, Does.Contain("#include <vtkRenderer.h>"));
    }
  }

  [Test]
  public void Write_DerivesFromAVtkClassOfTheRunByItsName()
  {
    // Arrange (as IVtkTools_DisplayModeFilter : IVtkTools_SubPolyDataFilter)
    string[] filter = ["vtkPolyDataAlgorithm", "vtkAlgorithm", "vtkObject", "vtkObjectBase"];
    var traits = TransientTraits with { IsTransient = false };
    var subFilter = new ClassModel("Demo_SubFilter", "Demo_SubFilter.hxx", filter, traits, [], []);
    var displayFilter = new ClassModel("Demo_DisplayFilter", "Demo_DisplayFilter.hxx",
                                       ["Demo_SubFilter", .. filter], traits, [], []);

    // Act
    var module = Write(subFilter, displayFilter);

    // Assert
    Assert.That(module.Interface,
                Does.Contain("%netvtk_derived(Demo_DisplayFilter, Demo_SubFilter)"));
  }

  private GeneratedModule Write(params ClassModel[] classes)
  {
    var package = new PackageModel("Demo", ["Demo.hxx"], [], [.. classes], [], []);
    foreach (var c in classes)
    {
      _registry.AddClass(new KnownClass(c.Name, package.Name, ClassRules.KindOf(c), c.Header,
                                        c.Traits.HasDefaultConstructor, ClassRules.IsMoveOnly(c)));
    }

    var writer = new InterfaceWriter(_registry, new SignatureMapper(_registry), "8.0.1",
                                     classOf: name => classes.FirstOrDefault(c => c.Name == name));
    return writer.Write(package, new PackageConfig(), _ => []);
  }

  private static MethodModel Method(string name, CppType returns,
                                    params ParameterModel[] parameters) =>
    new(name, returns, parameters, IsStatic: false, IsConst: false, IsVirtual: false,
        IsDeprecated: false);
}
