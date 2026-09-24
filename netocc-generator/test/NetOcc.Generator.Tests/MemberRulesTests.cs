// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

// NUnit
using NUnit.Framework;

//
using NetOcc.Generator.Emit;
using NetOcc.Generator.Model;

// static usings
using static NetOcc.Generator.Tests.Types;

namespace NetOcc.Generator.Tests;

/// <summary>The member rules both writers share.</summary>
[TestFixture]
public class MemberRulesTests
{
  [Test]
  public void HasConstTwin_CountsATwinReturningTheValue()
  {
    // Arrange (Image_ColorRGB::r() returns uint8_t&, r() const the value; the lists are the
    // overloads' own)
    var mutableParameters = new List<ParameterModel>();
    var constParameters = new List<ParameterModel>();
    CppType reference = Ref(Builtin("unsigned char"));

    // Act
    var covered = MemberRules.HasConstTwin(reference, mutableParameters, [
      (reference, mutableParameters), (Builtin("unsigned char"), constParameters)
    ]);

    // Assert
    Assert.That(covered, Is.True);
  }

  [Test]
  public void Declarable_LogsTheCallsBelowAnAmbiguousOne()
  {
    // Arrange (Fit(a, b) takes two arguments too, so Fit(a, b = 1, c = 2) declares all three: its
    // call with one is lost)
    List<ParameterModel> fit =
    [
      new("theA", Builtin("int"), null), new("theB", Builtin("int"), "1"),
      new("theC", Builtin("int"), "2")
    ];
    List<ParameterModel> pair =
      [new("theA", Builtin("int"), null), new("theB", Builtin("int"), null)];
    List<string> skipped = [];

    // Act
    var declared = MemberRules.Declarable(new(), "Demo_Fit::Fit", fit, [fit, pair], [fit, pair],
                                          null, skipped);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(declared, Has.Count.EqualTo(3));
      Assert.That(declared, Has.All.Matches<ParameterModel>(p => p.Default is null));
      Assert.That(skipped, Is.EqualTo(new[]
      {
        "Demo_Fit::Fit: its calls with 1 argument are left out: C++ takes them, but not the one with 2, "
        + "and a declaration's defaults run to its end"
      }));
    }
  }

  [Test]
  public void Declarable_SkipsAMemberWhoseEveryCallIsAmbiguous()
  {
    // Arrange (Fit(a = 0, b = 1) takes Fit() and Fit(a) too, so C++ can't pick Fit(a = 0) for
    // either)
    List<ParameterModel> one = [new("theA", Builtin("int"), "0")];
    List<ParameterModel> two = [new("theA", Builtin("int"), "0"), new("theB", Builtin("int"), "1")];
    List<string> skipped = [];

    // Act
    var declared =
      MemberRules.Declarable(new(), "Demo_Fit::Fit", one, [one, two], [one, two], null, skipped);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(declared, Is.Null);
      Assert.That(skipped, Is.EqualTo(new[]
      {
        "Demo_Fit::Fit: every call is ambiguous in C++: for each number of arguments, "
        + "another overload takes them and defaults the rest"
      }));
    }
  }

  [Test]
  public void Initializers_DropTheDefaultsBeforeOneTheyCantWrite()
  {
    // Arrange (theMode's default is a macro that isn't a constant: the ones before it can't stay, a
    // parameter without one follows)
    List<ParameterModel> parameters =
    [
      new("theShape", Builtin("int"), null), new("theLevel", Builtin("int"), "6"),
      new("theMode", Enum("Demo_Mode"), ""),
      new("theDeep", Builtin("bool"), "false"),
    ];
    List<string> skipped = [];

    // Act
    var initializers = MemberRules.Initializers(parameters, 1, "Demo_Tool::Apply", skipped);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(initializers, Is.EqualTo(new[] { "", "", "", " = false" }));
      Assert.That(skipped, Is.EqualTo(new[]
      {
        "Demo_Tool::Apply: default of theMode dropped (from a macro), and the defaults before it"
      }));
    }
  }
}
