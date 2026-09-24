// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BRepGraph;
using OCC.Core.Extrema;
using OCC.Core.Geom;
using OCC.Core.GeomAdaptor;
using OCC.Core.gp;

namespace NetOcc.Tests;

/// <summary>
/// What a native object refers to stays alive with its proxy: arguments its members keep, the
/// object a by-value return points into or a view refers to, and, past the proxy's finalizer, what
/// its constructor keeps.
/// </summary>
[TestFixture]
public class LifetimeTests
{
  [Test]
  public void Member_KeepsTheArgumentItStores()
  {
    // Arrange (Initialize stores a pointer to the surface; after InitializeOnPlane, no C# variable
    // holds it)
    var extrema = new Extrema_ExtPS();
    var surface = InitializeOnPlane(extrema);
    Garbage.Collect();

    // Act
    extrema.Perform(new gp_Pnt(1, 2, 5));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(surface.IsAlive, Is.True);
      Assert.That(extrema.IsDone(), Is.True);
      Assert.That(extrema.SquareDistance(1), Is.EqualTo(25.0).Within(1e-9));
    }
  }

  [Test]
  public void Member_ReleasesWhatItsPreviousCallKept()
  {
    // Arrange (the second Initialize replaces the surface the object refers to)
    var extrema = new Extrema_ExtPS();
    var first = InitializeOnPlane(extrema);
    var second = InitializeOnPlane(extrema);

    // Act
    Collect(first);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(first.IsAlive, Is.False);
      Assert.That(second.IsAlive, Is.True);
    }

    GC.KeepAlive(extrema);
  }

  [Test]
  public void ByValueReturn_KeepsTheObjectItPointsInto()
  {
    // Arrange (the guard points into the graph; after MutFirstVertex, no C# variable holds the
    // graph or its editor)
    var guard = MutFirstVertex(out var graph, out var vertex);
    Garbage.Collect();

    // Act
    var index = guard.Id().Index;
    guard.Dispose();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(graph.IsAlive, Is.True);
      Assert.That(index, Is.EqualTo(vertex));
    }
  }

  [Test]
  public void ViewReturn_KeepsTheObjectItViews()
  {
    // Arrange (the topology view is a copy that refers to its graph; after TopoOfBoxGraph, no C#
    // variable holds the graph)
    var topo = TopoOfBoxGraph(out var graph);
    Garbage.Collect();

    // Act
    var faces = topo.Faces().Nb();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(graph.IsAlive, Is.True);
      Assert.That(faces, Is.EqualTo(6));
    }
  }

  [Test]
  public void ConstructorArgument_OutlivesItsKeepersFinalizer()
  {
    // Arrange (the iterator and its graph become garbage together: the iterator's destructor may
    // still use the graph)
    var graph = GarbageIterator(out var iterator);

    // Act (one collection: the iterator's finalizer has run, but the GC hasn't collected it yet)
    GC.Collect();
    GC.WaitForPendingFinalizers();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(iterator.IsAlive, Is.False, "the iterator is garbage");
      Assert.That(graph.IsAlive, Is.True, "the graph waits for the iterator to be collected");
    }
  }

  [Test]
  public void ConstructorArgument_IsReleasedAfterItsKeeper()
  {
    // Arrange
    var graph = GarbageIterator(out _);

    // Act
    Collect(graph);

    // Assert
    Assert.That(graph.IsAlive, Is.False);
  }

  // full collections until the object is gone, or ten
  private static void Collect(WeakReference reference)
  {
    for (var i = 0; i < 10 && reference.IsAlive; i++)
    {
      GC.Collect();
      GC.WaitForPendingFinalizers();
    }
  }

  // a surface in this frame only, which the extrema keeps
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static WeakReference InitializeOnPlane(Extrema_ExtPS extrema)
  {
    var surface = new GeomAdaptor_Surface(new Geom_Plane(new gp_Pnt(0, 0, 0), new gp_Dir(0, 0, 1)));
    extrema.Initialize(surface, -100, 100, -100, 100, 1e-9, 1e-9);
    return new WeakReference(surface);
  }

  // the graph and its editors live in this frame only
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static BRepGraph_MutGuard_BRepGraphInc_VertexDef MutFirstVertex(
    out WeakReference graphReference, out uint vertexIndex)
  {
    var graph = Shapes.BoxGraph();
    graphReference = new WeakReference(graph);
    var vertex = graph.Topo().Vertices().StartId();
    vertexIndex = vertex.Index;
    return graph.Editor().Vertices().Mut(vertex);
  }

  // the graph lives in this frame only
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static BRepGraph_TopoView TopoOfBoxGraph(out WeakReference graphReference)
  {
    var graph = Shapes.BoxGraph();
    graphReference = new WeakReference(graph);
    return graph.Topo();
  }

  // an iterator and its graph no C# variable holds
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static WeakReference GarbageIterator(out WeakReference iteratorReference)
  {
    var graph = Shapes.BoxGraph();
    iteratorReference = new WeakReference(new BRepGraph_FaceIterator(graph));
    return new WeakReference(graph);
  }
}
