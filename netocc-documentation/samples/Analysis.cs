// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// NUnit
using NUnit.Framework;

//
using OCC.Core.Bnd;
using OCC.Core.BRepAlgoAPI;
using OCC.Core.BRepBndLib;
using OCC.Core.BRepBuilderAPI;
using OCC.Core.BRepCheck;
using OCC.Core.BRepClass3d;
using OCC.Core.BRepExtrema;
using OCC.Core.BRepGProp;
using OCC.Core.BRepPrimAPI;
using OCC.Core.GProp;
using OCC.Core.gp;
using OCC.Core.ShapeFix;
using OCC.Core.ShapeUpgrade;
using OCC.Core.TopAbs;
using OCC.Core.TopExp;
using OCC.Core.TopoDS;
using OCC.Core.TopTools;

namespace NetOcc.Samples;

/// <summary>
/// Analysis: properties, bounding boxes, distances, classification, checks and healing.
/// </summary>
[TestFixture]
public class Analysis
{
  private static int Count(TopoDS_Shape shape, TopAbs_ShapeEnum type)
  {
    var map = new TopTools_IndexedMapOfShape();
    TopExp.MapShapes(shape, type, map);
    return map.Extent();
  }

  [Test]
  public void Properties()
  {
    // Arrange
    var box = new BRepPrimAPI_MakeBox(10, 20, 30).Shape();

    // Act
    #region properties
    // volume properties: Mass() is the volume (density 1), with centre of mass and inertia
    var volume = new GProp_GProps();
    BRepGProp.VolumeProperties(box, volume);
    var centre = volume.CentreOfMass();     // (5, 10, 15)
    var inertia = volume.MatrixOfInertia(); // about the centre of mass

    // surface properties: Mass() is the area; linear properties: the length of the edges
    var surface = new GProp_GProps();
    BRepGProp.SurfaceProperties(box, surface);
    var linear = new GProp_GProps();
    BRepGProp.LinearProperties(box, linear, true); // true: each shared edge once, 12 not 24
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(volume.Mass(), Is.EqualTo(6000).Within(1e-6));
      Assert.That(centre.Distance(new gp_Pnt(5, 10, 15)), Is.LessThan(1e-9));
      Assert.That(inertia.Value(3, 3), Is.EqualTo(6000 * (10 * 10 + 20 * 20) / 12.0).Within(1e-3));
      Assert.That(surface.Mass(), Is.EqualTo(2200).Within(1e-6));
      Assert.That(linear.Mass(), Is.EqualTo(240).Within(1e-6));
    }
  }

  [Test]
  public void BoundingBox()
  {
    // Arrange
    var sphere = new BRepPrimAPI_MakeSphere(new gp_Pnt(0, 0, 0), 10).Shape();

    // Act
    #region bounding-box
    // Bnd_Box is a struct: BRepBndLib fills it through a ref
    var fast = new Bnd_Box();
    BRepBndLib.Add(sphere, ref fast); // quick, may be larger than the shape

    var tight = new Bnd_Box();
    BRepBndLib.AddOptimal(sphere, ref tight, false, false); // exact, slower
    var min = tight.CornerMin();                            // (-10, -10, -10)
    var max = tight.CornerMax();                            // (10, 10, 10)
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(fast.IsVoid(), Is.False);
      Assert.That(fast.CornerMax().X(), Is.GreaterThanOrEqualTo(max.X() - 1e-6));
      Assert.That(min.X(), Is.EqualTo(-10).Within(1e-6));
      Assert.That(max.Z(), Is.EqualTo(10).Within(1e-6));
    }
  }

  [Test]
  public void Distance()
  {
    // Arrange
    var a = new BRepPrimAPI_MakeBox(10, 10, 10).Shape();
    var b = new BRepPrimAPI_MakeSphere(new gp_Pnt(25, 5, 5), 5).Shape();

    // Act
    #region distance
    var extrema = new BRepExtrema_DistShapeShape(a, b);
    var gap = extrema.Value();          // 10: from x = 10 to the sphere at x = 20
    var onA = extrema.PointOnShape1(1); // the closest points, numbered from 1
    var onB = extrema.PointOnShape2(1);
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(extrema.IsDone(), Is.True);
      Assert.That(gap, Is.EqualTo(10).Within(1e-6));
      Assert.That(onA.X(), Is.EqualTo(10).Within(1e-6));
      Assert.That(onB.X(), Is.EqualTo(20).Within(1e-6));
    }
  }

  [Test]
  public void Classify()
  {
    // Arrange
    var box = new BRepPrimAPI_MakeBox(10, 10, 10).Shape();

    // Act
    #region classify
    var inside =
      new BRepClass3d_SolidClassifier(box, new gp_Pnt(5, 5, 5), 1e-7).State(); // TopAbs_IN
    var onFace =
      new BRepClass3d_SolidClassifier(box, new gp_Pnt(5, 5, 10), 1e-7).State(); // TopAbs_ON
    var outside =
      new BRepClass3d_SolidClassifier(box, new gp_Pnt(5, 5, 20), 1e-7).State(); // TopAbs_OUT
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(inside, Is.EqualTo(TopAbs_State.TopAbs_IN));
      Assert.That(onFace, Is.EqualTo(TopAbs_State.TopAbs_ON));
      Assert.That(outside, Is.EqualTo(TopAbs_State.TopAbs_OUT));
    }
  }

  [Test]
  public void SewAndFix()
  {
    // Arrange: six loose faces of a cube, none sharing an edge
    var corners = new[]
    {
      new gp_Pnt(0, 0, 0), new gp_Pnt(10, 0, 0), new gp_Pnt(10, 10, 0), new gp_Pnt(0, 10, 0),
      new gp_Pnt(0, 0, 10), new gp_Pnt(10, 0, 10), new gp_Pnt(10, 10, 10), new gp_Pnt(0, 10, 10),
    };
    var quads = new[]
      { (0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7) };
    var faces = new TopoDS_Face[quads.Length];
    for (var i = 0; i < quads.Length; i++)
    {
      var (a, b, c, d) = quads[i];
      faces[i] =
        new BRepBuilderAPI_MakeFace(
          new BRepBuilderAPI_MakePolygon(corners[a], corners[b], corners[c], corners[d], true)
            .Wire()).Face();
    }

    // Act
    #region sew-and-fix
    // sewing joins faces along edges that lie within the tolerance of each other
    var sewing = new BRepBuilderAPI_Sewing(1e-6);
    foreach (var face in faces)
    {
      sewing.Add(face);
    }

    sewing.Perform();
    var shell = TopoDS.Shell(sewing.SewedShape());

    // a solid from the closed shell; ShapeFix_Shape repairs what's left, orientation included
    var solid = new BRepBuilderAPI_MakeSolid(shell).Solid();
    var fix = new ShapeFix_Shape(solid);
    fix.Perform();
    var fixedSolid = fix.Shape();

    var valid = new BRepCheck_Analyzer(fixedSolid).IsValid();
    #endregion

    // Assert
    var volume = new GProp_GProps();
    BRepGProp.VolumeProperties(fixedSolid, volume);
    using (Assert.EnterMultipleScope())
    {
      Assert.That(Count(shell, TopAbs_ShapeEnum.TopAbs_EDGE), Is.EqualTo(12));
      Assert.That(valid, Is.True);
      Assert.That(volume.Mass(), Is.EqualTo(1000).Within(1e-6));
    }
  }

  [Test]
  public void Unify()
  {
    // Arrange
    var left = new BRepPrimAPI_MakeBox(10, 10, 10).Shape();
    var right = new BRepPrimAPI_MakeBox(new gp_Pnt(10, 0, 0), 10, 10, 10).Shape();

    // Act
    #region unify
    // two boxes side by side fuse into one solid, still with the seam's split faces
    var fused = new BRepAlgoAPI_Fuse(left, right).Shape(); // 10 faces

    // merge faces (and edges) that lie on the same surface
    var unify = new ShapeUpgrade_UnifySameDomain(fused, true, true, false);
    unify.Build();
    var merged = unify.Shape(); // 6 faces, a 20 x 10 x 10 box

    // booleans do the same when asked
    var fuse = new BRepAlgoAPI_Fuse(left, right);
    fuse.SimplifyResult();
    var simplified = fuse.Shape(); // 6 faces
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(Count(fused, TopAbs_ShapeEnum.TopAbs_FACE), Is.EqualTo(10));
      Assert.That(Count(merged, TopAbs_ShapeEnum.TopAbs_FACE), Is.EqualTo(6));
      Assert.That(Count(simplified, TopAbs_ShapeEnum.TopAbs_FACE), Is.EqualTo(6));
    }
  }
}
