// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Runtime.CompilerServices;

// NUnit
using NUnit.Framework;

//
using NetOcc.Generator.Mapping;
using NetOcc.Generator.Model;

// static usings
using static NetOcc.Generator.Tests.Types;

namespace NetOcc.Generator.Tests;

/// <summary>
/// Collection instantiations: an OCCT alias names one, or a name of its own in the package of its
/// first user.
/// </summary>
[TestFixture]
public class TypeRegistryTests
{
  [Test]
  public void Instantiation_WithAnAlias_IsNamedByIt()
  {
    // Arrange
    var registry = Registry();

    // Act
    var instantiation = registry.Instantiation(Template("NCollection_Array1", Builtin("double")));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(instantiation?.Alias, Is.EqualTo("TColStd_Array1OfReal"));
      Assert.That(instantiation?.Package, Is.EqualTo("TColStd"), "the alias's package");
    }
  }

  [TestCase("NCollection_Array1", "gp_Pnt", "NCollection_Array1_gp_Pnt")]
  [TestCase("NCollection_List", "Handle(Geom_Surface)", "NCollection_List_Handle_Geom_Surface")]
  [TestCase("NCollection_Sequence", "TColStd_Array1OfReal",
            "NCollection_Sequence_TColStd_Array1OfReal")]
  public void Instantiation_WithoutAlias_IsNamedAfterItsArguments(
    string template, string element, string name)
  {
    // Arrange
    var registry = Registry();
    CppType argument = element switch
    {
      "Handle(Geom_Surface)" => Handle("Geom_Surface"),
      "TColStd_Array1OfReal" => Template("NCollection_Array1", Builtin("double")),
      _ => Class(element),
    };

    // Act
    var instantiation = registry.Instantiation(Template(template, argument));

    // Assert
    Assert.That(instantiation?.Alias, Is.EqualTo(name));
  }

  [Test]
  public void Instantiation_OfAMapWithTheDefaultHasher_LeavesTheHasherOut()
  {
    // Arrange
    var registry = Registry();
    var map = Template("NCollection_Map", Class("gp_Pnt"),
                       Template("NCollection_DefaultHasher", Class("gp_Pnt")));

    // Act
    var instantiation = registry.Instantiation(map);

    // Assert
    Assert.That(instantiation?.Alias, Is.EqualTo("NCollection_Map_gp_Pnt"));
  }

  [Test]
  public void ScanModule_KnowsWhatARunWritingTheModuleKnows()
  {
    // Arrange (the golden run's Demo module, as a later run over other packages finds it)
    using var work = new TempDirectory("netocc-gen-scan");
    var expected = Path.Combine(SourceDirectory(), "Golden", "expected");
    var wrapper = work.CreateSubdirectory("wrapper");
    var headers = work.CreateSubdirectory("headers");
    File.Copy(Path.Combine(expected, "Demo.i"), Path.Combine(wrapper, "Demo.i"));
    File.Copy(Path.Combine(expected, "Demo_nested.hxx"), Path.Combine(headers, "Demo_nested.hxx"));
    var registry = new TypeRegistry();

    // Act
    registry.ScanModule(Path.Combine(wrapper, "Demo.i"));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(registry.Enum("Demo_Status"),
                  Is.EqualTo(new KnownEnum("Demo_Status", "Demo", "Demo_nested.hxx",
                                           QualifiedName: "Demo::Status")));
      Assert.That(registry.Enum("Demo_Curve_Flags")?.Underlying, Is.EqualTo("unsigned short"),
                  "csbase ushort");
      Assert.That(registry.Enum("class"), Is.Null, "enum class X is X");
      Assert.That(registry.Class("Demo_Curve_ResD1")?.DeclaringHeader,
                  Is.EqualTo("Demo_nested.hxx"));
      Assert.That(registry.Class("Demo_Options"),
                  Has.Property(nameof(KnownClass.HasDefaultConstructor)).False);
      Assert.That(registry.Class("Demo_Shape"),
                  Has.Property(nameof(KnownClass.HasDefaultConstructor)).True);
    }
  }

  [Test]
  public void SplitArguments_UnwrapsArgAndKeepsTemplateCommas()
  {
    // Arrange (a macro call's arguments after its name, as a module writes them)
    const string arguments = "%arg(std::pair<int, double>), NCollection_Map<int, Demo_Hasher>, int";

    // Act
    var split = TypeRegistry.SplitArguments(arguments);

    // Assert
    Assert.That(
      split,
      Is.EqualTo(new[] { "std::pair<int, double>", "NCollection_Map<int, Demo_Hasher>", "int" }));
  }

  [Test]
  public void AssignOwners_GivesTheFirstUserTheInstantiationAndItsBase()
  {
    // Arrange (a handle-managed array of points, used by two packages; its base array too)
    var registry = Registry();
    var handleManaged = registry.Instantiation(Template("NCollection_HArray1", Class("gp_Pnt")))!;

    // Act
    registry.AssignOwners([("GeomAPI", [handleManaged]), ("BRepFill", [handleManaged])]);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(registry.Instantiation(Template("NCollection_HArray1", Class("gp_Pnt")))?.Package,
                  Is.EqualTo("GeomAPI"));
      Assert.That(registry.BaseOf(handleManaged)?.Package, Is.EqualTo("GeomAPI"),
                  "its base goes with it");
    }
  }

  // the fixture lives next to this file in the source tree
  private static string SourceDirectory([CallerFilePath] string path = "") =>
    Path.GetDirectoryName(path)!;
}
