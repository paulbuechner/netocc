// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Linq;

//
using NetOcc.Generator.Mapping;
using NetOcc.Generator.Model;

namespace NetOcc.Generator.Tests;

/// <summary>Model types and a registry for tests, spelled like the C++ they stand for.</summary>
internal static class Types
{
  /// <summary>A copyable class, which C# gets as a value class (TopoDS_Shape).</summary>
  public static readonly ClassTraits ValueTraits =
    new(IsTransient: false, IsAbstract: false, HasPublicDestructor: true, IsCopyable: true);

  /// <summary>A handle-managed class (Geom_Surface).</summary>
  public static readonly ClassTraits TransientTraits =
    new(IsTransient: true, IsAbstract: false, HasPublicDestructor: true, IsCopyable: false);

  public static NamedType Class(string name) => new(name, NamedKind.Class, []);

  public static NamedType Enum(string name) => new(name, NamedKind.Enum, []);

  public static NamedType Handle(string name) => HandleOf(Class(name));

  public static NamedType HandleOf(NamedType target) =>
    new("opencascade::handle", NamedKind.Class, [target]);

  public static NamedType Template(string name, string argument) => Template(name, Class(argument));

  public static NamedType Template(string name, params CppType[] arguments) =>
    new(name, NamedKind.Class, arguments);

  public static NamedType ShapeList => Template("NCollection_List", "TopoDS_Shape");

  /// <summary>
  /// <c>Standard_OStream&amp;</c> as libclang spells it: <c>std::basic_ostream&lt;char,
  /// std::char_traits&lt;char&gt;&gt;&amp;</c>.
  /// </summary>
  public static ReferenceType OStream => Ref(Template("std::basic_ostream", Builtin("char"),
                                                      Template("std::char_traits",
                                                               Builtin("char"))));

  /// <summary>
  /// NCollection_DataMap&lt;TopoDS_Shape, NCollection_List&lt;TopoDS_Shape&gt;,
  /// TopTools_ShapeMapHasher&gt;.
  /// </summary>
  public static NamedType ShapeHistory => Template("NCollection_DataMap", Class("TopoDS_Shape"),
                                                   ShapeList, Class("TopTools_ShapeMapHasher"));

  /// <summary>An OCCT alias for a collection instantiation.</summary>
  public static KnownInstantiation Collection(string template, CppType element, string alias,
                                              string package) =>
    Collection(template, [element], alias, package);

  public static KnownInstantiation Collection(string template, CppType[] arguments, string alias,
                                              string package) =>
    new(CollectionTemplate.Named(template)!, [.. arguments.Select(a => a.Spelling)], alias, package,
        arguments);

  public static BuiltinType Builtin(string name) => new(name);

  public static CppType Const(CppType type) => type switch
  {
    BuiltinType b => b with { Const = true },
    NamedType n => n with { Const = true },
    PointerType p => p with { Const = true },
    _ => type,
  };

  public static ReferenceType Ref(CppType type) => new(type);

  public static PointerType Pointer(CppType type) => new(type);

  /// <summary>
  /// gp_Pnt (value type), TopoDS_Shape (value class), Geom_Surface (transient), a plain abstract
  /// class, an enum, and the aliases of a list of shapes, a map from shapes to such lists, and an
  /// array of reals with its handle-managed variant.
  /// </summary>
  public static TypeRegistry Registry()
  {
    var registry = new TypeRegistry();
    registry.AddClass(new KnownClass("Standard_Transient", "Standard", WrapKind.Transient));
    registry.AddClass(new KnownClass("gp_Pnt", "gp", WrapKind.ValueType));
    registry.AddClass(new KnownClass("TopoDS_Shape", "TopoDS", WrapKind.ValueClass));
    registry.AddClass(new KnownClass("Geom_Surface", "Geom", WrapKind.Transient));
    registry.AddClass(new KnownClass("BRepBuilderAPI_MakeShape", "BRepBuilderAPI", WrapKind.Plain));
    registry.AddEnum("TopAbs_ShapeEnum", "TopAbs");
    registry.AddInstantiation(Collection("NCollection_List", Class("TopoDS_Shape"),
                                         "TopTools_ListOfShape", "TopTools"));
    registry.AddInstantiation(Collection("NCollection_DataMap", [
      Class("TopoDS_Shape"), ShapeList, Class("TopTools_ShapeMapHasher")
    ], "TopTools_DataMapOfShapeListOfShape", "TopTools"));
    registry.AddInstantiation(Collection("NCollection_Array1", Builtin("double"),
                                         "TColStd_Array1OfReal", "TColStd"));
    registry.AddInstantiation(Collection("NCollection_HArray1", Builtin("double"),
                                         "TColStd_HArray1OfReal", "TColStd"));
    return registry;
  }
}
