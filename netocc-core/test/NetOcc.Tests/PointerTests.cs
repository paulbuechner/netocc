// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

// NUnit
using NUnit.Framework;

//
using OCC.Core.Bnd;
using OCC.Core.BRep;
using OCC.Core.BRepBuilderAPI;
using OCC.Core.BRepMesh;
using OCC.Core.BSplCLib;
using OCC.Core.Geom;
using OCC.Core.gp;
using OCC.Core.MathUtils;
using OCC.Core.OSD;
using OCC.Core.Poly;
using OCC.Core.Standard;
using OCC.Core.TColgp;
using OCC.Core.TCollection;
using OCC.Core.TColStd;
using OCC.Core.TopoDS;
using OCC.Core.XmlObjMgt;

// static usings
using static OCC.Core.TopAbs.TopAbs_ShapeEnum;

namespace NetOcc.Tests;

/// <summary>
/// Raw pointers: a class is its proxy (null for nullptr), numbers and structs a pinned C# array,
/// void*, functions and pointers the callee may keep an IntPtr, Standard_ExtString a string, const
/// char*&amp; a ref string, a bool* a bool[].
/// </summary>
[TestFixture]
public class PointerTests
{
  [Test]
  public void BoolPointer_IsWrittenBack()
  {
    // Arrange (an edge of a box has its pcurve on each face stored)
    var box = Shapes.Box();
    var face = TopoDS.Face(Shapes.SubShapes(box, TopAbs_FACE)[0]);
    var edge = TopoDS.Edge(Shapes.SubShapes(face, TopAbs_EDGE)[0]);
    double first = 0, last = 0;
    var isStored = new bool[1];

    // Act
    var curve = BRep_Tool.CurveOnSurface(edge, face, ref first, ref last, isStored);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(curve, Is.Not.Null);
      Assert.That(isStored[0], Is.True);
    }
  }

  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate IntPtr ThreadFunction(IntPtr data);

  private static TColgp_Array1OfPnt BoxCorners() =>
    new([
      new gp_Pnt(0, 0, 0), new gp_Pnt(2, 0, 0), new gp_Pnt(0, 4, 0), new gp_Pnt(0, 0, 6),
      new gp_Pnt(2, 4, 6)
    ]);

  [Test]
  public void ClassPointerParameter_Null_PassesNullptr()
  {
    // Arrange
    var box = new Bnd_OBB();

    // Act
    box.ReBuild(BoxCorners(), null);

    // Assert
    Assert.That(box.IsVoid(), Is.False);
  }

  [Test]
  public void ClassPointerParameter_Collection_PassesTheCollection()
  {
    // Arrange
    var exact = new Bnd_OBB();
    exact.ReBuild(BoxCorners(), null);
    var box = new Bnd_OBB();
    var tolerances = new TColStd_Array1OfReal([0.5, 0.5, 0.5, 0.5, 0.5]);

    // Act
    box.ReBuild(BoxCorners(), tolerances);

    // Assert
    Assert.That(box.XHSize() - exact.XHSize(), Is.EqualTo(0.5).Within(1e-9),
                "enlarged by the tolerances");
  }

  [Test]
  public void StructArrayParameter_IsFilledByTheCallee()
  {
    // Arrange
    var box = new Bnd_OBB();
    box.ReBuild(BoxCorners(), null);
    var vertices = new gp_Pnt[8];

    // Act
    var filled = box.GetVertex(vertices);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(filled, Is.True);
      Assert.That(vertices.Max(v => v.X()) - vertices.Min(v => v.X()),
                  Is.EqualTo(2.0).Within(1e-9));
      Assert.That(vertices.Max(v => v.Y()) - vertices.Min(v => v.Y()),
                  Is.EqualTo(4.0).Within(1e-9));
      Assert.That(vertices.Max(v => v.Z()) - vertices.Min(v => v.Z()),
                  Is.EqualTo(6.0).Within(1e-9));
    }
  }

  [Test]
  public void NumberArrayParameter_IsRead()
  {
    // Arrange
    double[] coefficients = [1.0, 2.0, 3.0];

    // Act
    var value = MathUtils.EvalPoly(coefficients, 2, 2.0);

    // Assert
    Assert.That(value, Is.EqualTo(1.0 + 2.0 * 2.0 + 3.0 * 4.0));
  }

  [Test]
  public void NumberArrayParameter_IsWrittenInPlace()
  {
    // Arrange
    double[] roots = [3.0, 1.0, 2.0];

    // Act
    MathUtils.SortRoots(roots, (ulong)roots.Length);

    // Assert
    Assert.That(roots, Is.EqualTo(new[] { 1.0, 2.0, 3.0 }));
  }

  [Test]
  public void StaticPointerReturn_Nullptr_IsNull()
  {
    // Arrange
    // (no inputs: BSplCLib::NoWeights() returns nullptr, the weights of a non-rational curve)

    // Act
    var weights = BSplCLib.NoWeights();

    // Assert
    Assert.That(weights, Is.Null);
  }

  [Test]
  public void MemberPointerReturn_BorrowsFromItsOwner()
  {
    // Arrange
    using var triangulation = new Poly_CoherentTriangulation();
    var n0 = triangulation.SetNode(new gp_XYZ(0, 0, 0), -1);
    var n1 = triangulation.SetNode(new gp_XYZ(1, 0, 0), -1);
    var n2 = triangulation.SetNode(new gp_XYZ(0, 1, 0), -1);

    // Act
    var triangle = triangulation.AddTriangle(n0, n1, n2);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(new[] { triangle.Node(0), triangle.Node(1), triangle.Node(2) },
                  Is.EqualTo(new[] { n0, n1, n2 }));
      Assert.That(triangle.netoccOwner, Is.SameAs(triangulation),
                  "the triangle lives in the triangulation");
    }
  }

  [Test]
  public void TransientPointerReturn_OwnsAReference()
  {
    // Arrange
    using var plane = new Geom_Plane(new gp_Pnt(0, 0, 0), new gp_Dir(0, 0, 1));
    int whileAlive;

    // Act
    using (var self = plane.This())
    {
      whileAlive = plane.GetRefCount();
    }

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(whileAlive, Is.EqualTo(2));
      Assert.That(plane.GetRefCount(), Is.EqualTo(1));
    }
  }

  [Test]
  public void ClassPointerReference_ReceivesTheNewObject()
  {
    // Arrange
    BRepMesh_DiscretRoot? algorithm = null;

    // Act
    var status = BRepMesh_IncrementalMesh.Discret(Shapes.Box(), 0.5, 0.5, ref algorithm);
    algorithm?.Perform();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(status, Is.Zero);
      Assert.That(algorithm, Is.Not.Null);
      Assert.That(algorithm?.IsDone(), Is.True);
      Assert.That(algorithm?.GetRefCount(), Is.EqualTo(1), "the proxy owns the new object");
    }

    algorithm?.Dispose();
  }

  [Test]
  public void VoidPointer_IsAnAddress()
  {
    // Arrange
    var block = Standard.Allocate(16);

    // Act
    Marshal.WriteInt32(block, 42);
    var read = Marshal.ReadInt32(block);
    Standard.Free(block);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(block, Is.Not.EqualTo(IntPtr.Zero));
      Assert.That(read, Is.EqualTo(42));
    }
  }

  [Test]
  public void FunctionPointer_RunsACSharpCallback()
  {
    // Arrange
    ThreadFunction function = data => new IntPtr(data.ToInt64() + 1);
    using var thread = new OSD_Thread(Marshal.GetFunctionPointerForDelegate(function));
    var result = IntPtr.Zero;

    // Act
    thread.Run(new IntPtr(41));
    var waited = thread.Wait(ref result);
    GC.KeepAlive(function);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(waited, Is.True);
      Assert.That(result, Is.EqualTo(new IntPtr(42)),
                  "the thread's result comes back through a ref IntPtr");
    }
  }

  [Test]
  public void ExtString_IsUtf16()
  {
    // Arrange
    using var text = new TCollection_HExtendedString(3, NonAscii.Text[0]);

    // Act
    var value = text.ToExtString();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(value, Is.EqualTo(new string(NonAscii.Text[0], 3)));
      Assert.That(text.Value(1), Is.EqualTo(NonAscii.Text[0]));
    }
  }

  [Test]
  public void StringReference_MovesAlongTheText()
  {
    // Arrange
    var text = "2.5 7";
    double real = 0;
    var integer = 0;

    // Act
    var gotReal = XmlObjMgt.GetReal(ref text, ref real);
    var afterReal = text;
    var gotInteger = XmlObjMgt.GetInteger(ref text, ref integer);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(gotReal, Is.True);
      Assert.That(gotInteger, Is.True);
      Assert.That(real, Is.EqualTo(2.5));
      Assert.That(afterReal, Is.EqualTo(" 7"));
      Assert.That(integer, Is.EqualTo(7));
      Assert.That(text, Is.Empty);
    }
  }

  [Test]
  public void StreamPointer_IsLentOrNull()
  {
    // Arrange
    var sewing = new BRepBuilderAPI_FastSewing(1e-6);
    using var output = new MemoryStream();

    // Act
    var withoutStream = sewing.GetStatuses(null);
    var withStream = sewing.GetStatuses(output);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(withoutStream, Is.EqualTo(withStream));
      Assert.That(Encoding.ASCII.GetString(output.ToArray()), Does.StartWith("Fast Sewing OK!"));
    }
  }
}
