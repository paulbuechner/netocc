// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Linq;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BOPTools;
using OCC.Core.BRep;
using OCC.Core.Geom;
using OCC.Core.Geom2d;
using OCC.Core.gp;
using OCC.Core.TopoDS;

// static usings
using static OCC.Core.TopAbs.TopAbs_ShapeEnum;

namespace NetOcc.Tests;

/// <summary>
/// Transient objects: each proxy owns one reference; Handle(T)::DownCast maps to T.DownCast; a
/// Handle(T)&amp; is a ref variable that keeps its proxy unless the callee puts another object
/// there.
/// </summary>
[TestFixture]
public class HandleTests
{
  private static TopoDS_Face FirstFace(TopoDS_Shape shape) =>
    TopoDS.Face(Shapes.SubShapes(shape, TopAbs_FACE)[0]);

  [Test]
  public void DownCast_FindsTheConcreteSurface()
  {
    // Arrange
    Geom_Surface surface = BRep_Tool.Surface(FirstFace(Shapes.Box()));

    // Act
    var plane = Geom_Plane.DownCast(surface);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(surface.DynamicType().Name(), Is.EqualTo("Geom_Plane"));
      Assert.That(plane, Is.Not.Null);
    }
  }

  [Test]
  public void HandleReference_KeepsItsProxyWhenAnEarlierArgumentFails()
  {
    // Arrange (the null edge fails SWIG's argument check, which returns before the rest of the
    // wrapper)
    var face = FirstFace(Shapes.Box());
    Geom2d_Curve curve = new Geom2d_Line(new gp_Pnt2d(0, 0), new gp_Dir2d(1, 0));
    var original = curve;
    double first = 0, last = 0, tolerance = 0;

    // Act
    Action call = () =>
      BOPTools_AlgoTools2D.HasCurveOnSurface(null!, face, ref curve, ref first, ref last,
                                             ref tolerance);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(call, Throws.TypeOf<ArgumentNullException>());
      Assert.That(curve, Is.SameAs(original));
    }
  }

  [Test]
  public void DynamicType_IsOcctsTypeDescriptor()
  {
    // Arrange
    Geom_Surface surface = BRep_Tool.Surface(FirstFace(Shapes.Box()));

    // Act
    var type = surface.DynamicType();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(type.Name(), Is.EqualTo("Geom_Plane"));
      Assert.That(type.Parent().Name(), Is.EqualTo("Geom_ElementarySurface"));
      Assert.That(surface.IsKind(Geom_Surface.get_type_descriptor()), Is.True);
      Assert.That(surface.IsInstance(Geom_Surface.get_type_descriptor()), Is.False,
                  "an instance of Geom_Plane only");
    }
  }

  [Test]
  public void DownCast_ReturnsNullOnMismatch()
  {
    // Arrange
    Geom_Surface surface = BRep_Tool.Surface(FirstFace(Shapes.Box()));

    // Act
    var cylinder = Geom_CylindricalSurface.DownCast(surface);

    // Assert
    Assert.That(cylinder, Is.Null);
  }

  [Test]
  public void CylindricalSurface_HasTheCylinderRadius()
  {
    // Arrange
    var surfaces = Shapes.SubShapes(Shapes.Cylinder(), TopAbs_FACE)
      .Select(face => BRep_Tool.Surface(TopoDS.Face(face)));

    // Act
    var cylinders = surfaces.Select(Geom_CylindricalSurface.DownCast)
      .Where(cylinder => cylinder is not null)
      .ToList();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(cylinders, Has.Count.EqualTo(1));
      Assert.That(cylinders[0].Radius(), Is.EqualTo(3.0).Within(1e-12));
    }
  }

  [Test]
  public void Proxy_OwnsExactlyOneReference()
  {
    // Arrange (no finalizer may release a reference while counting: earlier garbage goes first, and
    // the face, whose TFace holds the surface, stays alive to the end)
    var face = FirstFace(Shapes.Box());
    Garbage.Collect();
    using var first = BRep_Tool.Surface(face);
    var baseline = first.GetRefCount();

    // Act
    var second = BRep_Tool.Surface(face);
    var whileBothAlive = first.GetRefCount();
    var sameObject = first.Equals(second);
    second.Dispose();
    var afterDispose = first.GetRefCount();
    GC.KeepAlive(face);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(second, Is.Not.SameAs(first));
      Assert.That(sameObject, Is.True, "both proxies wrap the same native object");
      Assert.That(whileBothAlive, Is.EqualTo(baseline + 1));
      Assert.That(afterDispose, Is.EqualTo(baseline));
    }
  }

  [Test]
  public void ConstructedTransient_StartsWithOneReference()
  {
    // Arrange
    var origin = new gp_Pnt(0, 0, 0);
    var normal = new gp_Dir(0, 0, 1);

    // Act
    using var plane = new Geom_Plane(origin, normal);

    // Assert
    Assert.That(plane.GetRefCount(), Is.EqualTo(1));
  }

  [Test]
  public void Plane_EvaluatesPointsAndIsUnbounded()
  {
    // Arrange
    using var plane = new Geom_Plane(new gp_Pnt(0, 0, 0), new gp_Dir(0, 0, 1));
    double u1 = 0, u2 = 0, v1 = 0, v2 = 0;

    // Act
    var point = plane.Value(1, 2); // (u, v) axes are chosen by OCCT; only the distance is fixed
    plane.Bounds(ref u1, ref u2, ref v1, ref v2);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(point.Z(), Is.EqualTo(0.0).Within(1e-12));
      Assert.That(point.Distance(new gp_Pnt(0, 0, 0)), Is.EqualTo(Math.Sqrt(5.0)).Within(1e-12));
      Assert.That(u2, Is.GreaterThan(1e50));
    }
  }

  [Test]
  public void Coefficients_WritesRefParameters()
  {
    // Arrange
    var plane = Geom_Plane.DownCast(BRep_Tool.Surface(FirstFace(Shapes.Box())));
    double a = 0, b = 0, c = 0, d = 0;

    // Act
    plane.Coefficients(ref a, ref b, ref c, ref d);

    // Assert
    Assert.That(Math.Sqrt(a * a + b * b + c * c), Is.EqualTo(1.0).Within(1e-9), "unit normal");
  }

  [Test]
  public void Range_WritesRefParameters()
  {
    // Arrange
    var edges = Shapes.SubShapes(Shapes.Box(), TopAbs_EDGE).Select(TopoDS.Edge).ToList();

    // Act
    var ranges = edges.Select(edge =>
      {
        double first = -1, last = -1;
        BRep_Tool.Range(edge, ref first, ref last);
        return new[] { first, last };
      })
      .ToList();

    // Assert
    Assert.That(ranges, Has.All.Matches<double[]>(range => range[1] > range[0]));
  }
}
