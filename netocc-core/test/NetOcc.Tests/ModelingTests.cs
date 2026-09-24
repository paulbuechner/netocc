// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Linq;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BRepAlgoAPI;
using OCC.Core.BRepBuilderAPI;
using OCC.Core.BRepGProp;
using OCC.Core.gp;
using OCC.Core.GProp;
using OCC.Core.TopoDS;

// static usings
using static OCC.Core.TopAbs.TopAbs_ShapeEnum;

namespace NetOcc.Tests;

/// <summary>
/// Modeling through the proxies: primitives, booleans and their history, a transform by a gp_Trsf,
/// shape equality.
/// </summary>
[TestFixture]
public class ModelingTests
{
  [Test]
  public void Box_IsASolidWithSixFacesAndItsVolume()
  {
    // Arrange
    var box = Shapes.Box();

    // Act
    var faces = Shapes.SubShapes(box, TopAbs_FACE);
    var volume = Shapes.Volume(box);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(box.ShapeType(), Is.EqualTo(TopAbs_SOLID));
      Assert.That(faces, Has.Count.EqualTo(6));
      Assert.That(volume, Is.EqualTo(Shapes.BoxVolume).Within(1e-6));
    }
  }

  [Test]
  public void Fuse_AddsTheProtrudingPartOfTheCylinder()
  {
    // Arrange
    var box = Shapes.Box();
    var cylinder = Shapes.Cylinder();

    // Act
    var fuse = new BRepAlgoAPI_Fuse(box, cylinder);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(fuse.HasErrors(), Is.False);
      Assert.That(Shapes.Volume(fuse.Shape()), Is.EqualTo(Shapes.FusedVolume).Within(1e-3));
    }
  }

  [Test]
  public void Cut_RemovesTheCylinderInsideTheBox()
  {
    // Arrange
    var box = Shapes.Box();
    var cylinder = Shapes.Cylinder();

    // Act
    var cut = new BRepAlgoAPI_Cut(box, cylinder);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(cut.HasErrors(), Is.False);
      Assert.That(Shapes.Volume(cut.Shape()), Is.EqualTo(Shapes.CutVolume).Within(1e-3));
    }
  }

  [Test]
  public void Transform_MovesTheShapeByTheValueTypeTrsf()
  {
    // Arrange
    var trsf = new gp_Trsf();
    trsf.SetTranslation(new gp_Vec(100, 0, 0));

    // Act
    var moved = new BRepBuilderAPI_Transform(Shapes.Box(), trsf, true).Shape();
    var props = new GProp_GProps();
    BRepGProp.VolumeProperties(moved, props);
    var centre = props.CentreOfMass();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(props.Mass(), Is.EqualTo(Shapes.BoxVolume).Within(1e-6));
      Assert.That(centre.X(), Is.EqualTo(105.0).Within(1e-9));
      Assert.That(centre.Y(), Is.EqualTo(10.0).Within(1e-9));
      Assert.That(centre.Z(), Is.EqualTo(15.0).Within(1e-9));
    }
  }

  [Test]
  public void Fuse_ModifiedShapesAreEnumerableFaces()
  {
    // Arrange
    var box = Shapes.Box();
    var fuse = new BRepAlgoAPI_Fuse(box, Shapes.Cylinder());

    // Act
    var modified = Shapes.SubShapes(box, TopAbs_FACE)
      .SelectMany(face => fuse.Modified(face))
      .ToList();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(modified, Is.Not.Empty, "the cylinder splits the top face");
      Assert.That(
        modified, Has.All.Matches<TopoDS_Shape>(shape => shape.ShapeType() == TopAbs_FACE));
    }
  }

  [Test]
  public void Equality_FollowsIsEqualAcrossProxies()
  {
    // Arrange
    var box = Shapes.Box();

    // Act
    var first = Shapes.SubShapes(box, TopAbs_FACE)[0];
    var again = Shapes.SubShapes(box, TopAbs_FACE)[0];
    var reversed = first.Reversed();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(again, Is.Not.SameAs(first));
      Assert.That(again, Is.EqualTo(first));
      Assert.That(again.GetHashCode(), Is.EqualTo(first.GetHashCode()));
      Assert.That(reversed.IsSame(first), Is.True);
      Assert.That(reversed, Is.Not.EqualTo(first));
    }
  }
}
