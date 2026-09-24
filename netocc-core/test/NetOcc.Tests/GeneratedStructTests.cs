// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

// NUnit
using NUnit.Framework;

//
using OCC.Core.Bnd;
using OCC.Core.BRepBndLib;
using OCC.Core.BRepBuilderAPI;
using OCC.Core.BRepGProp;
using OCC.Core.BRepPrimAPI;
using OCC.Core.gp;
using OCC.Core.GProp;
using OCC.Core.Quantity;

namespace NetOcc.Tests;

/// <summary>
/// The generated value-type structs: native members through thunks, C# operators, use across
/// packages.
/// </summary>
[TestFixture]
public class GeneratedStructTests
{
  [Test]
  public void Pnt2d_DistanceRunsInOcct()
  {
    // Arrange
    var a = new gp_Pnt2d(0, 0);
    var b = new gp_Pnt2d(3, 4);

    // Act
    var distance = a.Distance(b);

    // Assert
    Assert.That(distance, Is.EqualTo(5.0).Within(1e-12));
  }

  [Test]
  public void Vec_OperatorsAreOcctsOperators()
  {
    // Arrange
    var x = new gp_Vec(1, 0, 0);
    var y = new gp_Vec(0, 1, 0);

    // Act
    var cross = x ^ y;
    var dot = x * y;
    var halved = x / 2;

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(cross, Is.EqualTo(new gp_Vec(0, 0, 1)), "operator^ is the cross product");
      Assert.That(dot, Is.EqualTo(0.0), "operator* of two vectors is the dot product");
      Assert.That(halved, Is.EqualTo(new gp_Vec(0.5, 0, 0)));
    }
  }

  [Test]
  public void Pln_DistanceToAPoint()
  {
    // Arrange
    var plane = new gp_Pln(new gp_Pnt(0, 0, 1), new gp_Dir(0, 0, 1));

    // Act
    var distance = plane.Distance(new gp_Pnt(5, 5, 4));

    // Assert
    Assert.That(distance, Is.EqualTo(3.0).Within(1e-12));
  }

  [Test]
  public void Circ_LengthIsTheCircumference()
  {
    // Arrange
    var circle = new gp_Circ(new gp_Ax2(), 2.0);

    // Act
    var length = circle.Length();

    // Assert
    Assert.That(length, Is.EqualTo(4 * Math.PI).Within(1e-12));
  }

  [Test]
  public void MakeFace_TakesAPlaneStruct()
  {
    // Arrange
    var plane = new gp_Pln(new gp_Pnt(0, 0, 0), new gp_Dir(0, 0, 1));
    var properties = new GProp_GProps();

    // Act
    var face = new BRepBuilderAPI_MakeFace(plane, 0, 2, 0, 3).Face();
    BRepGProp.SurfaceProperties(face, properties);

    // Assert
    Assert.That(properties.Mass(), Is.EqualTo(6.0).Within(1e-9));
  }

  [Test]
  public void BndBox_IsFilledThroughRef()
  {
    // Arrange
    var box = new BRepPrimAPI_MakeBox(10, 20, 30).Shape();
    var bounds = new Bnd_Box();
    double xmin = 0, ymin = 0, zmin = 0, xmax = 0, ymax = 0, zmax = 0;

    // Act
    BRepBndLib.Add(box, ref bounds);
    bounds.Get(ref xmin, ref ymin, ref zmin, ref xmax, ref ymax, ref zmax);

    // Assert (the box grows by the shape's tolerance)
    using (Assert.EnterMultipleScope())
    {
      Assert.That(bounds.IsVoid(), Is.False);
      Assert.That(xmax - xmin, Is.EqualTo(10).Within(1e-6));
      Assert.That(ymax - ymin, Is.EqualTo(20).Within(1e-6));
      Assert.That(zmax - zmin, Is.EqualTo(30).Within(1e-6));
    }
  }

  [Test]
  public void Gp_StaticMembersReturnStructs()
  {
    // Arrange
    // (gp's static members)

    // Act
    var origin = gp.Origin();
    var dz = gp.DZ();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(origin, Is.EqualTo(new gp_Pnt(0, 0, 0)));
      Assert.That(dz, Is.EqualTo(new gp_Dir(0, 0, 1)));
    }
  }

  [Test]
  public void ColorRgba_HoldsAColorAndAlpha()
  {
    // Arrange
    var red = new Quantity_Color(1, 0, 0, Quantity_TypeOfColor.Quantity_TOC_RGB);

    // Act
    var color = new Quantity_ColorRGBA(red, 0.5f);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(color.Alpha(), Is.EqualTo(0.5f));
      Assert.That(color.GetRGB(), Is.EqualTo(red));
    }
  }
}
