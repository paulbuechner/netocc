// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

// NUnit
using NUnit.Framework;

//
using OCC.Core.Geom;
using OCC.Core.gp;
using OCC.Core.TopoDS;

namespace NetOcc.Tests;

/// <summary>
/// Nested and namespace types under their flat names: plain data as C# structs, nested enums.
/// </summary>
[TestFixture]
public class NestedTypeTests
{
  [Test]
  public void PlainData_ComesBackByValue()
  {
    // Arrange
    var circle = new Geom_Circle(new gp_Ax2(), 2.0);

    // Act
    var result = circle.EvalD1(0); // Geom_Curve::ResD1

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(result.Point, Is.EqualTo(new gp_Pnt(2, 0, 0)));
      Assert.That(result.D1, Is.EqualTo(new gp_Vec(0, 2, 0)));
    }
  }

  [Test]
  public void PlainData_FieldsAreWritable()
  {
    // Arrange
    var result = new Geom_Curve_ResD1();

    // Act
    result.Point = new gp_Pnt(1, 2, 3);

    // Assert
    Assert.That(result.Point, Is.EqualTo(new gp_Pnt(1, 2, 3)));
  }

  [Test]
  public void NestedEnum_SelectsTheConstructor()
  {
    // Arrange
    var expected = new gp_Dir(0, -1, 0);

    // Act
    var direction = new gp_Dir(gp_Dir_D.NY); // gp_Dir::D

    // Assert
    Assert.That(direction, Is.EqualTo(expected));
  }

  [Test]
  public void NestedEnum_KeepsItsUnderlyingType()
  {
    // Arrange
    var type = typeof(TopoDS_TShape_BitLayout); // enum BitLayout : uint16_t

    // Act
    var underlying = Enum.GetUnderlyingType(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(underlying, Is.EqualTo(typeof(ushort)));
      Assert.That((ushort)TopoDS_TShape_BitLayout.Bits_Reserved, Is.EqualTo(0xF000),
                  "not sign-extended");
    }
  }
}
