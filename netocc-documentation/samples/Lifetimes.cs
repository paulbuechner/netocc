// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BRepGraph;
using OCC.Core.BRepMesh;
using OCC.Core.BRepPrimAPI;
using OCC.Core.gp;
using OCC.Core.TopAbs;
using OCC.Core.TopExp;
using OCC.Core.TopoDS;
using OCC.Core.TopTools;

namespace NetOcc.Samples;

/// <summary>Object lifetimes: disposing, and what NetOcc keeps alive for you.</summary>
[TestFixture]
public class Lifetimes
{
  [Test]
  public void Dispose()
  {
    // Arrange
    var shape = new BRepPrimAPI_MakeSphere(10).Shape();

    // Act
    #region dispose
    // using disposes at the end of the scope: the native object goes right away
    using (var mesh = new BRepMesh_IncrementalMesh(shape, 0.01))
    {
      // the triangulation stays on the shape; the mesher itself is done
    }

    // without, the finalizer releases it when the garbage collector gets to it
    var another = new BRepPrimAPI_MakeBox(10, 10, 10);
    #endregion

    // Assert
    Assert.That(another.Shape().IsNull(), Is.False);
  }

  [Test]
  public void BorrowedReference()
  {
    // Act
    #region borrowed
    TopoDS_Shape first;
    {
      var edges = new TopTools_IndexedMapOfShape();
      TopExp.MapShapes(new BRepPrimAPI_MakeBox(10, 10, 10).Shape(), TopAbs_ShapeEnum.TopAbs_EDGE,
                       edges);
      first = edges.FindKey(1); // const TopoDS_Shape&: a proxy into the map
    }

    // the map is out of reach, but the proxy keeps it alive: no GC.KeepAlive needed
    GC.Collect();
    GC.WaitForPendingFinalizers();
    var type = first.ShapeType(); // TopAbs_EDGE
    #endregion

    // Assert
    Assert.That(type, Is.EqualTo(TopAbs_ShapeEnum.TopAbs_EDGE));
  }

  [Test]
  public void Guard()
  {
    // Arrange
    var graph = new BRepGraph();
    graph.Shapes().Add(new BRepPrimAPI_MakeBox(10, 10, 10).Shape());
    var vertex = graph.Topo().Vertices().StartId();

    // Act
    #region guard
    // Mut returns a guard that locks the vertex until it's disposed
    var editor = graph.Editor().Vertices();
    using (var guard = editor.Mut(vertex))
    {
      editor.SetPoint(guard, new gp_Pnt(-1, -1, -1));
    }
    #endregion

    // Assert
    Assert.That(graph.Topo().Vertices().Nb(), Is.EqualTo(8u));
  }
}
