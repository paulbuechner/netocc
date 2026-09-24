// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Linq;

// NUnit
using NUnit.Framework;

//
using OCC.Core.Bnd;
using OCC.Core.BRep;
using OCC.Core.BRepPrimAPI;
using OCC.Core.Geom;
using OCC.Core.gp;
using OCC.Core.math;
using OCC.Core.TColgp;
using OCC.Core.TColStd;
using OCC.Core.TDataStd;
using OCC.Core.TopAbs;
using OCC.Core.TopExp;
using OCC.Core.TopoDS;
using OCC.Core.TopTools;

namespace NetOcc.Samples;

/// <summary>From C++ to C#: how OCCT's signatures look in C#.</summary>
[TestFixture]
public class Mapping
{
  [Test]
  public void RefParameters()
  {
    // Arrange
    var box = new BRepPrimAPI_MakeBox(10, 20, 30).Shape();
    var edge = TopoDS.Edge(new TopExp_Explorer(box, TopAbs_ShapeEnum.TopAbs_EDGE).Current());

    // Act
    #region ref-parameters
    // Standard_Real& First, Standard_Real& Last: ref, initialized first
    double first = 0, last = 0;
    var curve = BRep_Tool.Curve(edge, ref first, ref last);

    // Bnd_Box& (a struct): ref as well
    var bounds = new Bnd_Box();
    OCC.Core.BRepBndLib.BRepBndLib.Add(box, ref bounds);
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(curve, Is.Not.Null);
      Assert.That(last - first, Is.GreaterThan(0));
      Assert.That(bounds.IsVoid(), Is.False);
    }
  }

  [Test]
  public void RefReturns()
  {
    // Act
    #region ref-returns
    // Standard_Real& Value(int, int): a ref return, read and written in place
    var matrix = new math_Matrix(1, 2, 1, 2, 0.0);
    matrix.Value(1, 2) = 5.0;
    ref var cell = ref matrix.Value(2, 1);
    cell = 7.0;
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(matrix.Value(1, 2), Is.EqualTo(5.0));
      Assert.That(matrix.Value(2, 1), Is.EqualTo(7.0));
    }
  }

  [Test]
  public void Collections()
  {
    // Act
    #region collections
    // arrays from C# arrays, numbered from 1; indexers use OCCT's bounds
    var points =
      new TColgp_Array1OfPnt([new gp_Pnt(0, 0, 0), new gp_Pnt(10, 0, 0), new gp_Pnt(10, 10, 0)]);
    var second = points[2];
    gp_Pnt[] copy = points.ToArray();

    // or with bounds of your choice
    var weights = new TColStd_Array1OfReal(0, 2);
    weights[0] = 1.0;

    // sequences grow, and count from 1
    var sequence = new TColStd_SequenceOfReal();
    sequence.Append(1.5);
    sequence.Append(2.5);
    var sum = sequence.Sum(); // IEnumerable<double>: LINQ works

    // maps: an indexed map numbers its keys from 1
    var faces = new TopTools_IndexedMapOfShape();
    TopExp.MapShapes(new BRepPrimAPI_MakeBox(1, 1, 1).Shape(), TopAbs_ShapeEnum.TopAbs_FACE, faces);
    var index = faces.FindIndex(faces[3]); // 3
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(points.Lower(), Is.EqualTo(1));
      Assert.That(second.X(), Is.EqualTo(10));
      Assert.That(copy, Has.Length.EqualTo(3));
      Assert.That(weights.Lower(), Is.Zero);
      Assert.That(sum, Is.EqualTo(4.0));
      Assert.That(index, Is.EqualTo(3));
    }
  }

  [Test]
  public void OutOfRange()
  {
    // Arrange
    var points = new TColgp_Array1OfPnt(1, 3);

    // Act
    #region out-of-range
    Action beyond = () => _ = points[4]; // Standard_OutOfRange, as an OcctException
    #endregion

    // Assert
    Assert.That(
      beyond,
      Throws.TypeOf<OCC.Core.OcctException>()
        .With.Property("OcctType")
        .EqualTo("Standard_OutOfRange"));
  }

  [Test]
  public void StandardTypes()
  {
    // Act
    #region standard-types
    // Standard_GUID: System.Guid
    Guid id = TDataStd_Real.GetID();

    // std::optional<gp_Pnt>: gp_Pnt?, null for an empty box
    gp_Pnt? centre = new Bnd_Box().Center();

    // TCollection_AsciiString and const char*: string
    Geom_Curve curve = new Geom_Circle(new gp_Ax2(), 1);
    string name = curve.DynamicType().Name(); // "Geom_Circle"
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(id, Is.Not.EqualTo(Guid.Empty));
      Assert.That(centre, Is.Null);
      Assert.That(name, Is.EqualTo("Geom_Circle"));
    }
  }

  [Test]
  public void Operators()
  {
    // Act
    #region operators
    var sum = new gp_Vec(1, 0, 0) + new gp_Vec(0, 1, 0);   // operator+
    var scaled = sum * 2;                                  // operator*
    var dot = sum * new gp_Vec(1, 0, 0);                   // gp_Vec * gp_Vec: the dot product
    var cross = new gp_Vec(1, 0, 0) ^ new gp_Vec(0, 1, 0); // operator^: the cross product
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(scaled.X(), Is.EqualTo(2));
      Assert.That(dot, Is.EqualTo(1));
      Assert.That(cross.Z(), Is.EqualTo(1));
    }
  }
}
