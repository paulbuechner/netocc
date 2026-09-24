// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BRepGraph;
using OCC.Core.Geom;
using OCC.Core.GeomAdaptor;
using OCC.Core.gp;
using OCC.Core.IntPolyh;
using OCC.Core.math;
using OCC.Core.TCollection;

// static usings
using static OCC.Core.GeomAbs.GeomAbs_CurveType;

namespace NetOcc.Tests;

/// <summary>
/// Overloads C# can't tell apart are one call, which reaches the most capable one: a non-const
/// twin's ref return, a UTF-16 string overload. A C++ call two overloads fit is left out, the
/// others stay. A non-copyable transient comes back through an accessor. OCCT's GetType hides
/// object's.
/// </summary>
[TestFixture]
public class OverloadTests
{
  [Test]
  public void GetType_IsOcctsCurveType()
  {
    // Arrange
    using var adaptor = new GeomAdaptor_Curve(new Geom_Line(new gp_Ax1(gp.Origin(), gp.DZ())));

    // Act
    var occtType = adaptor.GetType();
    var clrType = ((object)adaptor).GetType();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(occtType, Is.EqualTo(GeomAbs_Line));
      Assert.That(clrType, Is.EqualTo(typeof(GeomAdaptor_Curve)));
    }
  }

  [Test]
  public void StringOverloads_ReachTheUtf16One()
  {
    // Arrange (the const char* constructor would copy the UTF-8 bytes as Latin-1)
    using var text = new TCollection_HExtendedString(NonAscii.Text);

    // Act
    var value = text.ToExtString();

    // Assert
    Assert.That(value, Is.EqualTo(NonAscii.Text));
  }

  [Test]
  public void CharacterOverload_IsWrappedBesideTheStringOne()
  {
    // Arrange (SWIG ranked char16_t and const char16_t* alike and dropped one; a char and a string
    // differ in C#)
    var character = NonAscii.Text[0];

    // Act
    using var text = new TCollection_HExtendedString(character);

    // Assert
    Assert.That(text.ToExtString(), Is.EqualTo(character.ToString()));
  }

  [Test]
  public void ConstTwins_ReachTheRefReturn()
  {
    // Arrange
    using var matrix = new math_Matrix(1, 2, 1, 2, 0.0);

    // Act
    matrix.Value(1, 2) = 5.0;

    // Assert
    Assert.That(matrix.Value(1, 2), Is.EqualTo(5.0));
  }

  [Test]
  public void AmbiguousCalls_LeaveTheOthers()
  {
    // Arrange (IntPolyh_Array(int = 256) and (int, int = 256): one argument fits both, in C++ too)

    // Act
    using var empty = new IntPolyh_ArrayOfPoints();
    using var sized = new IntPolyh_ArrayOfPoints(10, 5);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(empty.GetN(), Is.EqualTo(0));
      Assert.That(sized.GetN(), Is.EqualTo(10));
    }
  }

  [Test]
  public void NonCopyableTransient_ComesBackOwned()
  {
    // Arrange
    var graph = Shapes.BoxGraph();
    var edge = graph.Topo().Edges().StartId();
    using var range = BRepGraph_Tool_Edge.Range(graph, edge);

    // Act
    var adaptor = BRepGraph_Tool_Edge.CurveAdaptor(graph, edge);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(adaptor.FirstParameter(), Is.EqualTo(range.First).Within(1e-9));
      Assert.That(adaptor.LastParameter(), Is.EqualTo(range.Second).Within(1e-9));
    }
  }
}
