// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Linq;

// NUnit
using NUnit.Framework;

//
using OCC.Core;
using OCC.Core.BRepBuilderAPI;
using OCC.Core.BRepPrimAPI;
using OCC.Core.GeomAPI;
using OCC.Core.gp;
using OCC.Core.math;
using OCC.Core.ShapeAnalysis;
using OCC.Core.TColgp;
using OCC.Core.TColStd;
using OCC.Core.TopExp;
using OCC.Core.TopTools;

// static usings
using static OCC.Core.TopAbs.TopAbs_ShapeEnum;

namespace NetOcc.Tests;

/// <summary>
/// Collection instantiations, named by OCCT's aliases: indexers, enumeration, bulk copies,
/// handle-managed variants, maps, math vectors.
/// </summary>
[TestFixture]
public class CollectionTests
{
  private static readonly gp_Pnt[] Points =
    [new(0, 0, 0), new(1, 2, 0), new(2, 3, 1), new(3, 2, 2), new(4, 0, 2)];

  [Test]
  public void Array1_RoundTripsAStructArray()
  {
    // Arrange
    using var array = new TColgp_Array1OfPnt(Points);

    // Act
    var copy = array.ToArray();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(array.Lower(), Is.EqualTo(1));
      Assert.That(array.Upper(), Is.EqualTo(Points.Length));
      Assert.That(array[2], Is.EqualTo(Points[1]), "OCCT's bounds: from Lower()");
      Assert.That(copy, Is.EqualTo(Points));
      Assert.That(array, Is.EqualTo(Points), "IEnumerable, Lower() to Upper()");
    }
  }

  [Test]
  public void Array1_IndexerSetsElements()
  {
    // Arrange
    using var values = new TColStd_Array1OfReal(0, 2);
    values.Init(1.5);

    // Act
    values[2] = 4.0;

    // Assert
    Assert.That(values.ToArray(), Is.EqualTo(new[] { 1.5, 1.5, 4.0 }));
  }

  [TestCase(0)]
  [TestCase(4)]
  public void Array1_IndexOutsideTheBounds_Throws(int index)
  {
    // Arrange
    using var values = new TColStd_Array1OfReal(1, 3);

    // Act
    Action read = () => _ = values[index];

    // Assert
    Assert.That(
      read,
      Throws.TypeOf<OcctException>()
        .With.Property(nameof(OcctException.OcctType))
        .EqualTo("Standard_OutOfRange"));
  }

  [Test]
  public void Array1_IsTheInputOfAnApproximation()
  {
    // Arrange
    using var points = new TColgp_Array1OfPnt(Points);

    // Act
    var curve = new GeomAPI_PointsToBSpline(points).Curve();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(curve.StartPoint().Distance(Points[0]), Is.LessThan(1e-3));
      Assert.That(curve.EndPoint().Distance(Points[Points.Length - 1]), Is.LessThan(1e-3));
    }
  }

  [Test]
  public void HArray1_PassesAsAHandle()
  {
    // Arrange
    using var points = new TColgp_HArray1OfPnt(1, Points.Length);
    for (var i = 0; i < Points.Length; i++)
    {
      points[i + 1] = Points[i];
    }

    // Act
    var interpolation = new GeomAPI_Interpolate(points, false, 1e-7);
    interpolation.Perform();
    var poles = interpolation.Curve().Poles();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(interpolation.IsDone(), Is.True);
      Assert.That(interpolation.Curve().StartPoint().Distance(Points[0]), Is.LessThan(1e-9),
                  "the curve passes through the points");
      Assert.That(poles.Count, Is.EqualTo(interpolation.Curve().NbPoles()),
                  "a const& return is an owned copy");
      Assert.That(poles[1].Distance(Points[0]), Is.LessThan(1e-9),
                  "the curve is clamped at its first point");
    }
  }

  [Test]
  public void Array2_IndexerChecksTheColumn()
  {
    // Arrange
    using var grid = new TColgp_Array2OfPnt(1, 2, 1, 3);
    grid[1, 3] = new gp_Pnt(1, 3, 0);

    // Act
    Action pastTheRow = () => _ = grid[1, 4];

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(grid[1, 3], Is.EqualTo(new gp_Pnt(1, 3, 0)));
      Assert.That(pastTheRow, Throws.TypeOf<OcctException>(),
                  "OCCT itself would read row 2, column 1");
    }
  }

  [Test]
  public void Sequence_IsOneBased()
  {
    // Arrange
    using var sequence = new TColgp_SequenceOfPnt();
    foreach (var point in Points)
    {
      sequence.Append(point);
    }

    // Act
    sequence[1] = new gp_Pnt(-1, 0, 0);
    sequence.Remove(2);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(sequence.Count, Is.EqualTo(Points.Length - 1));
      Assert.That(sequence[1], Is.EqualTo(new gp_Pnt(-1, 0, 0)));
      Assert.That(sequence.Skip(1), Is.EqualTo(Points.Skip(2)));
    }
  }

  [Test]
  public void List_EnumeratesMoreThanOnce()
  {
    // Arrange
    using var list = new TopTools_ListOfShape();
    list.Append(new BRepBuilderAPI_MakeVertex(Points[1]).Vertex());
    list.Prepend(new BRepBuilderAPI_MakeVertex(Points[0]).Vertex());

    // Act
    var first = list.Select(shape => shape.ShapeType()).ToList();
    var second = list.Count();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(first, Is.EqualTo(new[] { TopAbs_VERTEX, TopAbs_VERTEX }));
      Assert.That(second, Is.EqualTo(2));
      Assert.That(list.First().IsSame(list.Last()), Is.False);
    }
  }

  [Test]
  public void HSequence_ComesBackAsAHandle()
  {
    // Arrange (the four sides of a square, as loose edges)
    using var edges = new TopTools_HSequenceOfShape();
    gp_Pnt[] corners = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)];
    for (var i = 0; i < corners.Length; i++)
    {
      edges.Append(
        new BRepBuilderAPI_MakeEdge(corners[i], corners[(i + 1) % corners.Length]).Edge());
    }

    // Act
    var wires = ShapeAnalysis_FreeBounds.ConnectEdgesToWires(edges, 1e-7, false);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(wires.Count, Is.EqualTo(1));
      Assert.That(wires[1].ShapeType(), Is.EqualTo(TopAbs_WIRE));
      Assert.That(Shapes.SubShapes(wires[1], TopAbs_EDGE), Has.Count.EqualTo(4));
    }
  }

  [Test]
  public void IndexedMap_NumbersTheShapesFromOne()
  {
    // Arrange
    var box = new BRepPrimAPI_MakeBox(1, 2, 3).Shape();
    using var faces = new TopTools_IndexedMapOfShape();

    // Act
    TopExp.MapShapes(box, TopAbs_FACE, faces);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(faces.Count, Is.EqualTo(6));
      Assert.That(faces[1].ShapeType(), Is.EqualTo(TopAbs_FACE));
      Assert.That(faces.FindIndex(faces[3]), Is.EqualTo(3));
      Assert.That(faces.Count(), Is.EqualTo(6), "IEnumerable, 1 to Extent()");
    }
  }

  [Test]
  public void IndexedDataMap_MapsEdgesToTheirFaces()
  {
    // Arrange
    var box = new BRepPrimAPI_MakeBox(1, 2, 3).Shape();
    using var ancestors = new TopTools_IndexedDataMapOfShapeListOfShape();

    // Act
    TopExp.MapShapesAndAncestors(box, TopAbs_EDGE, TopAbs_FACE, ancestors);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(ancestors.Count, Is.EqualTo(12));
      Assert.That(ancestors.Select(pair => pair.Value.Count), Is.All.EqualTo(2),
                  "every edge of a box bounds two faces");
      Assert.That(ancestors.Select(pair => pair.Key.ShapeType()), Is.All.EqualTo(TopAbs_EDGE));
    }
  }

  [Test]
  public void DataMap_FindsWhatWasBound()
  {
    // Arrange
    var box = new BRepPrimAPI_MakeBox(1, 2, 3).Shape();
    var faces = Shapes.SubShapes(box, TopAbs_FACE);
    using var numbers = new TopTools_DataMapOfShapeInteger();

    // Act
    for (var i = 0; i < faces.Count; i++)
    {
      numbers[faces[i]] = i;
    }

    var found = numbers.TryGetValue(faces[4], out var four);
    var edge = Shapes.SubShapes(box, TopAbs_EDGE)[0];

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(numbers.Count, Is.EqualTo(6));
      Assert.That(numbers[faces[2]], Is.EqualTo(2));
      Assert.That(found, Is.True);
      Assert.That(four, Is.EqualTo(4));
      Assert.That(numbers.ContainsKey(edge), Is.False);
      Assert.That(numbers.Select(pair => pair.Value).OrderBy(v => v),
                  Is.EqualTo(Enumerable.Range(0, 6)));
    }
  }

  [Test]
  public void DataMap_TakesStringKeys()
  {
    // Arrange
    using var counts = new TColStd_DataMapOfStringInteger();

    // Act
    counts["edges"] = 12;
    counts[NonAscii.Text] = 1;
    var missing = counts.TryGetValue("faces", out var faces);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(counts["edges"], Is.EqualTo(12));
      Assert.That(counts[NonAscii.Text], Is.EqualTo(1),
                  "keys are TCollection_ExtendedString, UTF-16");
      Assert.That(missing, Is.False);
      Assert.That(faces, Is.Zero);
    }
  }

  [Test]
  public void Map_HoldsEachShapeOnce()
  {
    // Arrange
    var box = new BRepPrimAPI_MakeBox(1, 2, 3).Shape();
    var face = Shapes.SubShapes(box, TopAbs_FACE)[0];
    using var map = new TopTools_MapOfShape();

    // Act
    var first = map.Add(face);
    var second = map.Add(face);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(first, Is.True);
      Assert.That(second, Is.False);
      Assert.That(map.Contains(face), Is.True);
      Assert.That(map.Single().IsSame(face), Is.True);
    }
  }

  [Test]
  public void MathVector_HasArithmeticOperators()
  {
    // Arrange
    using var v = new math_Vector([1.0, 2.0, 2.0]);

    // Act
    var dot = v * v;
    var sum = v + v;
    var scaled = 0.5 * v;

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(v.Norm(), Is.EqualTo(3.0).Within(1e-12));
      Assert.That(dot, Is.EqualTo(9.0), "vector * vector is the dot product");
      Assert.That(sum.ToArray(), Is.EqualTo(new[] { 2.0, 4.0, 4.0 }));
      Assert.That(scaled[1], Is.EqualTo(0.5));
    }
  }
}
