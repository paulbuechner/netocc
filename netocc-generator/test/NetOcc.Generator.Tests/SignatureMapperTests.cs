// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Linq;

// NUnit
using NUnit.Framework;

//
using NetOcc.Generator.Mapping;
using NetOcc.Generator.Model;

// static usings
using static NetOcc.Generator.Tests.Types;

namespace NetOcc.Generator.Tests;

/// <summary>C++ signature types to netocc-core's contract, or a skip reason.</summary>
[TestFixture]
public class SignatureMapperTests
{
  private SignatureMapper _mapper = null!;

  [SetUp]
  public void CreateMapper() => _mapper = new SignatureMapper(Registry());

  [Test]
  public void Parameter_ConstReferenceToValueType_StaysAReference()
  {
    // Arrange
    var type = Ref(Const(Class("gp_Pnt")));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("const gp_Pnt&"));
      Assert.That(mapped.Type?.CsKey, Is.EqualTo("gp_Pnt"));
      Assert.That(mapped.Type?.Uses, Is.EqualTo(new[] { new TypeUse("gp", "gp_Pnt.hxx") }));
    }
  }

  [Test]
  public void Parameter_MutableReferenceToValueType_IsRef()
  {
    // Arrange
    var type = Ref(Class("gp_Pnt"));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    Assert.That(mapped.Type?.CsKey, Is.EqualTo("ref gp_Pnt"));
  }

  [Test]
  public void Parameter_HandleOfTransient_MapsToItsProxy()
  {
    // Arrange
    var type = Ref(Const(Handle("Geom_Surface")));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("const opencascade::handle<Geom_Surface>&"));
      Assert.That(mapped.Type?.CsKey, Is.EqualTo("Geom_Surface"));
      Assert.That(mapped.Type?.Uses.Select(u => u.Package), Is.EqualTo(new[] { "Geom" }));
    }
  }

  [Test]
  public void Parameter_HandleOfUnwrappedClass_IsSkipped()
  {
    // Arrange
    var type = Ref(Const(Handle("Standard_Type")));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type, Is.Null);
      Assert.That(mapped.Skip, Does.Contain("unwrapped"));
    }
  }

  [TestCase("const char*")]
  [TestCase("const TCollection_AsciiString&")]
  [TestCase("TCollection_ExtendedString")]
  public void Parameter_Strings_BecomeCSharpStrings(string spelling)
  {
    // Arrange
    CppType type = spelling switch
    {
      "const char*" => Pointer(Const(Builtin("char"))),
      "const TCollection_AsciiString&" => Ref(Const(Class("TCollection_AsciiString"))),
      _ => Class("TCollection_ExtendedString"),
    };

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo(spelling));
      Assert.That(mapped.Type?.CsKey, Is.EqualTo("string"));
    }
  }

  [Test]
  public void Parameter_AliasedCollection_UsesTheAlias()
  {
    // Arrange
    var type = Ref(Const(Template("NCollection_List", "TopoDS_Shape")));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.CsKey, Is.EqualTo("TopTools_ListOfShape"));
      Assert.That(mapped.Type?.Uses.Select(u => (u.Package, u.Header, u.Collection?.Alias)),
                  Does.Contain(("TopTools", "NCollection_List.hxx", "TopTools_ListOfShape")),
                  "the alias's package instantiates it");
      Assert.That(mapped.Type?.Uses.Select(u => u.Package), Does.Contain("TopoDS"),
                  "the element's module");
    }
  }

  [Test]
  public void Parameter_MapOfCollections_UsesBothAliases()
  {
    // Arrange
    var type = Ref(Const(ShapeHistory));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.CsType, Is.EqualTo("TopTools_DataMapOfShapeListOfShape"));
      Assert.That(mapped.Type?.Uses.Select(u => u.Collection?.Alias).OfType<string>(),
                  Is.EquivalentTo(new[]
                  {
                    "TopTools_DataMapOfShapeListOfShape", "TopTools_ListOfShape"
                  }), "the item's collection is used too");
    }
  }

  [Test]
  public void Parameter_OStreamReference_IsAStream()
  {
    // Arrange
    var type = OStream;

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("std::ostream&"),
                  "the spelling Streams.i's typemaps match");
      Assert.That(mapped.Type?.CsType, Is.EqualTo("global::System.IO.Stream"));
    }
  }

  [Test]
  public void Return_TheStreamItWasGiven_IsVoid()
  {
    // Arrange (Standard_OStream& Print(Standard_OStream& theStream))
    ParameterModel[] parameters = [new("theStream", OStream, null)];

    // Act
    var chained = _mapper.Return(OStream, parameters);
    var alone = _mapper.Return(OStream, []);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(chained.Type?.Spelling, Is.EqualTo("void"));
      Assert.That(alone.Skip, Does.StartWith("returns a mutable reference"),
                  "a stream the object keeps");
    }
  }

  [TestCase("double", "ref double")]
  [TestCase("gp_Pnt", "ref gp_Pnt")]
  [TestCase("TopoDS_Shape", "TopoDS_Shape")]
  [TestCase("Handle(Geom_Surface)", "Geom_Surface")]
  [TestCase("BRepBuilderAPI_MakeShape", "void")]
  public void Return_MutableReferenceOfAMember_Maps(string referee, string csType)
  {
    // Arrange (a member of BRepBuilderAPI_MakeShape; its own class comes back for chaining)
    var type = Ref(referee switch
    {
      "double" => Builtin("double"),
      "Handle(Geom_Surface)" => Handle("Geom_Surface"),
      _ => Class(referee),
    });

    // Act
    var member = _mapper.Return(type, [], owner: "BRepBuilderAPI_MakeShape");
    var alone = _mapper.Return(type, []);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(member.Type?.CsType, Is.EqualTo(csType),
                  "a C# ref, a borrowing proxy, the handle, or void");
      Assert.That(alone.Skip, Does.StartWith("returns a mutable reference"),
                  "a static function has no object to borrow from");
    }
  }

  [Test]
  public void Parameter_CollectionWithoutAlias_GetsANameOfItsOwn()
  {
    // Arrange
    var type = Ref(Const(Template("NCollection_Sequence", Builtin("double"))));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.CsType, Is.EqualTo("NCollection_Sequence_double"));
      Assert.That(mapped.Type?.Uses[0].Package, Is.EqualTo(TypeRegistry.Unowned),
                  "the first writer pass finds its first user");
    }
  }

  [Test]
  public void Parameter_HandleOfHandleManagedCollection_UsesTheAlias()
  {
    // Arrange
    var type = Ref(Const(HandleOf(Template("NCollection_HArray1", Builtin("double")))));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling,
                  Is.EqualTo("const opencascade::handle<NCollection_HArray1<double>>&"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("TColStd_HArray1OfReal"));
    }
  }

  [Test]
  public void Parameter_HandleManagedCollectionByMutableReference_IsSkipped()
  {
    // Arrange
    var type = Ref(Template("NCollection_HArray1", Builtin("double")));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    Assert.That(mapped.Skip, Does.Contain("handle-managed"));
  }

  [Test]
  public void Return_HandleManagedCollectionByReference_IsSkipped()
  {
    // Arrange
    var type = Ref(Const(Template("NCollection_HArray1", Builtin("double"))));

    // Act
    var mapped = _mapper.Return(type);

    // Assert
    Assert.That(mapped.Skip,
                Is.EqualTo("returns NCollection_HArray1<double>&, which is handle-managed"));
  }

  [TestCase("double", "double", true)]
  [TestCase("gp_Pnt", "gp_Pnt", true)]
  [TestCase("TopoDS_Shape", "TopoDS_Shape", false)]
  [TestCase("Handle(Geom_Surface)", "Geom_Surface", false)]
  [TestCase("TCollection_AsciiString", "string", false)]
  [TestCase("TopAbs_ShapeEnum", "TopAbs_ShapeEnum", false)]
  public void Element_MapsToItsCSharpType(string element, string csType, bool blittable)
  {
    // Arrange
    CppType type = element switch
    {
      "double" => Builtin("double"),
      "Handle(Geom_Surface)" => Handle("Geom_Surface"),
      "TopAbs_ShapeEnum" => Enum("TopAbs_ShapeEnum"),
      _ => Class(element),
    };

    // Act
    var (mapped, _) = _mapper.Element(type, defaultConstructed: true);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped?.CsType, Is.EqualTo(csType));
      Assert.That(mapped?.IsBlittable, Is.EqualTo(blittable),
                  "bulk copies for numbers and structs");
    }
  }

  [TestCase("BRepBuilderAPI_MakeShape", "isn't copyable")]
  [TestCase("Geom_Surface", "a transient held by value")]
  [TestCase("unsigned long long", "spelled differently per platform")]
  public void Element_ThatNoCollectionHolds_IsSkipped(string element, string reason)
  {
    // Arrange
    CppType type = element.Contains(' ') ? Builtin(element) : Class(element);

    // Act
    var (mapped, skip) = _mapper.Element(type, defaultConstructed: false);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped, Is.Null);
      Assert.That(skip, Does.Contain(reason));
    }
  }

  [TestCase("wchar_t", "fixed-width")]
  public void Parameter_PlatformWidthBuiltin_IsSkipped(string builtin, string reason)
  {
    // Arrange
    var type = Builtin(builtin);

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    Assert.That(mapped.Skip, Does.Contain(reason));
  }

  [Test]
  public void Parameter_CLong_IsACsLongKeyedAsLongLong()
  {
    // Arrange (32 bits on Windows, 64 elsewhere: a C# long, range-checked in Types.i; C# can't tell
    // it from long long)
    var type = Ref(Builtin("long"));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("long&"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("ref long"));
      Assert.That(mapped.Type?.CsKey, Is.EqualTo("ref long long"));
    }
  }

  [Test]
  public void Parameter_Char_IsItsByte()
  {
    // Arrange (its sign differs per platform, and a C# char would pass through a code page)
    var type = Builtin("char");

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("char"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("byte"));
      Assert.That(mapped.Type?.CsKey, Is.EqualTo("unsigned char"),
                  "C# can't tell it from unsigned char");
    }
  }

  [Test]
  public void Parameter_CodePoint_IsAUint()
  {
    // Arrange (char32_t: UTF-32, 32 bits everywhere)
    var type = Builtin("char32_t");

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.CsType, Is.EqualTo("uint"));
      Assert.That(mapped.Type?.CsKey, Is.EqualTo("unsigned int"),
                  "C# can't tell it from unsigned int");
    }
  }

  [Test]
  public void Parameter_WindowHandle_KeepsItsName()
  {
    // Arrange (Aspect_Drawable is void* on Windows, unsigned long on X11: no one type converts to
    // both)
    var type = new BuiltinType("intptr_t", false, "Aspect_Drawable");

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("Aspect_Drawable"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("global::System.IntPtr"));
    }
  }

  [Test]
  public void Parameter_PointerSizedNumber_IsKeyedAsAnAddress()
  {
    // Arrange (C# can't overload F(void*) with F(intptr_t): both take an IntPtr)
    var number = new BuiltinType("intptr_t", false, "Aspect_Drawable");

    // Act
    var (mappedNumber, mappedAddress) = (_mapper.Parameter(number),
      _mapper.Parameter(Pointer(Builtin("void"))));

    // Assert
    Assert.That(mappedNumber.Type?.CsKey, Is.EqualTo(mappedAddress.Type?.CsKey));
  }

  [Test]
  public void Parameter_PointerToAWindowHandle_IsAnAddress()
  {
    // Arrange (Types.i has arrays of intptr_t, but none of a type that is an integer on X11)
    var type = Pointer(new BuiltinType("intptr_t", false, "Aspect_Drawable"));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("NetOcc_Address< Aspect_Drawable* >"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("global::System.IntPtr"));
    }
  }

  [Test]
  public void Return_SizeReferenceOfAMember_IsARefUIntPtr()
  {
    // Arrange (size_t is four bytes on win-x86)
    var type = Ref(Builtin("size_t"));

    // Act
    var mapped = _mapper.Return(type, [], "Demo_Stats");

    // Assert
    Assert.That(mapped.Type?.CsType, Is.EqualTo("ref global::System.UIntPtr"));
  }

  [Test]
  public void Parameter_WidthTypedefReference_KeepsItsName()
  {
    // Arrange (int64_t& is long& on Linux: Types.i applies INOUT to the written name)
    var type = Ref(new BuiltinType("long long", false, "int64_t"));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("int64_t&"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("ref long"));
    }
  }

  [Test]
  public void Parameter_StdStream_IsSkipped()
  {
    // Arrange
    var type = Ref(Class("std::basic_ostream"));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    Assert.That(mapped.Skip, Does.Contain("std type"));
  }

  [Test]
  public void Parameter_EnumReference_IsRefEnum()
  {
    // Arrange
    var type = Ref(Enum("TopAbs_ShapeEnum"));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("TopAbs_ShapeEnum&"));
      Assert.That(mapped.Type?.CsKey, Is.EqualTo("ref TopAbs_ShapeEnum"));
    }
  }

  [Test]
  public void Return_ConstReferenceToValueClass_IsCopied()
  {
    // Arrange
    var type = Ref(Const(Class("TopoDS_Shape")));

    // Act
    var mapped = _mapper.Return(type);

    // Assert
    Assert.That(mapped.Type?.Spelling, Is.EqualTo("const TopoDS_Shape&"));
  }

  [Test]
  public void Return_ConstReferenceToPlainClass_IsSkipped()
  {
    // Arrange
    var type = Ref(Const(Class("BRepBuilderAPI_MakeShape")));

    // Act
    var mapped = _mapper.Return(type);

    // Assert
    Assert.That(mapped.Skip, Does.Contain("isn't copyable"));
  }

  [Test]
  public void Return_MutableReference_IsSkipped()
  {
    // Arrange
    var type = Ref(Class("TopoDS_Shape"));

    // Act
    var mapped = _mapper.Return(type);

    // Assert
    Assert.That(mapped.Skip, Does.Contain("mutable reference"));
  }

  [TestCase("std::basic_string")]
  [TestCase("std::__cxx11::basic_string")]
  public void Parameter_StdString_IsAString(string template)
  {
    // Arrange (libstdc++ declares it in an inline namespace)
    var type = Ref(Const(Template(template, Builtin("char"),
                                  Template("std::char_traits", Builtin("char")),
                                  Template("std::allocator", Builtin("char")))));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("const std::string&"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("string"));
    }
  }

  [Test]
  public void Parameter_StdArrayOfNumbers_IsACsArrayThroughItsMacro()
  {
    // Arrange
    var type = Ref(Const(Template("std::array", Builtin("double"), new ConstantArgument("3"))));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("const std::array< double, 3 >&"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("double[]"));
      Assert.That(mapped.Type?.Typemap, Is.EqualTo("%netocc_std_array(%arg(double), 3, double)"));
    }
  }

  [TestCase("char")]
  [TestCase("bool")]
  [TestCase("long long")]
  public void Parameter_StdArrayOfAnElementCSharpDoesntPin_IsSkipped(string element)
  {
    // Arrange (char and bool marshal differently; template arguments are canonical, and int64_t is
    // long on Linux)
    var type = Ref(Const(Template("std::array", Builtin(element), new ConstantArgument("3"))));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    Assert.That(mapped.Type, Is.Null);
  }

  [Test]
  public void Parameter_ArrayReference_IsACsArrayThroughItsMacro()
  {
    // Arrange (int (&)[3]: three elements, which the callee reads and writes in place)
    var type = Ref(new ArrayType(Builtin("int"), 3));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("NetOcc_ArrayRef< int, 3 >"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("int[]"));
      Assert.That(mapped.Type?.Typemap, Is.EqualTo("%netocc_array_ref(int, 3, int)"));
    }
  }

  [Test]
  public void Parameter_BoolArrayReference_IsOneBytePerElement()
  {
    // Arrange
    var type = Ref(new ArrayType(Const(Builtin("bool")), 3));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    Assert.That(mapped.Type?.Typemap, Is.EqualTo("%netocc_bool_array_ref(3)"));
  }

  [Test]
  public void Parameter_StdBitset_IsAMaskThroughItsMacro()
  {
    // Arrange
    var type = Ref(Const(Template("std::bitset", new ConstantArgument("18"))));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("const std::bitset< 18 >&"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("ulong"));
      Assert.That(mapped.Type?.Typemap, Is.EqualTo("%netocc_bitset(18)"));
    }
  }

  [Test]
  public void Return_StdOptionalOfAStruct_IsNullable()
  {
    // Arrange
    var type = Template("std::optional", Class("gp_Pnt"));

    // Act
    var mapped = _mapper.Return(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.CsType, Is.EqualTo("gp_Pnt?"));
      Assert.That(mapped.Type?.Typemap, Is.EqualTo("%netocc_optional(%arg(gp_Pnt), gp_Pnt)"));
    }
  }

  [Test]
  public void Arguments_OfAPair_CarryTheirMacros()
  {
    // Arrange (the pair's module needs the bitset's typemaps before its %template)
    var pair = CollectionTemplate.Named("std::pair")!;

    // Act
    var (arguments, _) =
      _mapper.Arguments(
        pair, [Builtin("double"), Template("std::bitset", new ConstantArgument("18"))]);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(arguments?.CsTypes, Is.EqualTo(new[] { "double", "ulong" }));
      Assert.That(arguments?.Typemaps, Is.EqualTo(new[] { "%netocc_bitset(18)" }));
    }
  }

  [Test]
  public void Parameter_StdSharedPointer_IsSkipped()
  {
    // Arrange
    var type =
      Ref(Const(Template("std::shared_ptr", Template("std::basic_istream", Builtin("char")))));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    Assert.That(mapped.Skip, Is.EqualTo("std type std::shared_ptr"));
  }
}
