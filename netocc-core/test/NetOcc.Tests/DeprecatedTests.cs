// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

// NUnit
using NUnit.Framework;

//
using OCC.Core.Geom;
using OCC.Core.gp;
using OCC.Core.Message;
using OCC.Core.TColgp;

namespace NetOcc.Tests;

/// <summary>
/// What OCCT deprecates (Standard_DEPRECATED) is wrapped and [Obsolete]: C# callers get the warning
/// C++ callers get.
/// </summary>
[TestFixture]
public class DeprecatedTests
{
  private static readonly gp_Pnt[] Poles = [new(0, 0, 0), new(1, 2, 0), new(2, 0, 1)];

  [Test]
  public void DeprecatedOverload_IsObsolete_ItsSiblingIsNot()
  {
    // Arrange
    var type = typeof(Geom_BezierCurve);

    // Act
    var deprecated = type.GetMethod("Poles", [typeof(TColgp_Array1OfPnt)])!;
    var current = type.GetMethod("Poles", Type.EmptyTypes)!;

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(Attribute.IsDefined(deprecated, typeof(ObsoleteAttribute)), Is.True);
      Assert.That(Attribute.IsDefined(current, typeof(ObsoleteAttribute)), Is.False);
    }
  }

  [Test]
  public void DeprecatedMember_CallsOcct()
  {
    // Arrange
    using var poles = new TColgp_Array1OfPnt(Poles);
    var curve = new Geom_BezierCurve(poles);
    using var copied = new TColgp_Array1OfPnt(1, Poles.Length);

    // Act
#pragma warning disable CS0618 // the deprecated overload under test
    curve.Poles(copied);
#pragma warning restore CS0618

    // Assert
    Assert.That(copied.ToArray(), Is.EqualTo(Poles));
  }

  [Test]
  public void DeprecatedClass_IsObsolete_ItsBaseIsNot()
  {
    // Arrange
#pragma warning disable CS0618 // the deprecated class under test
    var type = typeof(Message_ProgressSentry);
#pragma warning restore CS0618

    // Act
    var obsolete = Attribute.IsDefined(type, typeof(ObsoleteAttribute), false);
    var baseObsolete = Attribute.IsDefined(type.BaseType!, typeof(ObsoleteAttribute), false);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(obsolete, Is.True);
      Assert.That(baseObsolete, Is.False);
    }
  }
}
