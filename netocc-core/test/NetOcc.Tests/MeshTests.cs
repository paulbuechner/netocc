// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BRep;
using OCC.Core.BRepMesh;
using OCC.Core.Poly;
using OCC.Core.TopLoc;
using OCC.Core.TopoDS;

// static usings
using static OCC.Core.TopAbs.TopAbs_ShapeEnum;

namespace NetOcc.Tests;

/// <summary>
/// BRepMesh triangulates every face; the bulk copies of nodes and triangles match OCCT's
/// per-element accessors.
/// </summary>
[TestFixture]
public class MeshTests
{
  private static List<Poly_Triangulation> Triangulations(TopoDS_Shape shape) =>
    Shapes.SubShapes(shape, TopAbs_FACE)
      .Select(face => BRep_Tool.Triangulation(TopoDS.Face(face), new TopLoc_Location()))
      .ToList();

  [Test]
  public void IncrementalMesh_TriangulatesEveryFace()
  {
    // Arrange
    var shape = Shapes.Fused();

    // Act
    var mesh =
      new BRepMesh_IncrementalMesh(
        shape, 0.5); // an overload per argument count: OCCT's defaults for the rest
    var triangulations = Triangulations(shape);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mesh.IsDone(), Is.True);
      Assert.That(triangulations, Has.Count.GreaterThanOrEqualTo(7));
      Assert.That(triangulations, Has.All.Not.Null);
    }
  }

  [Test]
  public void NodesToArray_MatchesPerNodeCalls()
  {
    // Arrange
    var shape = Shapes.Fused();
    _ = new BRepMesh_IncrementalMesh(shape, 0.5);
    var triangulations = Triangulations(shape);

    // Act
    var bulk = triangulations.SelectMany(t => t.NodesToArray()).ToArray();
    var perNode = triangulations.SelectMany(t => Enumerable.Range(1, t.NbNodes()).Select(t.Node))
      .ToArray();

    // Assert
    Assert.That(bulk, Is.EqualTo(perNode));
  }

  [Test]
  public void TrianglesToArray_ReferencesExistingNodes()
  {
    // Arrange
    var shape = Shapes.Fused();
    _ = new BRepMesh_IncrementalMesh(shape, 0.5);
    var triangulations = Triangulations(shape);

    // Act
    var faces = triangulations.Select(t => new
      {
        NodeCount = t.NbNodes(), Triangles = t.TrianglesToArray(), First = t.Triangle(1)
      })
      .ToList();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      foreach (var face in faces)
      {
        Assert.That(face.Triangles, Is.Not.Empty);
        Assert.That(face.Triangles[0], Is.EqualTo(face.First));
        Assert.That(face.Triangles.SelectMany(t => new[] { t.Value(1), t.Value(2), t.Value(3) }),
                    Is.All.InRange(1, face.NodeCount));
      }
    }
  }
}
