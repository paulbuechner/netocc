// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BRep;
using OCC.Core.BRepMesh;
using OCC.Core.BRepPrimAPI;
using OCC.Core.BRepTools;
using OCC.Core.gp;
using OCC.Core.TopAbs;
using OCC.Core.TopExp;
using OCC.Core.TopLoc;
using OCC.Core.TopoDS;

namespace NetOcc.Samples;

/// <summary>Meshing: triangulating shapes and reading the triangles.</summary>
[TestFixture]
public class Meshing
{
  private static int Triangles(TopoDS_Shape shape)
  {
    var count = 0;
    foreach (var face in new TopExp_Explorer(shape, TopAbs_ShapeEnum.TopAbs_FACE))
    {
      count += BRep_Tool.Triangulation(TopoDS.Face(face), new TopLoc_Location())?.NbTriangles()
               ?? 0;
    }

    return count;
  }

  [Test]
  public void Triangulate()
  {
    // Arrange
    var sphere = new BRepPrimAPI_MakeSphere(new gp_Pnt(0, 0, 0), 10).Shape();

    // Act
    #region triangulate
    // 0.1: how far a triangle may stray from the surface; 0.5 rad: how far its normals may turn
    var mesh = new BRepMesh_IncrementalMesh(sphere, 0.1, false, 0.5, true); // true: in parallel
    var done = mesh.IsDone();
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(done, Is.True);
      Assert.That(Triangles(sphere), Is.GreaterThan(100));
    }
  }

  [Test]
  public void Remesh()
  {
    // Arrange
    var sphere = new BRepPrimAPI_MakeSphere(new gp_Pnt(0, 0, 0), 10).Shape();

    // Act
    #region remesh
    new BRepMesh_IncrementalMesh(sphere, 1.0);
    var coarse = Triangles(sphere);

    // the triangulation stays on the shape: meshing again with a finer deflection refines it,
    // a coarser one keeps what's there. BRepTools.Clean drops it.
    new BRepMesh_IncrementalMesh(sphere, 0.01);
    var fine = Triangles(sphere);

    BRepTools.Clean(sphere);
    var cleaned = Triangles(sphere); // 0
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(fine, Is.GreaterThan(coarse));
      Assert.That(cleaned, Is.Zero);
    }
  }

  [Test]
  public void ReadTriangles()
  {
    // Arrange
    var box = new BRepPrimAPI_MakeBox(10, 20, 30).Shape();
    var shift = new gp_Trsf();
    shift.SetTranslation(new gp_Vec(100, 0, 0));
    var shape = box.Moved(new TopLoc_Location(shift));
    new BRepMesh_IncrementalMesh(shape, 0.1);

    // Act
    #region read-triangles
    // one vertex and index list for the whole shape, as a renderer wants it
    var vertices = new List<gp_Pnt>();
    var indices = new List<int>();
    foreach (var item in new TopExp_Explorer(shape, TopAbs_ShapeEnum.TopAbs_FACE))
    {
      var face = TopoDS.Face(item);
      var location = new TopLoc_Location();
      var triangulation =
        BRep_Tool.Triangulation(face, location); // writes the face's location into it
      if (triangulation is null)
      {
        continue;
      }

      // the nodes are in the face's own coordinates: move them where the face is
      var trsf = location.Transformation();
      var offset = vertices.Count;
      foreach (var node in triangulation.NodesToArray()) // one native call for all nodes
      {
        vertices.Add(node.Transformed(trsf));
      }

      // a reversed face's triangles turn the other way
      var reversed = face.Orientation() == TopAbs_Orientation.TopAbs_REVERSED;
      foreach (var triangle in triangulation.TrianglesToArray())
      {
        int n1 = 0, n2 = 0, n3 = 0;
        triangle.Get(ref n1, ref n2, ref n3); // node numbers from 1
        indices.Add(offset + n1 - 1);
        indices.Add(offset + (reversed ? n3 : n2) - 1);
        indices.Add(offset + (reversed ? n2 : n3) - 1);
      }
    }
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(vertices, Has.Count.EqualTo(24));
      Assert.That(indices, Has.Count.EqualTo(12 * 3));
      Assert.That(vertices.TrueForAll(v => v.X() >= 100 - 1e-9 && v.X() <= 110 + 1e-9), Is.True);
    }
  }
}
