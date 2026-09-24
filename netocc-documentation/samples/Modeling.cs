// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BRep;
using OCC.Core.BRepAlgoAPI;
using OCC.Core.BRepBuilderAPI;
using OCC.Core.BRepFilletAPI;
using OCC.Core.BRepGProp;
using OCC.Core.BRepOffsetAPI;
using OCC.Core.BRepPrimAPI;
using OCC.Core.GC;
using OCC.Core.GProp;
using OCC.Core.gp;
using OCC.Core.TopAbs;
using OCC.Core.TopExp;
using OCC.Core.TopLoc;
using OCC.Core.TopoDS;
using OCC.Core.TopTools;

namespace NetOcc.Samples;

/// <summary>Modeling: primitives, sweeps, booleans, fillets and offsets.</summary>
[TestFixture]
public class Modeling
{
  private static double Volume(TopoDS_Shape shape)
  {
    var properties = new GProp_GProps();
    BRepGProp.VolumeProperties(shape, properties);
    return properties.Mass();
  }

  private static int Count(TopoDS_Shape shape, TopAbs_ShapeEnum type)
  {
    var map = new TopTools_IndexedMapOfShape();
    TopExp.MapShapes(shape, type, map);
    return map.Extent();
  }

  [Test]
  public void Primitives()
  {
    // Act
    #region primitives
    var z = new gp_Ax2(new gp_Pnt(0, 0, 0), new gp_Dir(0, 0, 1));

    var box = new BRepPrimAPI_MakeBox(new gp_Pnt(-5, -5, 0), 10, 10, 10)
      .Shape();                                                    // corner, then sizes
    var cylinder = new BRepPrimAPI_MakeCylinder(z, 5, 20).Shape(); // axis, radius, height
    var cone = new BRepPrimAPI_MakeCone(z, 5, 2, 10).Shape();      // two radii, height
    var sphere = new BRepPrimAPI_MakeSphere(new gp_Pnt(0, 0, 0), 5).Shape();
    var torus = new BRepPrimAPI_MakeTorus(z, 10, 2).Shape();      // two radii
    var wedge = new BRepPrimAPI_MakeWedge(10, 10, 10, 5).Shape(); // a box with a narrower top
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(Volume(box), Is.EqualTo(1000).Within(1e-6));
      Assert.That(Volume(cylinder), Is.EqualTo(Math.PI * 25 * 20).Within(1e-6));
      Assert.That(Volume(cone), Is.EqualTo(Math.PI * 10 / 3 * (25 + 10 + 4)).Within(1e-6));
      Assert.That(Volume(sphere), Is.EqualTo(4.0 / 3 * Math.PI * 125).Within(1e-6));
      Assert.That(Volume(torus), Is.EqualTo(2 * Math.PI * Math.PI * 10 * 4).Within(1e-6));
      Assert.That(Volume(wedge), Is.LessThan(1000));
    }
  }

  [Test]
  public void Sweeps()
  {
    // Act
    #region sweeps
    // a profile: a 10 x 4 rectangle in the XZ plane, 20 away from the Z axis
    var profile = new BRepBuilderAPI_MakeFace(new BRepBuilderAPI_MakePolygon(
                                                  new gp_Pnt(20, 0, 0), new gp_Pnt(30, 0, 0),
                                                  new gp_Pnt(30, 0, 4), new gp_Pnt(20, 0, 4), true)
                                                .Wire()).Face();

    // extruded along a vector, and revolved a full turn about Z (a ring)
    var bar = new BRepPrimAPI_MakePrism(profile, new gp_Vec(0, 50, 0)).Shape();
    var ring =
      new BRepPrimAPI_MakeRevol(profile, new gp_Ax1(new gp_Pnt(0, 0, 0), new gp_Dir(0, 0, 1)))
        .Shape();

    // swept along a path: an arc through three points
    var arc = new GC_MakeArcOfCircle(new gp_Pnt(25, 0, 2), new gp_Pnt(45, 20, 2),
                                     new gp_Pnt(65, 0, 2)).Value();
    var spine = new BRepBuilderAPI_MakeWire(new BRepBuilderAPI_MakeEdge(arc).Edge()).Wire();
    var circle = new BRepBuilderAPI_MakeWire(new BRepBuilderAPI_MakeEdge(
                                               new GC_MakeCircle(
                                                 new gp_Ax2(new gp_Pnt(25, 0, 2),
                                                            new gp_Dir(0, 1, 0)),
                                                 1.5).Value()).Edge()).Wire();
    var pipe = new BRepOffsetAPI_MakePipe(spine, new BRepBuilderAPI_MakeFace(circle).Face())
      .Shape();

    // lofted through sections: a square that turns into a circle
    var loft = new BRepOffsetAPI_ThruSections(true); // true: a solid
    loft.AddWire(new BRepBuilderAPI_MakePolygon(new gp_Pnt(-5, -5, 0), new gp_Pnt(5, -5, 0),
                                                new gp_Pnt(5, 5, 0), new gp_Pnt(-5, 5, 0), true)
                   .Wire());
    loft.AddWire(new BRepBuilderAPI_MakeWire(new BRepBuilderAPI_MakeEdge(
                                               new GC_MakeCircle(
                                                 new gp_Ax2(new gp_Pnt(0, 0, 20),
                                                            new gp_Dir(0, 0, 1)),
                                                 4).Value()).Edge()).Wire());
    var transition = loft.Shape();
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(Volume(bar), Is.EqualTo(10 * 4 * 50).Within(1e-6));
      Assert.That(Volume(ring), Is.EqualTo(Math.PI * (30 * 30 - 20 * 20) * 4).Within(1e-6));
      Assert.That(Volume(pipe), Is.GreaterThan(0));
      Assert.That(Count(transition, TopAbs_ShapeEnum.TopAbs_SOLID), Is.EqualTo(1));
    }
  }

  [Test]
  public void Booleans()
  {
    // Arrange
    var box = new BRepPrimAPI_MakeBox(new gp_Pnt(-10, -10, -10), 20, 20, 20).Shape();
    var sphere = new BRepPrimAPI_MakeSphere(new gp_Pnt(0, 0, 0), 15).Shape();

    // Act
    #region booleans
    var fused = new BRepAlgoAPI_Fuse(box, sphere).Shape();    // box ∪ sphere
    var common = new BRepAlgoAPI_Common(box, sphere).Shape(); // box ∩ sphere
    var cut = new BRepAlgoAPI_Cut(box, sphere).Shape();       // box − sphere: the eight corners

    // Section gives the intersection curves as edges
    var section = new BRepAlgoAPI_Section(box, sphere).Shape();
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(Volume(fused) + Volume(common),
                  Is.EqualTo(Volume(box) + Volume(sphere)).Within(1e-3));
      Assert.That(Volume(cut) + Volume(common), Is.EqualTo(Volume(box)).Within(1e-3));
      Assert.That(Count(cut, TopAbs_ShapeEnum.TopAbs_SOLID), Is.EqualTo(8));
      Assert.That(Count(section, TopAbs_ShapeEnum.TopAbs_EDGE), Is.GreaterThan(0));
    }
  }

  [Test]
  public void BooleanArguments()
  {
    // Act
    #region boolean-arguments
    // several tools at once: one cut drills all four holes
    var plate = new BRepPrimAPI_MakeBox(100, 60, 10).Shape();

    var arguments = new TopTools_ListOfShape();
    arguments.Append(plate);
    var tools = new TopTools_ListOfShape();
    foreach (var (x, y) in new[] { (10, 10), (90, 10), (10, 50), (90, 50) })
    {
      tools.Append(
        new BRepPrimAPI_MakeCylinder(new gp_Ax2(new gp_Pnt(x, y, -1), new gp_Dir(0, 0, 1)), 3, 12)
          .Shape());
    }

    var cut = new BRepAlgoAPI_Cut();
    cut.SetArguments(arguments);
    cut.SetTools(tools);
    cut.SetFuzzyValue(1e-5); // tolerate near-coincident faces
    cut.Build();
    if (cut.HasErrors())
    {
      throw new InvalidOperationException("the cut failed");
    }

    var drilled = cut.Shape();
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(Volume(drilled), Is.EqualTo(100 * 60 * 10 - 4 * Math.PI * 9 * 10).Within(1e-3));
      Assert.That(Count(drilled, TopAbs_ShapeEnum.TopAbs_FACE), Is.EqualTo(6 + 4));
    }
  }

  [Test]
  public void History()
  {
    // Arrange
    var maker = new BRepPrimAPI_MakeBox(20, 20, 20);
    var box = maker.Shape();
    var hole =
      new BRepPrimAPI_MakeCylinder(new gp_Ax2(new gp_Pnt(10, 10, -1), new gp_Dir(0, 0, 1)), 4, 22)
        .Shape();

    // Act
    #region history
    var top = maker.TopFace(); // the box's face at z = 20
    var cut = new BRepAlgoAPI_Cut(box, hole);

    var topAfter = cut.Modified(top);   // the top face with a hole in it
    var fromHole = cut.Generated(hole); // nothing: a boolean modifies faces, it doesn't sweep them
    var deleted = cut.IsDeleted(top);   // false: the top face is still there, modified
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(topAfter.Extent(), Is.EqualTo(1));
      Assert.That(fromHole.Extent(), Is.Zero);
      Assert.That(deleted, Is.False);
    }
  }

  [Test]
  public void FilletsAndChamfers()
  {
    // Arrange
    var maker = new BRepPrimAPI_MakeBox(20, 20, 20);
    var box = maker.Shape();

    // Act
    #region fillets
    // round the four vertical edges only: those whose ends differ in z alone
    var fillet = new BRepFilletAPI_MakeFillet(box);
    var edges = new TopTools_IndexedMapOfShape();
    TopExp.MapShapes(box, TopAbs_ShapeEnum.TopAbs_EDGE, edges);
    foreach (var shape in edges)
    {
      var edge = TopoDS.Edge(shape);
      var start = BRep_Tool.Pnt(TopExp.FirstVertex(edge));
      var end = BRep_Tool.Pnt(TopExp.LastVertex(edge));
      if (Math.Abs(start.X() - end.X()) < 1e-9 && Math.Abs(start.Y() - end.Y()) < 1e-9)
      {
        fillet.Add(3, edge);
      }
    }

    var rounded = fillet.Shape();

    // chamfer the edges of the top face by 1: the maker names the box's faces
    var chamfer = new BRepFilletAPI_MakeChamfer(box);
    foreach (var edge in new TopExp_Explorer(maker.TopFace(), TopAbs_ShapeEnum.TopAbs_EDGE))
    {
      chamfer.Add(1, TopoDS.Edge(edge));
    }

    var chamfered = chamfer.Shape();
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(fillet.NbContours(), Is.EqualTo(4));
      Assert.That(Volume(rounded), Is.EqualTo(8000 - 4 * (9 - Math.PI * 9 / 4) * 20).Within(1e-3));
      Assert.That(Volume(chamfered),
                  Is.EqualTo(8000 - 4 * 0.5 * 20)
                    .Within(2)); // four prisms of 0.5 x 20, overlapping at the corners
    }
  }

  [Test]
  public void Shell()
  {
    // Arrange
    var maker = new BRepPrimAPI_MakeBox(40, 30, 20);
    var box = maker.Shape();

    // Act
    #region shell
    // hollow the box to 2 thick walls, open at the top
    var removed = new TopTools_ListOfShape();
    removed.Append(maker.TopFace());
    var thick = new BRepOffsetAPI_MakeThickSolid();
    thick.MakeThickSolidByJoin(box, removed, -2, 1e-3); // negative: the walls grow inwards
    var tray = thick.Shape();
    #endregion

    // Assert
    Assert.That(Volume(tray), Is.EqualTo(40 * 30 * 20 - 36 * 26 * 18).Within(1e-3));
  }

  [Test]
  public void Transform()
  {
    // Arrange
    var box = new BRepPrimAPI_MakeBox(10, 10, 10).Shape();

    // Act
    #region transform
    var trsf = new gp_Trsf();
    trsf.SetRotation(new gp_Ax1(new gp_Pnt(0, 0, 0), new gp_Dir(0, 0, 1)), Math.PI / 4);

    // Moved shares the geometry and only sets a location: cheap
    var placed = box.Moved(new TopLoc_Location(trsf));

    // BRepBuilderAPI_Transform copies (true) the geometry, and handles scaling too
    var scale = new gp_Trsf();
    scale.SetScale(new gp_Pnt(0, 0, 0), 2);
    var doubled = new BRepBuilderAPI_Transform(box, scale, true).Shape();
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(Volume(placed), Is.EqualTo(1000).Within(1e-6));
      Assert.That(Volume(doubled), Is.EqualTo(8000).Within(1e-6));
    }
  }
}
