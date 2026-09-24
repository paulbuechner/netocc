// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// NUnit
using NUnit.Framework;

//
using NetOcc.Generator.Mapping;
using NetOcc.Generator.Model;

// static usings
using static NetOcc.Generator.Tests.Types;

namespace NetOcc.Generator.Tests;

/// <summary>
/// Raw pointers to netocc-core's contract: a class is its proxy, numbers and structs a C# array
/// unless the callee may keep them, void*, functions and undefined classes an IntPtr.
/// </summary>
[TestFixture]
public class PointerMappingTests
{
  private SignatureMapper _mapper = null!;

  [SetUp]
  public void CreateMapper() => _mapper = new SignatureMapper(Registry());

  [TestCase("TopoDS_Shape")]
  [TestCase("BRepBuilderAPI_MakeShape")]
  [TestCase("Geom_Surface")]
  public void Parameter_PointerToClass_IsItsProxy(string name)
  {
    // Arrange
    var type = Pointer(Const(Class(name)));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo($"const {name}*"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo(name));
    }
  }

  [Test]
  public void Parameter_PointerToCollection_IsTheAlias()
  {
    // Arrange
    var type = Pointer(Const(Template("NCollection_Array1", Builtin("double"))));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    Assert.That(mapped.Type?.CsType, Is.EqualTo("TColStd_Array1OfReal"));
  }

  [TestCase("double", true, "double[]")]
  [TestCase("int", false, "int[]")]
  [TestCase("unsigned char", false, "byte[]")]
  [TestCase("char", false, "byte[]")]
  [TestCase("char16_t", false, "char[]")]
  [TestCase("bool", false, "bool[]")]
  [TestCase("intptr_t", false, "global::System.IntPtr[]")]
  public void Parameter_PointerToNumbers_IsAnArray(string builtin, bool isConst, string csType)
  {
    // Arrange
    CppType element = isConst ? Const(Builtin(builtin)) : Builtin(builtin);

    // Act
    var mapped = _mapper.Parameter(Pointer(element));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.CsType, Is.EqualTo(csType));
      Assert.That(mapped.Type?.Spelling, Is.EqualTo(Pointer(element).Spelling));
    }
  }

  [Test]
  public void Parameter_PointerToValueType_IsAnArrayOfTheStruct()
  {
    // Arrange
    var type = Pointer(Const(Class("gp_Pnt")));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.CsType, Is.EqualTo("gp_Pnt[]"));
      Assert.That(mapped.Type?.Uses, Is.EqualTo(new[] { new TypeUse("gp", "gp_Pnt.hxx") }));
    }
  }

  [Test]
  public void Parameter_PointerTheCalleeMayKeep_IsAnAddress()
  {
    // Arrange (a constructor or setter may keep it; a C# array is pinned for the call only)
    var type = Pointer(Const(Builtin("double")));

    // Act
    var mapped = _mapper.Parameter(type, mayKeep: true);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("NetOcc_Address< const double* >"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo(SignatureMapper.CsAddress));
      Assert.That(mapped.Type?.Typemap, Is.EqualTo("%netocc_address(%arg(const double*))"));
    }
  }

  [Test]
  public void Parameter_ConstPointerToVoid_IsAnIntPtrWithoutTopLevelConst()
  {
    // Arrange
    var type = new PointerType(Builtin("void"), Const: true);

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("void*"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo(SignatureMapper.CsAddress));
      Assert.That(mapped.Type?.Typemap, Is.Null, "Types.i covers void*");
    }
  }

  [Test]
  public void Parameter_FunctionPointer_IsAnAddress()
  {
    // Arrange
    var type =
      Pointer(new FunctionType(Builtin("double"), [Builtin("double"), Pointer(Builtin("void"))]));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("NetOcc_Address< double (*)(double, void*) >"));
      Assert.That(mapped.Type?.Typemap,
                  Is.EqualTo("%netocc_address(%arg(double (*)(double, void*)))"));
    }
  }

  [Test]
  public void Parameter_PointerToPointer_IsAnAddress()
  {
    // Arrange
    var type = Pointer(Pointer(Const(Builtin("int"))));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    Assert.That(mapped.Type?.Spelling, Is.EqualTo("NetOcc_Address< const int** >"));
  }

  [Test]
  public void Parameter_PointerToUndefinedClass_IsOpaqueUnderItsQualifiedName()
  {
    // Arrange (class Outer::Cursor; declared, never defined)
    var type =
      Pointer(new NamedType("Outer_Cursor", NamedKind.Class, [], DeclaredOnly: "Outer::Cursor"));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    Assert.That(mapped.Type?.Spelling, Is.EqualTo("NetOcc_Address< Outer::Cursor* >"));
  }

  [Test]
  public void Parameter_PointerToClassDefinedElsewhere_IsItsProxy()
  {
    // Arrange (only declared in this package's headers, defined by another)
    var type =
      Pointer(new NamedType("TopoDS_Shape", NamedKind.Class, [], DeclaredOnly: "TopoDS_Shape"));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    Assert.That(mapped.Type?.CsType, Is.EqualTo("TopoDS_Shape"));
  }

  [Test]
  public void Parameter_ConstReferenceToPointer_IsThePointer()
  {
    // Arrange
    var type = Ref(new PointerType(Class("TopoDS_Shape"), Const: true));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("TopoDS_Shape*"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("TopoDS_Shape"));
    }
  }

  [TestCase("TopoDS_Shape", "TopoDS_Shape*&", "ref TopoDS_Shape")]
  [TestCase("const char", "const char*&", "ref string")]
  [TestCase("const int", "NetOcc_AddressRef< const int* >", "ref " + SignatureMapper.CsAddress)]
  public void Parameter_ReferenceToPointer_IsRef(string pointee, string spelling, string csType)
  {
    // Arrange
    CppType element = pointee switch
    {
      "const char" => Const(Builtin("char")),
      "const int" => Const(Builtin("int")),
      _ => Class(pointee),
    };

    // Act
    var mapped = _mapper.Parameter(Ref(Pointer(element)));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo(spelling));
      Assert.That(mapped.Type?.CsType, Is.EqualTo(csType));
    }
  }

  [Test]
  public void Parameter_ExtString_IsAString()
  {
    // Arrange (Standard_ExtString)
    var type = Pointer(Const(Builtin("char16_t")));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    Assert.That(mapped.Type?.CsType, Is.EqualTo("string"));
  }

  [Test]
  public void Parameter_StreamPointer_IsAStream()
  {
    // Arrange
    var type = Pointer(OStream.Referee);

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("std::ostream*"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo(SignatureMapper.CsStream));
    }
  }

  [Test]
  public void Parameter_ArrayOfArrays_IsARectangularArrayDeclaredAsWritten()
  {
    // Arrange
    var type = new ArrayType(new ArrayType(Const(Builtin("double")), 3), 3);

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.CsType, Is.EqualTo("double[,]"));
      Assert.That(mapped.Type?.Declare("theJ"), Is.EqualTo("const double theJ[3][3]"));
    }
  }

  [Test]
  public void Parameter_ArrayOfStructs_IsDeclaredAsAPointer()
  {
    // Arrange (gp_Pnt theP[8])
    var type = new ArrayType(Class("gp_Pnt"), 8);

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.CsType, Is.EqualTo("gp_Pnt[]"));
      Assert.That(mapped.Type?.Declare("theP"), Is.EqualTo("gp_Pnt* theP"));
    }
  }

  [Test]
  public void Return_PointerToClass_BorrowsInAMemberOnly()
  {
    // Arrange
    var type = Pointer(Class("TopoDS_Shape"));

    // Act
    var member = _mapper.Return(type, [], owner: "BRepBuilderAPI_MakeShape");
    var alone = _mapper.Return(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(member.Type?.Spelling, Is.EqualTo("TopoDS_Shape*"),
                  "References.i keeps the owner alive");
      Assert.That(alone.Type?.Spelling, Is.EqualTo("TopoDS_Shape* const"),
                  "no object to keep alive");
      Assert.That(member.Type?.CsType, Is.EqualTo("TopoDS_Shape"));
    }
  }

  [Test]
  public void Return_PointerToTransient_OwnsAReference()
  {
    // Arrange
    var type = Pointer(Class("Geom_Surface"));

    // Act
    var member = _mapper.Return(type, [], owner: "BRepBuilderAPI_MakeShape");

    // Assert
    Assert.That(member.Type?.Spelling, Is.EqualTo("Geom_Surface* const"),
                "constructors return T*, which the ref feature counts");
  }

  [TestCase("double", "NetOcc_Address< const double* >")]
  [TestCase("gp_Pnt", "NetOcc_Address< const gp_Pnt* >")]
  public void Return_PointerToNumbersOrStructs_IsAnAddress(string pointee, string spelling)
  {
    // Arrange (C# can't know the length)
    var type = Pointer(Const(pointee == "double" ? Builtin(pointee) : Class(pointee)));

    // Act
    var mapped = _mapper.Return(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo(spelling));
      Assert.That(mapped.Type?.CsType, Is.EqualTo(SignatureMapper.CsAddress));
    }
  }

  [Test]
  public void Return_PointerAMemberHolds_IsARefIntPtr()
  {
    // Arrange (NCollection_ListNode*& Next())
    var type = Ref(Pointer(Class("TopoDS_Shape")));

    // Act
    var mapped = _mapper.Return(type, [], owner: "BRepBuilderAPI_MakeShape");

    // Assert
    Assert.That(mapped.Type?.CsType, Is.EqualTo("ref " + SignatureMapper.CsAddress));
  }

  [Test]
  public void Return_NativeStream_IsSkipped()
  {
    // Arrange
    var type = Pointer(OStream.Referee);

    // Act
    var mapped = _mapper.Return(type);

    // Assert
    Assert.That(mapped.Skip, Does.StartWith("returns a native std::ostream"));
  }
}
