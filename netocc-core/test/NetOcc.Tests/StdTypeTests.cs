// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Text;

// NUnit
using NUnit.Framework;

//
using OCC.Core;
using OCC.Core.Bnd;
using OCC.Core.BRepGraph;
using OCC.Core.gp;
using OCC.Core.IGESControl;
using OCC.Core.MathPoly;
using OCC.Core.RWHeaderSection;
using OCC.Core.Standard;
using OCC.Core.Transfer;

namespace NetOcc.Tests;

/// <summary>
/// Standard library types in OCCT's signatures: std::array as a C# array, std::optional as a
/// nullable, std::bitset as a ulong mask, std::string_view and a std::stringstream's text as
/// strings, std::pair as a class with First and Second, std::complex as Complex.
/// </summary>
[TestFixture]
public class StdTypeTests
{
  [Test]
  public void Array_RoundTrips()
  {
    // Arrange
    using var box = new Bnd_B3d([1.0, 2.0, 3.0], [0.5, 0.5, 0.5]);

    // Act
    var center = box.Center();

    // Assert
    Assert.That(center, Is.EqualTo(new[] { 1.0, 2.0, 3.0 }));
  }

  [Test]
  public void Array_OfAnotherLength_Throws()
  {
    // Arrange
    double[] center = [1.0, 2.0];

    // Act
    Action construct = () => _ = new Bnd_B3d(center, [0.5, 0.5, 0.5]);

    // Assert
    Assert.That(construct, Throws.TypeOf<ArgumentException>());
  }

  [Test]
  public void Optional_IsNullWithoutAValue()
  {
    // Arrange
    using var empty = new Bnd_Range();
    using var range = new Bnd_Range(1.0, 3.0);

    // Act
    var none = empty.Min();
    var min = range.Min();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(none, Is.Null);
      Assert.That(min, Is.EqualTo(1.0));
    }
  }

  [Test]
  public void Optional_OfAStruct_IsTheStruct()
  {
    // Arrange
    var box = new Bnd_Box();
    box.Update(0, 0, 0, 2, 4, 6);

    // Act
    var center = box.Center();

    // Assert
    Assert.That(center, Is.EqualTo(new gp_Pnt(1, 2, 3)));
  }

  [Test]
  public void Bitset_IsAMask()
  {
    // Arrange
    using var writer = new IGESControl_Writer();

    // Act
    writer.SetShapeProcessFlags(0b101UL);
    var flags = writer.GetShapeProcessFlags();

    // Assert
    Assert.That(flags, Is.EqualTo(0b101UL));
  }

  [Test]
  public void StringStream_IsItsText()
  {
    // Arrange (UTF-8 both ways)
    const string text = NonAscii.Text;

    // Act
    var copied = Standard_Dump.Text(text);

    // Assert
    Assert.That(copied, Is.EqualTo(text));
  }

  [Test]
  public void StringStream_ReadsWhatDumpJsonWrites()
  {
    // Arrange
    using var stream = new MemoryStream();
    new gp_Pnt(1, 2, 3).DumpJson(stream);
    var json = Encoding.UTF8.GetString(stream.ToArray());
    var point = new gp_Pnt();
    var position = 1;

    // Act
    var read = point.InitFromJson(json, ref position);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(read, Is.True);
      Assert.That(point, Is.EqualTo(new gp_Pnt(1, 2, 3)));
    }
  }

  [Test]
  public void StringView_IsAString()
  {
    // Arrange
    var module = new RWHeaderSection_ReadWriteModule();

    // Act
    var type = module.StepType(1);

    // Assert
    Assert.That(type, Is.EqualTo("FILE_NAME"));
  }

  [Test]
  public void Pair_OfNumbers_HasFirstAndSecond()
  {
    // Arrange (a box edge's parameter range: from 0 to its length)
    var graph = Shapes.BoxGraph();
    var edge = graph.Topo().Edges().StartId();

    // Act
    using var range = BRepGraph_Tool_Edge.Range(graph, edge);

    // Assert
    Assert.That(range.Second, Is.GreaterThan(range.First));
  }

  [Test]
  public void Pair_OfFlags_HasTheMaskAndWhetherItsSet()
  {
    // Arrange (a reader keeps its flags in its actor, which it has only once it reads)
    var actor = new Transfer_ActorOfTransientProcess();
    actor.SetProcessingFlags(0b11UL);

    // Act
    using var flags = actor.GetProcessingFlags();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(flags.First, Is.EqualTo(0b11UL));
      Assert.That(flags.Second, Is.True);
    }
  }

  [Test]
  public void Complex_RefsAreWrittenBack()
  {
    // Arrange (x^2 + 1 at i: its root; the derivatives there are 2i and 2)
    double[] coefficients = [1, 0, 1];
    Complex value = default, first = default, second = default;

    // Act
    MathPoly_detail.EvaluatePolynomialWithDerivatives(coefficients, 2, new Complex(0, 1), ref value,
                                                      ref first, ref second);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(value.Real, Is.EqualTo(0).Within(1e-12));
      Assert.That(value.Imaginary, Is.EqualTo(0).Within(1e-12));
      Assert.That(first.Imaginary, Is.EqualTo(2).Within(1e-12));
      Assert.That(second.Real, Is.EqualTo(2).Within(1e-12));
    }
  }

  [Test]
  public void Complex_Return_IsTheRoot()
  {
    // Arrange (x^2 + 1: Laguerre's iteration from near i ends at i)
    double[] coefficients = [1, 0, 1];

    // Act
    var root = MathPoly_detail.LaguerreIteration(coefficients, 2, new Complex(0.1, 0.9), 1e-12, 50);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(root.Real, Is.EqualTo(0).Within(1e-9));
      Assert.That(root.Imaginary, Is.EqualTo(1).Within(1e-9));
    }
  }
}
