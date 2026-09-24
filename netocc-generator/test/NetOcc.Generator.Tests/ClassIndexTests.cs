// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

// NUnit
using NUnit.Framework;

//
using NetOcc.Generator.Config;
using NetOcc.Generator.Emit;
using NetOcc.Generator.Model;

// static usings
using static NetOcc.Generator.Tests.Types;

namespace NetOcc.Generator.Tests;

/// <summary>
/// classes.json: the types a package gives C#, as OCCT's reference manual documents them.
/// </summary>
[TestFixture]
public class ClassIndexTests
{
  [Test]
  public void Of_NamesWhatEachTypeIsDocumentedAs()
  {
    // Arrange (a class, a nested struct of plain data, an enum and a namespace's functions)
    var package = new PackageModel("Demo", ["Demo_Curve.hxx"], [
      new EnumModel("Demo_Kind", "Demo_Kind.hxx", [new EnumConstant("Demo_A", 0)])
    ], [
      new ClassModel("Demo_Curve", "Demo_Curve.hxx", [], ValueTraits, [], []),
      new ClassModel("Demo_Curve_ResD1", "Demo_Curve.hxx", [], ValueTraits, [], [],
                     Fields: [new FieldModel("Value", Builtin("double"), 0, 8, IsPublic: true)],
                     QualifiedName: "Demo_Curve::ResD1", IsPlainData: true, IsStruct: true),
    ], [], [new FunctionModel("Demo_Tools", "Scale", Builtin("double"), [], IsDeprecated: false)]);

    // Act
    var types = ClassIndex.Of(package, new PackageConfig(), Registry(), ["Demo_Tools"]);

    // Assert
    Assert.That(types, Is.EqualTo(new[]
    {
      new DocumentedType("Demo_Curve", "class", "Demo_Curve"),
      new DocumentedType("Demo_Curve_ResD1", "struct", "Demo_Curve::ResD1"),
      new DocumentedType("Demo_Kind", "enum", "Demo_Kind", "Demo_Kind.hxx"),
      new DocumentedType("Demo_Tools", "namespace", "Demo_Tools"),
    }));
  }

  [Test]
  public void Of_NamesTheTemplatesOfInstancesAndCollections()
  {
    // Arrange (an instance of a class template the package owns, and a collection its signatures
    // use)
    var registry = Registry();
    registry.AddInstantiation(Collection("NCollection_List", Builtin("int"), "Demo_ListOfInteger",
                                         "Demo"));
    registry.Request(registry.Instantiation(Template("NCollection_List", Builtin("int")))!);
    var tree = new ClassModel("Demo_Tree_float", "Demo_Tree.hxx", [], ValueTraits, [], [],
                              Instance: new ClassInstance("Demo_Tree<float>", ["Demo_Tree.hxx"],
                                                          Template("Demo_Tree", Builtin("float"))),
                              Template: "Demo_Tree");
    var package = new PackageModel("Demo", ["Demo_Tree.hxx"], [], [tree], [], []);

    // Act
    var types = ClassIndex.Of(package, new PackageConfig(), registry, []);

    // Assert
    Assert.That(types, Is.EqualTo(new[]
    {
      new DocumentedType("Demo_ListOfInteger", "class", "NCollection_List", Instance: true),
      new DocumentedType("Demo_Tree_float", "class", "Demo_Tree", Instance: true),
    }));
  }

  [Test]
  public void Of_ListsOnlyTheNamespacesCSharpGets()
  {
    // Arrange (every function of Demo_Hidden is skipped: the module has no static class for it)
    var package = new PackageModel("Demo", ["Demo_Tools.hxx"], [], [], [], [
      new FunctionModel("Demo_Tools", "Scale", Builtin("double"), [], IsDeprecated: false),
      new FunctionModel("Demo_Hidden", "Raw", Builtin("double"), [], IsDeprecated: false)
    ]);

    // Act
    var types = ClassIndex.Of(package, new PackageConfig(), Registry(), ["Demo_Tools"]);

    // Assert
    Assert.That(
      types, Is.EqualTo(new[] { new DocumentedType("Demo_Tools", "namespace", "Demo_Tools") }));
  }

  [Test]
  public void Json_KeepsThePackagesARunDidntParse()
  {
    // Arrange (a run over Demo only: Other comes from the previous file)
    const string previous =
      "{\n  \"Other\": [\n    {\n      \"name\": \"Other_Tool\",\n      \"kind\": \"class\",\n      \"cpp\": \"Other_Tool\"\n    }\n  ]\n}\n";
    var packages = new Dictionary<string, List<DocumentedType>>
      { ["Demo"] = [new DocumentedType("Demo_Curve", "class", "Demo_Curve")] };

    // Act
    var json = ClassIndex.Json(["Demo", "Other"], packages, previous);

    // Assert
    Assert.That(
      json,
      Is.EqualTo(
        "{\n  \"Demo\": [\n    {\n      \"name\": \"Demo_Curve\",\n      \"kind\": \"class\",\n      \"cpp\": \"Demo_Curve\"\n    }\n  ],\n"
        + "  \"Other\": [\n    {\n      \"name\": \"Other_Tool\",\n      \"kind\": \"class\",\n      \"cpp\": \"Other_Tool\"\n    }\n  ]\n}\n"));
  }
}
