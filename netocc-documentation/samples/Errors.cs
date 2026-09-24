// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

// NUnit
using NUnit.Framework;

//
using OCC.Core;
using OCC.Core.BRepAlgoAPI;
using OCC.Core.BRepBuilderAPI;
using OCC.Core.BRepFilletAPI;
using OCC.Core.BRepPrimAPI;
using OCC.Core.gp;
using OCC.Core.Standard;
using OCC.Core.StdFail;
using OCC.Core.TopAbs;
using OCC.Core.TopExp;
using OCC.Core.TopoDS;

namespace NetOcc.Samples;

/// <summary>Errors: OCCT's exceptions, and algorithms that report failure.</summary>
[TestFixture]
public class Errors
{
  [Test]
  public void Catch()
  {
    // Arrange
    string? caught = null;

    // Act
    #region catch
    try
    {
      var flat = new BRepPrimAPI_MakeBox(0, 10, 10).Shape();
    }
    catch (OcctException e) when (e.Is<Standard_DomainError>())
    {
      // OcctType names the class OCCT threw, OcctMessage has its message
      caught = $"{e.OcctType}: {e.OcctMessage}";
    }
    #endregion

    // Assert
    Assert.That(caught, Does.StartWith("Standard_DomainError"));
  }

  [Test]
  public void Hierarchy()
  {
    // Arrange
    string? type = null;
    bool construction = false, domain = false, range = false;

    // Act
    #region hierarchy
    try
    {
      var zero = new gp_Dir(0, 0, 0);
    }
    catch (OcctException e)
    {
      type = e.OcctType;                                 // "Standard_ConstructionError"
      construction = e.Is<Standard_ConstructionError>(); // true
      domain = e.Is<Standard_DomainError>();             // true: its base class
      range = e.Is<Standard_RangeError>();               // false
    }
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(type, Is.EqualTo("Standard_ConstructionError"));
      Assert.That(construction && domain, Is.True);
      Assert.That(range, Is.False);
    }
  }

  [Test]
  public void IsDone()
  {
    // Act
    #region is-done
    // builders check their input and say why they failed, without throwing
    var point = new gp_Pnt(1, 1, 1);
    var edge = new BRepBuilderAPI_MakeEdge(point, point);
    var done = edge.IsDone(); // false
    var why = edge.Error();   // BRepBuilderAPI_LineThroughIdenticPoints

    // asking a failed builder for its result throws StdFail_NotDone
    Action result = () => edge.Edge();
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(done, Is.False);
      Assert.That(
        why, Is.EqualTo(BRepBuilderAPI_EdgeError.BRepBuilderAPI_LineThroughIdenticPoints));
      Assert.That(
        result,
        Throws.TypeOf<OcctException>().With.Matches<OcctException>(x => x.Is<StdFail_NotDone>()));
    }
  }

  [Test]
  public void FailedFillet()
  {
    // Arrange
    var box = new BRepPrimAPI_MakeBox(10, 10, 10).Shape();
    var edge = TopoDS.Edge(new TopExp_Explorer(box, TopAbs_ShapeEnum.TopAbs_EDGE).Current());

    // Act
    #region failed-fillet
    // a radius the box can't take
    var fillet = new BRepFilletAPI_MakeFillet(box);
    fillet.Add(20, edge);
    fillet.Build();

    TopoDS_Shape result = fillet.IsDone() ? fillet.Shape() : box; // keep the box unrounded
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(fillet.IsDone(), Is.False);
      Assert.That(result.IsSame(box), Is.True);
    }
  }

  [Test]
  public void BooleanErrors()
  {
    // Arrange
    var box = new BRepPrimAPI_MakeBox(10, 10, 10).Shape();

    // Act
    #region boolean-errors
    // booleans report through HasErrors and HasWarnings
    var cut = new BRepAlgoAPI_Cut(box, new TopoDS_Shape()); // a null tool
    var failed = cut.HasErrors();                           // true
    #endregion

    // Assert
    Assert.That(failed, Is.True);
  }
}
