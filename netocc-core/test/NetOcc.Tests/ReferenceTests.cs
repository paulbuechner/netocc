// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BOPTools;
using OCC.Core.BRepBuilderAPI;
using OCC.Core.Draft;
using OCC.Core.gp;
using OCC.Core.ShapeFix;
using OCC.Core.TopTools;

namespace NetOcc.Tests;

/// <summary>
/// References members return: C# refs into the object (numbers, structs), proxies that borrow it
/// (classes).
/// </summary>
[TestFixture]
public class ReferenceTests
{
  [Test]
  public void NumberReference_WritesIntoTheObject()
  {
    // Arrange
    using var fix = new ShapeFix_Wire();
    var before = fix.FixReorderMode();

    // Act
    fix.FixReorderMode() = 1;

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(before, Is.EqualTo(-1), "-1: OCCT decides");
      Assert.That(fix.FixReorderMode(), Is.EqualTo(1));
    }
  }

  [Test]
  public void StructReference_WritesIntoTheObject()
  {
    // Arrange
    using var vertex = new Draft_VertexInfo();

    // Act
    vertex.ChangeGeometry() = new gp_Pnt(1, 2, 3);

    // Assert
    Assert.That(vertex.Geometry(), Is.EqualTo(new gp_Pnt(1, 2, 3)));
  }

  [Test]
  public void ClassReference_BorrowsTheObject()
  {
    // Arrange
    using var block = new BOPTools_ConnexityBlock();
    var vertex = new BRepBuilderAPI_MakeVertex(new gp_Pnt(0, 0, 0)).Vertex();

    // Act
    var shapes = block.ChangeShapes();
    shapes.Append(vertex);
    shapes.Dispose();

    // Assert
    Assert.That(block.Shapes().Count, Is.EqualTo(1),
                "the list is the block's; disposing the borrowed proxy leaves it");
  }

  [Test]
  public void BorrowedProxy_KeepsItsOwnerAlive()
  {
    // Arrange
    var shapes = BorrowShapes(out var owner);
    var vertex = new BRepBuilderAPI_MakeVertex(new gp_Pnt(0, 0, 0)).Vertex();

    // Act
    Garbage.Collect();
    shapes.Append(vertex);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(owner.IsAlive, Is.True, "the block's proxy lives as long as the list's");
      Assert.That(shapes.Count, Is.EqualTo(1));
    }

    GC.KeepAlive(shapes);
  }

  // the block is unreachable once this returns, except through the borrowed list
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static TopTools_ListOfShape BorrowShapes(out WeakReference owner)
  {
    var block = new BOPTools_ConnexityBlock();
    owner = new WeakReference(block);
    return block.ChangeShapes();
  }
}
