// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BRep;
using OCC.Core.BRepBuilderAPI;
using OCC.Core.BRepPrimAPI;
using OCC.Core.Geom;
using OCC.Core.gp;
using OCC.Core.TopAbs;
using OCC.Core.TopExp;
using OCC.Core.TopLoc;
using OCC.Core.TopoDS;
using OCC.Core.TopTools;

namespace NetOcc.Samples;

/// <summary>Topology: TopoDS shapes, exploring them, and the geometry behind them.</summary>
[TestFixture]
public class Topology
{
  [Test]
  public void Explore()
  {
    // Arrange
    var box = new BRepPrimAPI_MakeBox(10, 20, 30).Shape();

    // Act
    #region explore
    // the explorer visits every occurrence: each edge of a box twice, once per face using it
    var occurrences = 0;
    for (var explorer = new TopExp_Explorer(box, TopAbs_ShapeEnum.TopAbs_EDGE);
         explorer.More();
         explorer.Next())
    {
      occurrences++;
    }

    // a map keeps each shape once
    var edges = new TopTools_IndexedMapOfShape();
    TopExp.MapShapes(box, TopAbs_ShapeEnum.TopAbs_EDGE, edges);
    var unique = edges.Extent();

    // foreach works too; ShapeType() tells what a TopoDS_Shape is, TopoDS.Face() casts it
    var areas = new List<TopoDS_Face>();
    foreach (var shape in new TopExp_Explorer(box, TopAbs_ShapeEnum.TopAbs_FACE))
    {
      if (shape.ShapeType() == TopAbs_ShapeEnum.TopAbs_FACE)
      {
        areas.Add(TopoDS.Face(shape));
      }
    }
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(occurrences, Is.EqualTo(24));
      Assert.That(unique, Is.EqualTo(12));
      Assert.That(areas, Has.Count.EqualTo(6));
    }
  }

  [Test]
  public void Ancestors()
  {
    // Arrange
    var box = new BRepPrimAPI_MakeBox(10, 20, 30).Shape();

    // Act
    #region ancestors
    // for each edge, the faces that use it
    var facesOfEdge = new TopTools_IndexedDataMapOfShapeListOfShape();
    TopExp.MapShapesAndAncestors(box, TopAbs_ShapeEnum.TopAbs_EDGE, TopAbs_ShapeEnum.TopAbs_FACE,
                                 facesOfEdge);

    var firstEdge = facesOfEdge.FindKey(1);
    var neighbours = facesOfEdge.FindFromIndex(1).Extent(); // 2: a box's edges join two faces
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(firstEdge.ShapeType(), Is.EqualTo(TopAbs_ShapeEnum.TopAbs_EDGE));
      Assert.That(neighbours, Is.EqualTo(2));
      Assert.That(facesOfEdge.Extent(), Is.EqualTo(12));
    }
  }

  [Test]
  public void Geometry()
  {
    // Arrange
    var box = new BRepPrimAPI_MakeBox(10, 20, 30).Shape();
    var vertex = TopoDS.Vertex(new TopExp_Explorer(box, TopAbs_ShapeEnum.TopAbs_VERTEX).Current());
    var edge = TopoDS.Edge(new TopExp_Explorer(box, TopAbs_ShapeEnum.TopAbs_EDGE).Current());
    var face = TopoDS.Face(new TopExp_Explorer(box, TopAbs_ShapeEnum.TopAbs_FACE).Current());

    // Act
    #region geometry
    // BRep_Tool reads the geometry behind a vertex, an edge and a face
    var point = BRep_Tool.Pnt(vertex);

    double first = 0, last = 0;
    var curve =
      BRep_Tool.Curve(edge, ref first, ref last); // the edge is the curve between first and last
    var start = curve.Value(first);

    var surface = BRep_Tool.Surface(face);
    var plane = Geom_Plane.DownCast(surface); // a box's faces are planes
    var tolerance = BRep_Tool.Tolerance(face);
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(point.Distance(new gp_Pnt(0, 0, 0)), Is.LessThanOrEqualTo(40));
      Assert.That(last, Is.GreaterThan(first));
      Assert.That(start.Distance(BRep_Tool.Pnt(TopExp.FirstVertex(edge, true))), Is.LessThan(1e-6));
      Assert.That(plane, Is.Not.Null);
      Assert.That(tolerance, Is.LessThan(1e-3));
    }
  }

  [Test]
  public void Identity()
  {
    // Arrange
    var box = new BRepPrimAPI_MakeBox(10, 10, 10).Shape();

    // Act
    #region identity
    var shift = new gp_Trsf();
    shift.SetTranslation(new gp_Vec(100, 0, 0));
    var moved = box.Moved(new TopLoc_Location(shift)); // same TShape, another location
    var reversed = box.Reversed(); // same TShape and location, other orientation

    var partners = box.IsPartner(moved); // true: same TShape
    var same = box.IsSame(reversed);     // true: same TShape and location
    var equal = box.IsEqual(reversed);   // false: the orientation differs
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(partners, Is.True);
      Assert.That(box.IsSame(moved), Is.False);
      Assert.That(same, Is.True);
      Assert.That(equal, Is.False);
    }
  }

  [Test]
  public void BottomUp()
  {
    // Act
    #region bottom-up
    // edges from points, a wire from edges, a face from the wire
    var a = new gp_Pnt(0, 0, 0);
    var b = new gp_Pnt(40, 0, 0);
    var c = new gp_Pnt(40, 10, 0);
    var d = new gp_Pnt(0, 10, 0);

    var wire = new BRepBuilderAPI_MakeWire(new BRepBuilderAPI_MakeEdge(a, b).Edge(),
                                           new BRepBuilderAPI_MakeEdge(b, c).Edge(),
                                           new BRepBuilderAPI_MakeEdge(c, d).Edge(),
                                           new BRepBuilderAPI_MakeEdge(d, a).Edge()).Wire();
    var face = new BRepBuilderAPI_MakeFace(wire).Face();

    // the same rectangle in one go
    var polygon = new BRepBuilderAPI_MakePolygon(a, b, c, d, true).Wire();
    #endregion

    // Assert
    var edges = new TopTools_IndexedMapOfShape();
    TopExp.MapShapes(polygon, TopAbs_ShapeEnum.TopAbs_EDGE, edges);
    using (Assert.EnterMultipleScope())
    {
      Assert.That(face.IsNull(), Is.False);
      Assert.That(BRep_Tool.IsClosed(wire), Is.True);
      Assert.That(edges.Extent(), Is.EqualTo(4));
    }
  }

  [Test]
  public void Compound()
  {
    // Act
    #region compound
    // a compound groups shapes of any type; BRep_Builder fills it
    var builder = new BRep_Builder();
    var compound = new TopoDS_Compound();
    builder.MakeCompound(compound);
    builder.Add(compound, new BRepPrimAPI_MakeBox(10, 10, 10).Shape());
    builder.Add(compound, new BRepPrimAPI_MakeSphere(new gp_Pnt(30, 0, 0), 5).Shape());
    #endregion

    // Assert
    var solids = new TopTools_IndexedMapOfShape();
    TopExp.MapShapes(compound, TopAbs_ShapeEnum.TopAbs_SOLID, solids);
    Assert.That(solids.Extent(), Is.EqualTo(2));
  }
}
