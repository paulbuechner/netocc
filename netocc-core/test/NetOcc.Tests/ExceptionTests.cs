// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

// NUnit
using NUnit.Framework;

//
using OCC.Core;
using OCC.Core.gp;
using OCC.Core.Quantity;
using OCC.Core.Standard;
using OCC.Core.TopoDS;

// static usings
using static OCC.Core.Quantity.Quantity_TypeOfColor;
using static OCC.Core.TopAbs.TopAbs_ShapeEnum;

namespace NetOcc.Tests;

/// <summary>
/// C++ exceptions are caught at the native boundary and rethrown as OcctException; OCCT's exception
/// classes are classes, whose hierarchy OcctException.Is checks against.
/// </summary>
[TestFixture]
public class ExceptionTests
{
  [Test]
  public void NullDirection_ThrowsConstructionError()
  {
    // Arrange
    const double zero = 0.0;

    // Act
    Action construct = () => _ = new gp_Dir(zero, zero, zero);

    // Assert
    Assert.That(construct,
                Throws.TypeOf<OcctException>()
                  .With.Property(nameof(OcctException.OcctType))
                  .EqualTo("Standard_ConstructionError"));
  }

  [Test]
  public void NormalizingNullVector_ThrowsConstructionError()
  {
    // Arrange (OCCT 8 raises Standard_ConstructionError here; older releases:
    // gp_VectorWithNullMagnitude)
    var nullVector = new gp_Vec(0, 0, 0);

    // Act
    Action normalize = () => _ = nullVector.Normalized();

    // Assert
    Assert.That(normalize,
                Throws.TypeOf<OcctException>()
                  .With.Property(nameof(OcctException.OcctType))
                  .EqualTo("Standard_ConstructionError")
                  .And.Property(nameof(OcctException.OcctMessage))
                  .Contains("zero norm"));
  }

  [Test]
  public void WrongTopoDSCast_ThrowsTypeMismatch()
  {
    // Arrange
    var edge = Shapes.SubShapes(Shapes.Box(), TopAbs_EDGE)[0];

    // Act
    Action cast = () => _ = TopoDS.Face(edge);

    // Assert
    Assert.That(
      cast,
      Throws.TypeOf<OcctException>()
        .With.Property(nameof(OcctException.OcctType))
        .EqualTo("Standard_TypeMismatch"));
  }

  [Test]
  public void NativeCalls_WorkAfterAnException()
  {
    // Arrange
    try
    {
      _ = new gp_Dir(0, 0, 0);
    }
    catch (OcctException)
    {
      // the failed call is the precondition
    }

    // Act
    var direction = new gp_Dir(0, 0, 5);

    // Assert
    Assert.That(direction.Z(), Is.EqualTo(1.0).Within(1e-12));
  }

  [Test]
  public void ColorComponentOutOfRange_ThrowsOutOfRange()
  {
    // Arrange
    const double tooBright = 1.5;

    // Act
    Action construct = () => _ = new Quantity_Color(tooBright, 0, 0, Quantity_TOC_RGB);

    // Assert
    Assert.That(construct,
                Throws.TypeOf<OcctException>()
                  .With.Property(nameof(OcctException.OcctType))
                  .EqualTo("Standard_OutOfRange"));
  }

  [Test]
  public void Is_MatchesTheRaisedClassAndItsBases()
  {
    // Arrange (gp_Dir raises Standard_ConstructionError, a Standard_DomainError)
    var raised = Raised(() => _ = new gp_Dir(0, 0, 0));

    // Act
    var exact = raised.Is<Standard_ConstructionError>();
    var baseClass = raised.Is<Standard_DomainError>();
    var root = raised.Is<Standard_Failure>();
    var sibling = raised.Is<Standard_RangeError>();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(exact, Is.True);
      Assert.That(baseClass, Is.True);
      Assert.That(root, Is.True);
      Assert.That(sibling, Is.False);
    }
  }

  [Test]
  public void ExceptionClass_IsAnOrdinaryClass()
  {
    // Arrange
    using var failure = new Standard_OutOfRange("bad index");

    // Act
    var type = failure.ExceptionType();
    var message = failure.what();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(type, Is.EqualTo("Standard_OutOfRange"));
      Assert.That(message, Is.EqualTo("bad index"));
    }
  }

  // the OcctException a call raises
  private static OcctException Raised(Action call)
  {
    try
    {
      call();
    }
    catch (OcctException e)
    {
      return e;
    }

    throw new AssertionException("the call raised no OcctException");
  }
}
