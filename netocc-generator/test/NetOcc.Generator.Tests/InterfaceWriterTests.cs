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

/// <summary>Declarations the writer emits for a synthetic package "Demo".</summary>
[TestFixture]
public class InterfaceWriterTests
{
  private TypeRegistry _registry = null!;

  [SetUp]
  public void CreateRegistry() => _registry = Registry();

  [Test]
  public void Write_GivesEnumsExplicitValues()
  {
    // Arrange
    var package = Package(enums:
    [
      new EnumModel("Demo_Kind", "Demo_Kind.hxx",
                    [new EnumConstant("Demo_A", 1), new EnumConstant("Demo_B", 4)])
    ]);

    // Act
    var module = Write(package);

    // Assert
    Assert.That(module.Interface,
                Does.Contain("enum Demo_Kind {\n  Demo_A = 1,\n  Demo_B = 4,\n};"));
  }

  [Test]
  public void Write_DeclaresHandleTypemapsBeforeAnyClass()
  {
    // Arrange (Demo_Owner returns a handle of Demo_Part, which comes later in the package)
    var owner = new ClassModel("Demo_Owner", "Demo_Owner.hxx", ["Standard_Transient"],
                               TransientTraits, [], [Method("Part", Handle("Demo_Part"))]);
    var part = new ClassModel("Demo_Part", "Demo_Part.hxx", ["Standard_Transient"], TransientTraits,
                              [], []);

    // Act
    var module = Write(Package(classes: [owner, part]));

    // Assert
    var text = module.Interface;
    using (Assert.EnterMultipleScope())
    {
      Assert.That(text.IndexOf("%occt_transient(Demo_Part)", StringComparison.Ordinal),
                  Is.LessThan(text.IndexOf("class Demo_Owner", StringComparison.Ordinal)));
      Assert.That(text, Does.Contain("opencascade::handle<Demo_Part> Part();"));
    }
  }

  [Test]
  public void Write_DerivesFromTheNearestWrappedAncestor()
  {
    // Arrange (TDataStd_GenericEmpty isn't wrapped; the proxy must still be a transient)
    var tool = new ClassModel("Demo_Tool", "Demo_Tool.hxx",
                              ["TDataStd_GenericEmpty", "TDF_Attribute", "Standard_Transient"],
                              TransientTraits, [], []);

    // Act
    var module = Write(Package(classes: [tool]));

    // Assert
    Assert.That(module.Interface, Does.Contain("class Demo_Tool : public Standard_Transient {"));
  }

  [Test]
  public void Write_DropsTrailingProgressRangeDefaults()
  {
    // Arrange
    var perform = Method("Perform", Builtin("void"),
                         new ParameterModel("theShape", Ref(Const(Class("TopoDS_Shape"))), null),
                         new ParameterModel("theRange", Ref(Const(Class("Message_ProgressRange"))),
                                            "Message_ProgressRange()"));

    // Act
    var module = Write(Package(classes: [ValueClass("Demo_Algo", perform)]));

    // Assert
    Assert.That(module.Interface, Does.Contain("  void Perform(const TopoDS_Shape& theShape);"));
  }

  [Test]
  public void Write_CoversOverloadsThatCollideInCSharp()
  {
    // Arrange (const char* and TCollection_AsciiString are both UTF-8 strings in C#: one call,
    // which reaches the first)
    var byPointer = Method("Load", Builtin("bool"),
                           new ParameterModel("theName", Pointer(Const(Builtin("char"))), null));
    var byString = Method("Load", Builtin("bool"),
                          new ParameterModel("theName",
                                             Ref(Const(Class("TCollection_AsciiString"))), null));

    // Act
    var module = Write(Package(classes: [ValueClass("Demo_Store", byPointer, byString)]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("bool Load(const char* theName);"));
      Assert.That(module.Interface, Does.Not.Contain("TCollection_AsciiString& theName"));
      Assert.That(module.Skipped, Is.Empty);
    }
  }

  [Test]
  public void Write_PrefersTheNonConstTwin()
  {
    // Arrange (its ref return reads as well as writes)
    var read = Method("Value", Ref(Const(Builtin("double")))) with { IsConst = true };
    var write = Method("Value", Ref(Builtin("double")));

    // Act
    var module = Write(Package(classes: [ValueClass("Demo_Cell", read, write)]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("  double& Value();"));
      Assert.That(module.Interface, Does.Not.Contain("Value() const;"));
    }
  }

  [Test]
  public void Write_KeepsTheFirstTwinReturningAClass()
  {
    // Arrange (neither an owned copy nor a borrowed proxy is safe for every class: declaration
    // order decides)
    var write = Method("Value", Ref(Class("TopoDS_Shape")));
    var read = Method("Value", Ref(Const(Class("TopoDS_Shape")))) with { IsConst = true };

    // Act
    var module = Write(Package(classes: [ValueClass("Demo_Iterator", write, read)]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("  TopoDS_Shape& Value();"));
      Assert.That(module.Interface, Does.Not.Contain("Value() const;"));
    }
  }

  [Test]
  public void Write_PrefersTheUtf16StringOverload()
  {
    // Arrange (a C# string reaches OCCT exactly as UTF-16; a const char* overload may copy its
    // UTF-8 bytes as Latin-1)
    var narrow = Method("Load", Builtin("bool"),
                        new ParameterModel("theName", Pointer(Const(Builtin("char"))), null));
    var wide = Method("Load", Builtin("bool"),
                      new ParameterModel("theName", Ref(Const(Class("TCollection_ExtendedString"))),
                                         null));

    // Act
    var module = Write(Package(classes: [ValueClass("Demo_Store", narrow, wide)]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface,
                  Does.Contain("bool Load(const TCollection_ExtendedString& theName);"));
      Assert.That(module.Interface, Does.Not.Contain("const char* theName"));
    }
  }

  [Test]
  public void Write_KeepsTheDefaultsPastAClaimedOverload()
  {
    // Arrange (Send(string) is the UTF-16 overload's; the other keeps its two-argument call)
    var wide = Method("Send", Builtin("void"),
                      new ParameterModel("theText", Ref(Const(Class("TCollection_ExtendedString"))),
                                         null));
    var narrow = Method("Send", Builtin("void"),
                        new ParameterModel("theText", Pointer(Const(Builtin("char"))), null),
                        new ParameterModel("theLevel", Builtin("int"), "0"));

    // Act
    var module = Write(Package(classes: [ValueClass("Demo_Log", narrow, wide)]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface,
                  Does.Contain("  void Send(const char* theText, int theLevel);"));
      Assert.That(module.Interface,
                  Does.Contain("  void Send(const TCollection_ExtendedString& theText);"));
      Assert.That(module.Skipped, Is.Empty);
    }
  }

  [Test]
  public void Write_WrapsExceptionClassesAsClasses()
  {
    // Arrange (OCCT 8's exceptions are copyable classes; thrown ones still reach C# as
    // OcctException)
    var root = new ClassModel("Standard_Failure", "Standard_Failure.hxx", ["std::exception"],
                              ValueTraits, [], []);
    var failure = new ClassModel("Demo_Failure", "Demo_Failure.hxx",
                                 ["Standard_Failure", "std::exception"], ValueTraits, [], []);

    // Act
    var module = Write(Package(classes: [root, failure]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("class Standard_Failure {"));
      Assert.That(module.Interface, Does.Contain("class Demo_Failure : public Standard_Failure {"));
      Assert.That(module.Skipped, Is.Empty);
    }
  }

  [Test]
  public void Write_ImportsWhatItUsesWithTheirDependencies()
  {
    // Arrange
    var move = Method("Move", Builtin("void"),
                      new ParameterModel("theTarget", Ref(Const(Class("gp_Pnt"))), null));
    Dictionary<string, IReadOnlyList<string>> imports = new() { ["gp"] = ["Standard"] };

    // Act
    var module = Write(Package(classes: [ValueClass("Demo_Mover", move)]), imports);

    // Assert
    Assert.That(module.Imports, Is.EqualTo(new[] { "Standard", "gp" }));
  }

  [Test]
  public void Write_ImportsThePackagesItsDependenciesDeriveFromFirst()
  {
    // Arrange (Left and Right use each other, and Right_Shape derives from Left_Shape: SWIG must
    // meet Left first)
    _registry.AddClass(new KnownClass("Left_Shape", "Left", WrapKind.ValueClass));
    _registry.AddClass(new KnownClass("Right_Shape", "Right", WrapKind.ValueClass));
    var fit = Method("Fit", Builtin("void"),
                     new ParameterModel("theRight", Ref(Const(Class("Right_Shape"))), null),
                     new ParameterModel("theLeft", Ref(Const(Class("Left_Shape"))), null));
    Dictionary<string, IReadOnlyList<string>> imports = new()
      { ["Left"] = ["Right"], ["Right"] = ["Left"] };
    Dictionary<string, IReadOnlyCollection<string>> bases = new() { ["Right"] = ["Left"] };

    // Act
    var module = Write(Package(classes: [ValueClass("Demo_Fitter", fit)]), imports, bases);

    // Assert (without the bases, the cycle puts Right first)
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Imports, Is.EqualTo(new[] { "Left", "Right" }));
      Assert.That(module.Interface,
                  Does.Contain(
                    "#ifndef SWIGIMPORTED\n%import \"Left.i\"\n%import \"Right.i\"\n#endif\n"),
                  "a module SWIG imports doesn't import again");
    }
  }

  [Test]
  public void Write_RecordsThePackagesItsClassesDeriveFrom()
  {
    // Arrange
    var maker = new ClassModel("Demo_Maker", "Demo_Maker.hxx", ["BRepBuilderAPI_MakeShape"],
                               ValueTraits, [], []);

    // Act
    var module = Write(Package(classes: [maker]));

    // Assert
    Assert.That(module.BasePackages, Is.EqualTo(new[] { "BRepBuilderAPI" }));
  }

  [Test]
  public void Write_IncludesDependencyHeadersBeforeThePackagesOwn()
  {
    // Arrange
    var move = Method("Move", Builtin("void"),
                      new ParameterModel("theTarget", Ref(Const(Class("gp_Pnt"))), null));

    // Act
    var header = Write(Package(classes: [ValueClass("Demo_Mover", move)])).ModuleHeader;

    // Assert
    Assert.That(header.IndexOf("#include <gp_Pnt.hxx>", StringComparison.Ordinal),
                Is.Not.Negative.And.LessThan(
                  header.IndexOf("#include <Demo.hxx>", StringComparison.Ordinal)),
                "OCCT headers that use a type without including it still compile");
  }

  [Test]
  public void Write_InstantiatesTheRequestedCollectionsOfItsAliases()
  {
    // Arrange (a signature used the handle-managed array, which needs its base; nothing used the
    // list)
    _registry.AddInstantiation(Collection("NCollection_List", Builtin("int"),
                                          "TColStd_ListOfInteger", "TColStd"));
    _registry.Request(_registry.Instantiation(Template("NCollection_HArray1", Builtin("double")))!);
    var values = ValueClass("TColStd_Values",
                            Method("Values",
                                   Ref(Const(Template("NCollection_Array1", Builtin("double"))))));
    var package = new PackageModel("TColStd", ["TColStd_HArray1OfReal.hxx"], [], [values], [], []);

    // Act
    var module = Write(package);

    // Assert
    var text = module.Interface;
    using (Assert.EnterMultipleScope())
    {
      Assert.That(
        text, Does.Contain("%occt_array1_blittable(TColStd_Array1OfReal, double, double)"),
        "an array of numbers gets bulk copies");
      Assert.That(text.IndexOf("%occt_array1_blittable", StringComparison.Ordinal),
                  Is.LessThan(text.IndexOf("%occt_harray1(TColStd_HArray1OfReal, double, double)",
                                           StringComparison.Ordinal)), "the base comes first");
      Assert.That(text, Does.Not.Contain("TColStd_ListOfInteger"));
      Assert.That(text.IndexOf("%occt_harray1", StringComparison.Ordinal),
                  Is.LessThan(text.IndexOf("class TColStd_Values", StringComparison.Ordinal)),
                  "SWIG applies the typemaps (Values' copy) to the declarations after them");
      Assert.That(module.Imports, Does.Contain("Standard"),
                  "the handle typemaps take Standard_Transient");
    }
  }

  [Test]
  public void Write_InstantiatesACollectionAfterTheOnesItHolds()
  {
    // Arrange (the map holds lists; by name it would come first)
    _registry.Request(_registry.Instantiation(ShapeHistory)!);
    _registry.Request(_registry.Instantiation(ShapeList)!);
    var package = new PackageModel("TopTools", ["TopTools.hxx"], [], [], [], []);

    // Act
    var text = Write(package).Interface;

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(
        text,
        Does.Contain(
          "%occt_datamap(TopTools_DataMapOfShapeListOfShape, TopoDS_Shape, NCollection_List<TopoDS_Shape>, TopTools_ShapeMapHasher, TopoDS_Shape, TopTools_ListOfShape)"),
        "the hasher has no C# type");
      Assert.That(text.IndexOf("%occt_list(TopTools_ListOfShape", StringComparison.Ordinal),
                  Is.LessThan(text.IndexOf("%occt_datamap", StringComparison.Ordinal)));
    }
  }

  [Test]
  public void Write_ReportsTheCollectionsItsMembersUse()
  {
    // Arrange
    var perform = Method("Perform", Builtin("void"),
                         new ParameterModel("theShapes",
                                            Ref(Const(Template("NCollection_List",
                                                               "TopoDS_Shape"))), null));

    // Act
    var module = Write(Package(classes: [ValueClass("Demo_Algo", perform)]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Collections?.Select(c => c.Alias),
                  Is.EqualTo(new[] { "TopTools_ListOfShape" }));
      Assert.That(module.Imports, Does.Contain("TopTools"));
      Assert.That(module.Interface,
                  Does.Contain("  void Perform(const NCollection_List<TopoDS_Shape>& theShapes);"));
    }
  }

  [Test]
  public void Write_LendsStreamsForTheCallOnly()
  {
    // Arrange (a constructor, or a class that holds a stream, may keep the stream)
    ParameterModel stream = new("theStream", OStream, null);
    var dumper = new ClassModel("Demo_Dumper", "Demo_Dumper.hxx", [], ValueTraits,
                                [new ConstructorModel([stream], false)],
                                [Method("Dump", Builtin("void"), stream)]);
    var printer = new ClassModel("Demo_Printer", "Demo_Printer.hxx", [], ValueTraits, [],
                                 [Method("Print", Builtin("void"), stream)],
                                 Held: [Pointer(OStream.Referee)]);

    // Act
    var module = Write(Package(classes: [dumper, printer]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("  void Dump(std::ostream& theStream);"));
      Assert.That(module.Skipped,
                  Does.Contain(
                    "Demo_Dumper::Demo_Dumper: parameter theStream: a constructor may keep the stream, which C# lends for the call only"));
      Assert.That(module.Skipped,
                  Does.Contain(
                    "Demo_Printer::Print: parameter theStream: the class may keep the stream, which C# lends for the call only"));
    }
  }

  [Test]
  public void Write_OrdersImportsAndIncludesWhateverTheMembersOrder()
  {
    // Arrange (two members using the types of two other packages, declared in either order)
    var move = Method("Move", Builtin("void"),
                      new ParameterModel("theTarget", Ref(Const(Class("gp_Pnt"))), null));
    var fill = Method("Fill", Builtin("void"),
                      new ParameterModel("theSurface", Ref(Const(Handle("Geom_Surface"))), null));
    Dictionary<string, IReadOnlyList<string>> imports = new()
      { ["gp"] = ["Standard"], ["Geom"] = ["Standard"] };

    // Act
    var first = Write(Package(classes: [ValueClass("Demo_Mover", move, fill)]), imports);
    var second = Write(Package(classes: [ValueClass("Demo_Mover", fill, move)]), imports);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(first.Imports, Is.EqualTo(new[] { "Standard", "Geom", "gp" }));
      Assert.That(second.Imports, Is.EqualTo(first.Imports));
      Assert.That(second.ModuleHeader, Is.EqualTo(first.ModuleHeader));
    }
  }

  [Test]
  public void Write_PointersTheCalleeMayKeep_AreAddressesTypemappedBeforeTheClasses()
  {
    // Arrange (a constructor or setter may keep the pointer; a C# array is pinned for the call
    // only)
    var values = new ParameterModel("theValues", Pointer(Const(Builtin("double"))), null);
    var buffer = new ClassModel("Demo_Buffer", "Demo_Buffer.hxx", [], ValueTraits,
                                [new ConstructorModel([values], false)], [
                                  Method("SetValues", Builtin("void"), values),
                                  Method("Sum", Builtin("double"), values)
                                ]);

    // Act
    var text = Write(Package(classes: [buffer])).Interface;

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(text, Does.Contain("  Demo_Buffer(NetOcc_Address< const double* > theValues);"));
      Assert.That(
        text, Does.Contain("  void SetValues(NetOcc_Address< const double* > theValues);"));
      Assert.That(text, Does.Contain("  double Sum(const double* theValues);"));
      Assert.That(text.IndexOf("%netocc_address(%arg(const double*))", StringComparison.Ordinal),
                  Is.LessThan(text.IndexOf("class Demo_Buffer", StringComparison.Ordinal)));
    }
  }

  [Test]
  public void Write_MoveOverloadWithACopyTwin_IsCoveredNotSkipped()
  {
    // Arrange (C# has no moves: its call reaches the const& overload)
    var byCopy = Method("SetShape", Builtin("void"),
                        new ParameterModel("theShape", Ref(Const(Class("TopoDS_Shape"))), null));
    var byMove = Method("SetShape", Builtin("void"),
                        new ParameterModel("theShape",
                                           new ReferenceType(Class("TopoDS_Shape"), IsRValue: true),
                                           null));

    // Act
    var module = Write(Package(classes: [ValueClass("Demo_Holder", byCopy, byMove)]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("  void SetShape(const TopoDS_Shape& theShape);"));
      Assert.That(module.Skipped, Is.Empty);
    }
  }

  [Test]
  public void Write_MarksADeprecatedOverloadObsolete()
  {
    // Arrange (SWIG matches the feature by name, parameters, defaults and const: the other overload
    // stays unmarked)
    var shape = ValueClass("Demo_Shape",
                           Method("Reversed", Class("Demo_Shape")) with { IsConst = true }, Method(
                               "Reversed", Builtin("void"),
                               new ParameterModel("theResult", Ref(Class("Demo_Shape")), null),
                               new ParameterModel("theDeep", Builtin("bool"), "false")) with
                             {
                               IsConst = true, IsDeprecated = true
                             });

    // Act
    var module = Write(Package(classes: [shape]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface,
                  Does.Contain("  Demo_Shape Reversed() const;\n"
                               + "  %csattributes Reversed(Demo_Shape& theResult, bool theDeep = false) const \"[global::System.Obsolete(\\\"Deprecated in OCCT.\\\")]\";\n"
                               + "  void Reversed(Demo_Shape& theResult, bool theDeep = false) const;"));
      Assert.That(module.Skipped, Is.Empty);
    }
  }

  [Test]
  public void Write_MarksADeprecatedClassObsolete()
  {
    // Arrange (the class typemap: %csattributes on the class name would mark its constructor)
    var old = ValueClass("Demo_Old") with { IsDeprecated = true };

    // Act
    var module = Write(Package(classes: [old]));

    // Assert
    Assert.That(module.Interface,
                Does.Contain(
                  "%typemap(csattributes) Demo_Old \"[global::System.Obsolete(\\\"Deprecated in OCCT.\\\")]\""));
  }

  [Test]
  public void Write_KeepsGetType_LeavesOutDispose()
  {
    // Arrange (OCCT's GetType hides object's, which isn't virtual; Dispose is the proxy's own)
    var adaptor = ValueClass("Demo_Adaptor",
                             Method("GetType", Enum("TopAbs_ShapeEnum")) with { IsConst = true },
                             Method("Dispose", Builtin("void")));

    // Act
    var module = Write(Package(classes: [adaptor]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("  TopAbs_ShapeEnum GetType() const;"));
      Assert.That(module.Skipped,
                  Is.EqualTo(new[] { "Demo_Adaptor::Dispose: name clashes with the C# proxy" }));
    }
  }

  [Test]
  public void Write_KeepsTheCallsOnlyOneOverloadTakes()
  {
    // Arrange (one argument fits both, as in C++: each keeps the calls it alone takes, (), (int,
    // int))
    var byIncrement =
      new ConstructorModel([new ParameterModel("theIncrement", Builtin("int"), "256")],
                           IsDeprecated: false);
    var byLength = new ConstructorModel([
      new ParameterModel("theLength", Builtin("int"), null),
      new ParameterModel("theIncrement", Builtin("int"), "256")
    ], IsDeprecated: false);
    var array = new ClassModel("Demo_Array", "Demo_Array.hxx", [], ValueTraits,
                               [byIncrement, byLength], []);

    // Act
    var module = Write(Package(classes: [array]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface,
                  Does.Contain("  Demo_Array();\n  Demo_Array(int theLength, int theIncrement);"));
      Assert.That(module.Skipped, Is.Empty);
    }
  }

  [Test]
  public void Write_ReturnsANonCopyableTransientThroughAnAccessor()
  {
    // Arrange (constructed in place from the returned value; its proxy owns a reference)
    var owner = ValueClass("Demo_Owner",
                           Method("Copy", Class("Standard_Transient")) with { IsConst = true });

    // Act
    var module = Write(Package(classes: [owner]));

    // Assert
    Assert.That(module.Interface,
                Does.Contain("  %rename(Copy) NetOcc_Copy;\n  %extend {\n"
                             + "    Standard_Transient* const NetOcc_Copy() const { return new Standard_Transient($self->Copy()); }\n  }"));
  }

  [Test]
  public void Write_KeepsTheArgumentsAConstructorRefersTo()
  {
    // Arrange (Demo_Iterator holds a reference to its shape; the point is a struct, a copy for the
    // call)
    var iterator = new ClassModel("Demo_Iterator", "Demo_Iterator.hxx", [], ValueTraits, [
                                    new ConstructorModel([
                                      new ParameterModel("theShape",
                                                         Ref(Const(Class("TopoDS_Shape"))), null),
                                      new ParameterModel("thePoint", Ref(Const(Class("gp_Pnt"))),
                                                         null)
                                    ], IsDeprecated: false)
                                  ], [], Held:
                                  [
                                    Ref(Const(Class("TopoDS_Shape"))),
                                    Pointer(Const(Class("gp_Pnt")))
                                  ]);

    // Act
    var module = Write(Package(classes: [iterator]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("%netocc_keep_construct(Demo_Iterator)"));
      Assert.That(module.Interface,
                  Does.Contain("  %apply SWIGTYPE & NETOCC_KEEP { const TopoDS_Shape& theShape };\n"
                               + "  Demo_Iterator(const TopoDS_Shape& theShape, const gp_Pnt& thePoint);\n  %clear const TopoDS_Shape& theShape;"));
    }
  }

  [Test]
  public void Write_KeepsTheArgumentsAMemberRefersTo()
  {
    // Arrange (Initialize stores the shape; a const member can't, and a struct argument is a copy
    // for the call)
    var extrema = ValueClass("Demo_Extrema",
                             Method("Initialize", Builtin("void"),
                                    new ParameterModel("theShape",
                                                       Ref(Const(Class("TopoDS_Shape"))), null),
                                    new ParameterModel("thePoint", Ref(Const(Class("gp_Pnt"))),
                                                       null)), Method(
                                 "Contains", Builtin("bool"),
                                 new ParameterModel("theShape", Ref(Const(Class("TopoDS_Shape"))),
                                                    null)) with
                               {
                                 IsConst = true
                               }) with
    {
      Held = [Pointer(Const(Class("TopoDS_Shape")))]
    };

    // Act
    var module = Write(Package(classes: [extrema]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface,
                  Does.Contain(
                    "  %netocc_keep_argument(Initialize.0, const TopoDS_Shape& theShape)\n"
                    + "  void Initialize(const TopoDS_Shape& theShape, const gp_Pnt& thePoint);\n  %clear const TopoDS_Shape& theShape;"));
      Assert.That(module.Interface, Does.Not.Contain("%netocc_keep_argument(Contains"));
      Assert.That(module.Interface, Does.Not.Contain("%netocc_keep_construct"));
    }
  }

  [Test]
  public void Write_KeepsTheObjectAViewReturnRefersTo()
  {
    // Arrange (Demo_View refers to its graph, and so does Demo_Range, whose destructor may use it;
    // Demo_Label refers to nothing; a static member has no object to keep)
    var view = new ClassModel("Demo_View", "Demo_View.hxx", [], ValueTraits, [], [],
                              Held: [Pointer(Class("Demo_Graph"))]);
    var range = new ClassModel("Demo_Range", "Demo_Range.hxx", [],
                               ValueTraits with { DeclaresDestructor = true }, [], [],
                               Held: [Pointer(Class("Demo_Graph"))]);
    var label = new ClassModel("Demo_Label", "Demo_Label.hxx", [], ValueTraits, [], []);
    var graph = ValueClass("Demo_Graph",
                           Method("View", Ref(Const(Class("Demo_View")))) with { IsConst = true },
                           Method("Copy", Class("Demo_View")) with { IsConst = true },
                           Method("Next", Class("Demo_Range")),
                           Method("Label", Ref(Const(Class("Demo_Label")))) with { IsConst = true },
                           Method("Empty", Class("Demo_View")) with { IsStatic = true });

    // Act
    var module = Write(Package(classes: [view, range, label, graph]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface,
                  Does.Contain("  %apply const SWIGTYPE & NETOCC_VIEW { const Demo_View& View };\n"
                               + "  const Demo_View& View() const;\n  %clear const Demo_View& View;"));
      Assert.That(module.Interface,
                  Does.Contain("  %apply SWIGTYPE NETOCC_VIEW { Demo_View Copy };\n"
                               + "  Demo_View Copy() const;\n  %clear Demo_View Copy;"));
      Assert.That(module.Interface,
                  Does.Contain("  %apply SWIGTYPE NETOCC_VIEW_HELD { Demo_Range Next };\n"
                               + "  Demo_Range Next();\n  %clear Demo_Range Next;"));
      Assert.That(module.Interface, Does.Not.Contain("Demo_Label& Label }"));
      Assert.That(module.Interface, Does.Not.Contain("Demo_View Empty }"));
    }
  }

  [Test]
  public void Write_KeepsTheObjectAMovedResultRefersTo()
  {
    // Arrange (Demo_Guard, move-only, refers to its graph and releases it in its destructor; its
    // const& return is a borrowing proxy, which keeps the object already)
    var guard = new ClassModel("Demo_Guard", "Demo_Guard.hxx", [], ValueTraits with
    {
      IsCopyable = false, IsMovable = true, DeclaresDestructor = true
    }, [], [], Held: [Pointer(Class("Demo_Graph"))]);
    var graph = ValueClass("Demo_Graph", Method("Edit", Class("Demo_Guard")),
                           Method("Current", Ref(Const(Class("Demo_Guard")))) with
                           {
                             IsConst = true
                           });

    // Act
    var module = Write(Package(classes: [guard, graph]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface,
                  Does.Contain(
                    "  %apply SWIGTYPE NETOCC_VIEW_HELD { Demo_Guard Edit };\n  Demo_Guard Edit();\n  %clear Demo_Guard Edit;"));
      Assert.That(module.Interface, Does.Contain("  const Demo_Guard& Current() const;"));
      Assert.That(module.Interface, Does.Not.Contain("Demo_Guard& Current }"));
    }
  }

  [Test]
  public void Write_KeepsNoStandardLibraryArgument()
  {
    // Arrange (a std::string is a C# string, which has no proxy to keep)
    var name = Template("std::basic_string", Builtin("char"));
    var label = new ClassModel("Demo_Label", "Demo_Label.hxx", [], ValueTraits, [
      new ConstructorModel([new ParameterModel("theName", Ref(Const(name)), null)],
                           IsDeprecated: false)
    ], [], Held: [Ref(Const(name))]);

    // Act
    var module = Write(Package(classes: [label]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("  Demo_Label(const std::string& theName);"));
      Assert.That(module.Interface, Does.Not.Contain("NETOCC_KEEP"));
    }
  }

  [Test]
  public void Write_LeavesOutAMemberAStaticOverloadShadows()
  {
    // Arrange (SWIG passes the object as the first argument: Multiply(m) collides with static
    // Multiply(a, b))
    var matrix = ValueClass("Demo_Mat",
                            Method("Multiply", Builtin("void"),
                                   new ParameterModel("theOther", Ref(Const(Class("Demo_Mat"))),
                                                      null)), Method(
                                "Multiply", Class("Demo_Mat"),
                                new ParameterModel("theA", Ref(Const(Class("Demo_Mat"))), null),
                                new ParameterModel("theB", Ref(Const(Class("Demo_Mat"))), null))
                              with
                              {
                                IsStatic = true
                              });

    // Act
    var module = Write(Package(classes: [matrix]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface,
                  Does.Contain(
                    "static Demo_Mat Multiply(const Demo_Mat& theA, const Demo_Mat& theB);"));
      Assert.That(module.Interface, Does.Not.Contain("void Multiply("));
      Assert.That(module.Skipped,
                  Has.One.StartsWith("Demo_Mat::Multiply: a static overload takes the object"));
    }
  }

  [Test]
  public void Write_ComparesStaticOverloadsWithoutConstAndReferences()
  {
    // Arrange (by value and by const& are one type to SWIG: the wrappers still collide)
    var matrix = ValueClass("Demo_Mat",
                            Method("Multiply", Builtin("void"),
                                   new ParameterModel("theOther", Class("Demo_Mat"), null)), Method(
                                "Multiply", Class("Demo_Mat"),
                                new ParameterModel("theA", Ref(Const(Class("Demo_Mat"))), null),
                                new ParameterModel("theB", Ref(Const(Class("Demo_Mat"))), null))
                              with
                              {
                                IsStatic = true
                              });

    // Act
    var module = Write(Package(classes: [matrix]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Not.Contain("void Multiply("));
      Assert.That(module.Skipped,
                  Has.One.StartsWith("Demo_Mat::Multiply: a static overload takes the object"));
    }
  }

  [Test]
  public void Write_KeepsAMemberWhoseStaticOverloadTakesAHandle()
  {
    // Arrange (Message_ProgressIndicator::Start() beside static Start(handle): SWIG tells a handle
    // from the object)
    var progress = new ClassModel("Demo_Progress", "Demo_Progress.hxx", ["Standard_Transient"],
                                  TransientTraits, [], [
                                    Method("Start", Builtin("int")),
                                    Method("Start", Builtin("int"),
                                           new ParameterModel(
                                             "theProgress", Ref(Const(Handle("Demo_Progress"))),
                                             null)) with
                                    {
                                      IsStatic = true
                                    }
                                  ]);

    // Act
    var module = Write(Package(classes: [progress]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("  int Start();"));
      Assert.That(module.Interface,
                  Does.Contain(
                    "  static int Start(const opencascade::handle<Demo_Progress>& theProgress);"));
    }
  }

  [Test]
  public void Write_KeepsAMemberWhoseStaticOverloadIsLeftOut()
  {
    // Arrange (the static overload doesn't link, so nothing shadows the instance member)
    var matrix = ValueClass("Demo_Mat",
                            Method("Multiply", Builtin("void"),
                                   new ParameterModel("theOther", Ref(Const(Class("Demo_Mat"))),
                                                      null)), Method(
                                "Multiply", Class("Demo_Mat"),
                                new ParameterModel("theA", Ref(Const(Class("Demo_Mat"))), null),
                                new ParameterModel("theB", Ref(Const(Class("Demo_Mat"))), null))
                              with
                              {
                                IsStatic = true, IsCallable = false
                              });

    // Act
    var module = Write(Package(classes: [matrix]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("  void Multiply(const Demo_Mat& theOther);"));
      Assert.That(module.Skipped, Has.None.Contains("a static overload takes the object"));
    }
  }

  [Test]
  public void Write_CopiesConstReturnsOfAnEmptyValueClass()
  {
    // Arrange (no constructors or members: C# gets it only from a static member's const& return, as
    // a copy)
    var order = ValueClass("Demo_Order");

    // Act
    var module = Write(Package(classes: [order]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("%occt_valueclass(Demo_Order)"),
                  "the copy typemaps");
      Assert.That(module.Interface, Does.Contain("class Demo_Order {\npublic:\n};"));
      Assert.That(module.Skipped, Is.Empty);
    }
  }

  [Test]
  public void Write_CoversLegacyHandleClasses()
  {
    // Arrange (OCCT's Handle_T classes: in C#, T's proxy is the handle)
    var legacy = ValueClass("Handle_Demo_Part");

    // Act
    var module = Write(Package(classes: [legacy]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Not.Contain("Handle_Demo_Part"));
      Assert.That(module.Skipped, Is.Empty);
    }
  }

  [Test]
  public void Write_EnumeratesARangeForClass()
  {
    // Arrange (OCCT 8's range-for: C# enumerates with the class's own More(), Next() and Value(),
    // which it declares too)
    var explorer = ValueClass("Demo_Explorer",
                              Method("begin",
                                     Template("NCollection_ForwardRangeIterator", "Demo_Explorer")),
                              Method("end", Class("NCollection_ForwardRangeSentinel")),
                              Method("More", Builtin("bool")) with { IsConst = true },
                              Method("Next", Builtin("void")),
                              Method("Value", Ref(Const(Class("TopoDS_Shape")))) with
                              {
                                IsConst = true
                              });

    // Act
    var module = Write(Package(classes: [explorer]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface,
                  Does.Contain("%occt_forward_range(Demo_Explorer, TopoDS_Shape, Value)"));
      Assert.That(module.Interface, Does.Contain(" Value() const;"));
      Assert.That(module.Interface, Does.Not.Contain("begin()"));
      Assert.That(module.Skipped, Is.Empty);
    }
  }

  [Test]
  public void Write_LogsTheRangeForMembersOfAClassWhoseNextIsExcluded()
  {
    // Arrange (the macro would call a Next() the proxy doesn't have)
    var explorer = ValueClass("Demo_Explorer",
                              Method("begin",
                                     Template("NCollection_ForwardRangeIterator", "Demo_Explorer")),
                              Method("end", Class("NCollection_ForwardRangeSentinel")),
                              Method("More", Builtin("bool")) with { IsConst = true },
                              Method("Next", Builtin("void")),
                              Method("Value", Ref(Const(Class("TopoDS_Shape")))) with
                              {
                                IsConst = true
                              });
    var config = new PackageConfig { ExcludeMethods = ["Demo_Explorer::Next"] };

    // Act
    var module = Write(Package(classes: [explorer]), config: config);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Not.Contain("%occt_forward_range"));
      Assert.That(module.Skipped, Is.EqualTo(new[]
      {
        "Demo_Explorer::begin: range-for needs More() and Next(), which the class doesn't wrap",
        "Demo_Explorer::end: range-for needs More() and Next(), which the class doesn't wrap",
        "Demo_Explorer::Next: excluded in the config",
      }));
    }
  }

  [Test]
  public void Write_LogsANamespaceWhoseCSharpNameAClassHas()
  {
    // Arrange (namespace Demo_Tools's static class would be Demo_Tools, the name of another
    // package's class)
    _registry.AddClass(new KnownClass("Demo_Tools", "Other", WrapKind.ValueClass));
    var package = new PackageModel("Demo", ["Demo.hxx"], [], [], [], [
      new FunctionModel("Demo_Tools", "Scale", Builtin("double"), [], IsDeprecated: false)
    ]);

    // Act
    var module = Write(package);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Skipped, Is.EqualTo(new[]
      {
        "namespace Demo_Tools: class Demo_Tools (Other) has its C# name"
      }));
      Assert.That(module.Namespaces, Is.Empty);
      Assert.That(module.Interface, Does.Not.Contain("NetOcc_Demo_Tools"));
    }
  }

  [Test]
  public void Write_CreatesNoObjectItsProxyCouldntDelete()
  {
    // Arrange (a protected destructor: the proxy couldn't release what a constructor made)
    var scope = new ClassModel("Demo_Scope", "Demo_Scope.hxx", [],
                               ValueTraits with { HasPublicDestructor = false },
                               [new ConstructorModel([], IsDeprecated: false)],
                               [Method("Depth", Builtin("int")) with { IsConst = true }]);

    // Act
    var module = Write(Package(classes: [scope]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("%nodefaultdtor Demo_Scope;"));
      Assert.That(module.Interface,
                  Does.Contain("class Demo_Scope {\npublic:\n  int Depth() const;\n};"));
      Assert.That(module.Skipped, Is.EqualTo(new[]
      {
        "Demo_Scope::Demo_Scope: the destructor isn't public or doesn't link, so C# couldn't release the object"
      }));
    }
  }

  [Test]
  public void Write_IncludesThePreludesFirst()
  {
    // Arrange (Demo's headers need Demo_Macro.hxx first, and gp's gp_Macro.hxx: a module using gp
    // needs both)
    var move = Method("Move", Builtin("void"),
                      new ParameterModel("theTarget", Ref(Const(Class("gp_Pnt"))), null));
    var config = new PackageConfig { Prelude = ["Demo_Macro.hxx"] };
    Dictionary<string, IReadOnlyList<string>> preludes = new() { ["gp"] = ["gp_Macro.hxx"] };

    // Act
    var header = Write(Package(classes: [ValueClass("Demo_Mover", move)]), config: config,
                       preludes: preludes)
      .ModuleHeader;

    // Assert
    Assert.That(
      header,
      Does.EndWith(
        "#pragma once\n#include <Demo_Macro.hxx>\n#include <gp_Macro.hxx>\n#include <gp_Pnt.hxx>\n#include <Demo.hxx>\n"));
  }

  [Test]
  public void Write_KeepsAnArgumentWhoseBaseTheObjectRefersTo()
  {
    // Arrange (Demo_Walker holds a Demo_Graph, and a Demo_SubGraph is one)
    var graph = ValueClass("Demo_Graph");
    var subGraph = new ClassModel("Demo_SubGraph", "Demo_SubGraph.hxx", ["Demo_Graph"], ValueTraits,
                                  [], []);
    var walker = new ClassModel("Demo_Walker", "Demo_Walker.hxx", [], ValueTraits, [
      new ConstructorModel(
        [new ParameterModel("theGraph", Ref(Const(Class("Demo_SubGraph"))), null)],
        IsDeprecated: false)
    ], [], Held: [Pointer(Const(Class("Demo_Graph")))]);

    // Act
    var module = Write(Package(classes: [graph, subGraph, walker]));

    // Assert
    Assert.That(module.Interface,
                Does.Contain("  %apply SWIGTYPE & NETOCC_KEEP { const Demo_SubGraph& theGraph };\n"
                             + "  Demo_Walker(const Demo_SubGraph& theGraph);\n  %clear const Demo_SubGraph& theGraph;"));
  }

  // the package's classes registered as Generate registers them
  private GeneratedModule Write(PackageModel package,
                                Dictionary<string, IReadOnlyList<string>>? imports = null,
                                Dictionary<string, IReadOnlyCollection<string>>? bases = null,
                                PackageConfig? config = null,
                                Dictionary<string, IReadOnlyList<string>>? preludes = null)
  {
    foreach (var c in package.Classes)
    {
      _registry.AddClass(new KnownClass(c.Name, package.Name, ClassRules.KindOf(c), c.Header,
                                        c.Traits.HasDefaultConstructor, ClassRules.IsMoveOnly(c)));
    }

    var writer = new InterfaceWriter(_registry, new SignatureMapper(_registry), "8.0.1",
                                     preludeOf: p => preludes?.GetValueOrDefault(p) ?? [],
                                     classOf: name =>
                                       package.Classes.FirstOrDefault(c => c.Name == name));
    return writer.Write(package, config ?? new PackageConfig(),
                        p => imports?.GetValueOrDefault(p) ?? [],
                        basesOf: bases is null ? null : p => bases.GetValueOrDefault(p) ?? []);
  }

  private static PackageModel Package(List<EnumModel>? enums = null,
                                      List<ClassModel>? classes = null) =>
    new("Demo", ["Demo.hxx"], enums ?? [], classes ?? [], [], []);

  private static ClassModel ValueClass(string name, params MethodModel[] methods) =>
    new(name, $"{name}.hxx", [], ValueTraits, [], methods);

  private static MethodModel Method(string name, CppType returns,
                                    params ParameterModel[] parameters) =>
    new(name, returns, parameters, IsStatic: false, IsConst: false, IsVirtual: false,
        IsDeprecated: false);
}
