// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Linq;

// NUnit
using NUnit.Framework;

//
using OCC.Core;
using OCC.Core.gp;
using OCC.Core.NCollection;

namespace NetOcc.Tests;

/// <summary>
/// Class template instances no alias names: classes named after their arguments
/// (NCollection_DynamicArray_int), OCCT 8's vectors with checked indexers, and move-only guards
/// returned by value.
/// </summary>
[TestFixture]
public class TemplateInstanceTests
{
  [Test]
  public void DynamicArray_AppendsInsertsAndErases()
  {
    // Arrange
    using var numbers = new NCollection_DynamicArray_int();

    // Act
    numbers.Append(1);
    numbers.Append(3);
    numbers.InsertBefore(1, 2);
    numbers.Append(4);
    numbers.EraseLast();
    numbers[0] = 10;

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(numbers.ToList(), Is.EqualTo(new[] { 10, 2, 3 }));
      Assert.That(numbers.Count, Is.EqualTo(3));
      Assert.That(numbers.Last(), Is.EqualTo(3));
    }
  }

  [Test]
  public void DynamicArray_BadIndex_Throws()
  {
    // Arrange
    using var numbers = new NCollection_DynamicArray_int();
    numbers.Append(1);

    // Act
    Action read = () => _ = numbers[1];

    // Assert
    Assert.That(
      read,
      Throws.TypeOf<OcctException>()
        .With.Property(nameof(OcctException.OcctType))
        .EqualTo("Standard_OutOfRange"));
  }

  [Test]
  public void LinearVector_OfIds_CopiesToAnArray()
  {
    // Arrange (a box's first vertex has three edges)
    var graph = Shapes.BoxGraph();
    var vertex = graph.Topo().Vertices().StartId();

    // Act
    var edges = graph.Topo().Vertices().Edges(vertex);
    var copied = edges.ToArray();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(edges.Count, Is.EqualTo(3));
      Assert.That(copied.Select(e => e.Index), Is.EqualTo(edges.Select(e => e.Index)));
    }
  }

  [Test]
  public void MutGuard_IsReleasedByDispose()
  {
    // Arrange (a second guard on the item throws while the first is registered)
    var graph = Shapes.BoxGraph();
    var vertex = graph.Topo().Vertices().StartId();
    var editor = graph.Editor().Vertices();
    bool dirty;

    // Act
    using (var guard = editor.Mut(vertex))
    {
      editor.SetPoint(guard, new gp_Pnt(1, 2, 3));
      dirty = guard.IsDirty();
    }

    using var again = editor.Mut(vertex);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(dirty, Is.True, "the setter flagged the guard");
      Assert.That(again.Id().Index, Is.EqualTo(vertex.Index), "Dispose ran the guard's destructor");
    }
  }
}
