// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BRepGraph;
using OCC.Core.Geom;

namespace NetOcc.Tests;

/// <summary>
/// OCCT 8's BRepGraph: typed ids (alias-named template instances) as structs, its iterators as
/// classes, which keep their graph alive.
/// </summary>
[TestFixture]
public class BRepGraphTests
{
  [Test]
  public void TypedId_RunsOcctsMembers()
  {
    // Arrange
    var id = new BRepGraph_FaceId(3); // BRepGraph_NodeId::Typed<Kind::Face>

    // Act
    var next = id + 2;
    var invalid = BRepGraph_FaceId.Invalid();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(next.Index, Is.EqualTo(5u));
      Assert.That(id.IsValid(), Is.True);
      Assert.That(invalid.IsValid(), Is.False);
    }
  }

  [Test]
  public void FaceIterator_KeepsItsGraphAlive()
  {
    // Arrange (the iterator refers to the graph natively; after BoxFaces, no C# variable holds the
    // graph)
    var iterator = BoxFaces(out var graph);
    Garbage.Collect();
    var faces = 0;

    // Act
    for (; iterator.More(); iterator.Next())
    {
      faces++;
    }

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(faces, Is.EqualTo(6));
      Assert.That(graph.IsAlive, Is.True);
    }
  }

  // the graph lives in this frame only
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static BRepGraph_FaceIterator BoxFaces(out WeakReference graphReference)
  {
    var graph = Shapes.BoxGraph();
    graphReference = new WeakReference(graph);
    return new BRepGraph_FaceIterator(graph);
  }

  [Test]
  public void FaceIterator_VisitsEveryFaceOfABox()
  {
    // Arrange
    var graph = Shapes.BoxGraph();
    var faces = 0;
    var planes = 0;

    // Act
    for (var face = new BRepGraph_FaceIterator(graph); face.More(); face.Next())
    {
      faces++;
      planes += Geom_Plane.DownCast(graph.Topo().Faces().Surface(face.CurrentId())) is null ? 0 : 1;
    }

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(faces, Is.EqualTo(6));
      Assert.That(graph.Topo().Faces().Nb(), Is.EqualTo(6u));
      Assert.That(planes, Is.EqualTo(6), "a box has planar faces");
    }
  }
}
