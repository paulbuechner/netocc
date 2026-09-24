// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

// NUnit
using NUnit.Framework;

//
using OCC.Core;
using OCC.Core.GCPnts;
using OCC.Core.Geom;
using OCC.Core.GeomAdaptor;
using OCC.Core.GeomAPI;
using OCC.Core.gp;
using OCC.Core.TColgp;

namespace NetOcc.Samples;

/// <summary>Geometry: gp's value types, Geom's curves and surfaces.</summary>
[TestFixture]
public class Geometry
{
  [Test]
  public void ValueTypes()
  {
    // Act
    #region value-types
    var p = new gp_Pnt(1, 2, 3);
    var q = new gp_Pnt(4, 6, 3);
    var distance = p.Distance(q); // 5

    var along = new gp_Vec(p, q); // from p to q
    var up = new gp_Vec(0, 0, 1);
    var normal = along ^ up;                 // ^ is the cross product
    var halfway = p.Translated(along * 0.5); // Translated returns a new point, p stays

    var x = new gp_Dir(1, 0, 0);              // a direction is always unit length
    var angle = x.Angle(new gp_Dir(0, 1, 0)); // π/2
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(distance, Is.EqualTo(5).Within(1e-12));
      Assert.That(normal.Z(), Is.EqualTo(0).Within(1e-12));
      Assert.That(halfway.X(), Is.EqualTo(2.5).Within(1e-12));
      Assert.That(angle, Is.EqualTo(Math.PI / 2).Within(1e-12));
      Assert.That(p.X(), Is.EqualTo(1));
    }
  }

  [Test]
  public void Transformations()
  {
    // Act
    #region transformations
    var rotation = new gp_Trsf();
    rotation.SetRotation(new gp_Ax1(new gp_Pnt(0, 0, 0), new gp_Dir(0, 0, 1)),
                         Math.PI / 2); // 90° about Z

    var shift = new gp_Trsf();
    shift.SetTranslation(new gp_Vec(10, 0, 0));

    var both = shift * rotation;                       // rotate, then shift
    var moved = new gp_Pnt(1, 0, 0).Transformed(both); // (10, 1, 0)
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(moved.X(), Is.EqualTo(10).Within(1e-12));
      Assert.That(moved.Y(), Is.EqualTo(1).Within(1e-12));
    }
  }

  [Test]
  public void StructCopies()
  {
    // Act
    #region struct-copies
    var point = new gp_Pnt(1, 1, 1);
    var copy = point; // a copy: gp types are C# structs
    copy.SetX(5);     // changes the copy only

    point.Translate(new gp_Vec(1, 0, 0));                   // Translate changes point itself...
    var translated = point.Translated(new gp_Vec(1, 0, 0)); // ...Translated returns a new one
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(point.X(), Is.EqualTo(2));
      Assert.That(copy.X(), Is.EqualTo(5));
      Assert.That(translated.X(), Is.EqualTo(3));
    }
  }

  [Test]
  public void Curves()
  {
    // Act
    #region curves
    // a circle of radius 10 in the XY plane, and a B-spline through four points
    Geom_Curve circle = new Geom_Circle(new gp_Ax2(new gp_Pnt(0, 0, 0), new gp_Dir(0, 0, 1)), 10);
    var points = new TColgp_Array1OfPnt([
      new gp_Pnt(0, 0, 0), new gp_Pnt(10, 5, 0), new gp_Pnt(20, 0, 0), new gp_Pnt(30, 5, 0)
    ]);
    Geom_BSplineCurve spline = new GeomAPI_PointsToBSpline(points).Curve();

    // a point and a derivative on the circle, at a quarter turn
    var atQuarter = circle.Value(Math.PI / 2); // (0, 10, 0)
    var d1 = circle.EvalD1(Math.PI / 2);       // OCCT 8: the point and first derivative together
    var tangent = d1.D1;                       // (-10, 0, 0)

    // lengths come from an adaptor, which algorithms take instead of the curve
    var length = GCPnts_AbscissaPoint.Length(new GeomAdaptor_Curve(circle)); // 2π·10
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(atQuarter.Y(), Is.EqualTo(10).Within(1e-9));
      Assert.That(tangent.X(), Is.EqualTo(-10).Within(1e-9));
      Assert.That(length, Is.EqualTo(2 * Math.PI * 10).Within(1e-6));
      Assert.That(spline.Degree(), Is.GreaterThanOrEqualTo(1));
    }
  }

  [Test]
  public void HandlesAndDownCast()
  {
    // Act
    #region downcast
    Geom_Curve curve = new Geom_Circle(new gp_Ax2(), 10); // a Handle(Geom_Curve) in C++
    var circle = Geom_Circle.DownCast(curve);             // the circle, or null
    var line = Geom_Line.DownCast(curve);                 // null: it isn't a line
    var radius = circle?.Radius();
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(radius, Is.EqualTo(10));
      Assert.That(line, Is.Null);
    }
  }

  [Test]
  public void Projection()
  {
    // Act
    #region projection
    Geom_Curve circle = new Geom_Circle(new gp_Ax2(), 10);
    var projection = new GeomAPI_ProjectPointOnCurve(new gp_Pnt(20, 0, 5), circle);
    var nearest = projection.NearestPoint(); // (10, 0, 0)
    var gap = projection.LowerDistance();    // √(10² + 5²)
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(nearest.X(), Is.EqualTo(10).Within(1e-9));
      Assert.That(gap, Is.EqualTo(Math.Sqrt(125)).Within(1e-9));
    }
  }

  [Test]
  public void ZeroDirectionThrows()
  {
    // Act
    #region zero-direction
    Action zero = () => new gp_Dir(0, 0, 0); // OCCT raises Standard_ConstructionError
    #endregion

    // Assert
    Assert.That(
      zero,
      Throws.TypeOf<OcctException>()
        .With.Property(nameof(OcctException.OcctType))
        .EqualTo("Standard_ConstructionError"));
  }
}
