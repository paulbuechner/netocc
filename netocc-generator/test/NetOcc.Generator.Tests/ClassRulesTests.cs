// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

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

/// <summary>What a package's classes become, as its config picks them.</summary>
[TestFixture]
public class ClassRulesTests
{
  [Test]
  public void IsListed_PicksTheInstancesAPackageOwns()
  {
    // Arrange (classes lists the package's own classes; an instance it owns serves every package's
    // signatures)
    var config = new PackageConfig { Classes = ["Demo_Driver"] };
    var instance = new ClassModel("Demo_Tree_float", "Demo_Tree.hxx", [], ValueTraits, [], [],
                                  Instance: new ClassInstance(
                                    "Demo_Tree<float>", ["Demo_Tree.hxx"],
                                    Template("Demo_Tree", Builtin("float"))));

    // Act
    var listed = ClassRules.IsListed(instance, config);

    // Assert
    Assert.That(listed, Is.True);
  }

  [TestCase(true, true, true, nameof(WrapKind.Transient))]
  [TestCase(false, true, true, nameof(WrapKind.ValueClass))]
  [TestCase(false, false, true, nameof(WrapKind.Plain))]
  [TestCase(false, true, false, nameof(WrapKind.Plain))]
  public void KindOf_WrapsCopiesThatProxiesCanDelete(bool transient, bool copyable,
                                                     bool publicDestructor, string expected)
  {
    // Arrange (a proxy owns its value class's copies, so it must be able to delete them)
    var c = new ClassModel("Demo_Thing", "Demo_Thing.hxx", [], ValueTraits with
    {
      IsTransient = transient,
      IsCopyable = copyable,
      HasPublicDestructor = publicDestructor,
    }, [], []);

    // Act
    var kind = ClassRules.KindOf(c);

    // Assert (by name: the test's parameters are public, the enum isn't)
    Assert.That(kind.ToString(), Is.EqualTo(expected));
  }
}
