// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

// NUnit
using NUnit.Framework;

//
using OCC.Core;
using OCC.Core.AIS;
using OCC.Core.BRepAlgoAPI;
using OCC.Core.BRepPrimAPI;
using OCC.Core.gp;
using OCC.Core.Graphic3d;
using OCC.Core.Message;
using OCC.Core.OpenGl;
using OCC.Core.PrsMgr;
using OCC.Core.Select3D;
using OCC.Core.SelectMgr;
using OCC.Core.TopoDS;
using OCC.Core.V3d;

namespace NetOcc.Samples;

/// <summary>
/// C# subclasses of the OCCT classes meant to be derived from: progress, messages, presentations.
/// Presentations need no GPU until they're drawn, so these run.
/// </summary>
[TestFixture]
public class Subclassing
{
  #region progress
  public sealed class Progress : Message_ProgressIndicator
  {
    public double Position { get; private set; }

    public bool Cancelled { get; set; }

    // OCCT reports: GetPosition() is the share done, from 0 to 1
    protected override void Show(Message_ProgressScope theScope, bool isForce) =>
      Position = GetPosition();

    // asked now and then whether to stop
    protected override bool UserBreak() => Cancelled;
  }

  public static TopoDS_Shape FuseWithProgress(TopoDS_Shape a, TopoDS_Shape b, Progress progress)
  {
    // Start() gives the range the boolean reports through, its last parameter
    var fuse = new BRepAlgoAPI_Fuse(a, b, progress.Start());
    return fuse.IsDone() ? fuse.Shape() : throw new OperationCanceledException();
  }
  #endregion

  #region printer
  public sealed class Log : Message_Printer
  {
    public List<string> Lines { get; } = [];

    // what the messenger's printers get, as UTF-8 text
    protected override void send(string theString, Message_Gravity theGravity) =>
      Lines.Add($"{theGravity}: {theString}");
  }
  #endregion

  #region presentation
  // a point: drawn as a marker, selected where it is
  public sealed class Marker(gp_Pnt position) : AIS_InteractiveObject
  {
    protected override void Compute(PrsMgr_PresentationManager thePrsMgr,
                                    Graphic3d_Structure thePrs, int theMode)
    {
      var points = new Graphic3d_ArrayOfPoints(1);
      points.AddVertex(position);
      thePrs.CurrentGroup().AddPrimitiveArray(points);
    }

    public override void ComputeSelection(SelectMgr_Selection theSelection, int theMode) =>
      theSelection.Add(new Select3D_SensitivePoint(new SelectMgr_EntityOwner(this), position));
  }
  #endregion

  [Test]
  public void ProgressAndCancellation()
  {
    // Arrange
    var box = new BRepPrimAPI_MakeBox(10, 20, 30).Shape();
    var cylinder = new BRepPrimAPI_MakeCylinder(3, 40).Shape();
    var progress = new Progress();
    var cancelled = new Progress { Cancelled = true };

    // Act
    var fused = FuseWithProgress(box, cylinder, progress);
    Action cancel = () => FuseWithProgress(box, cylinder, cancelled);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(fused.IsNull(), Is.False);
      Assert.That(progress.Position, Is.GreaterThan(0.0));
      Assert.That(cancel, Throws.TypeOf<OperationCanceledException>());
    }
  }

  [Test]
  public void Messages()
  {
    // Arrange
    var log = new Log();

    // Act
    #region messenger
    // a messenger with the printer; Message.DefaultMessenger().AddPrinter(log) routes all of OCCT's
    var messenger = new Message_Messenger(log);
    messenger.Send("shape healed", Message_Gravity.Message_Warning);
    #endregion

    // Assert
    Assert.That(log.Lines, Is.EqualTo(new[] { "Message_Warning: shape healed" }));
  }

  [Test]
  public void Presentation()
  {
    // Arrange (no GPU: the driver computes presentations and selection without rendering)
    var context = new AIS_InteractiveContext(new V3d_Viewer(new OpenGl_GraphicDriver(null, false)));

    // Act
    #region display
    var marker = new Marker(new gp_Pnt(1, 2, 3));
    context.Display(marker, 0, 0, false); // display mode 0, selection mode 0
    #endregion

    // Assert
    Assert.That(context.IsDisplayed(marker), Is.True);
  }

  [Test]
  public void Exceptions()
  {
    // Arrange
    var messenger = new Message_Messenger(new Failing());

    // Act
    #region exception
    OcctException? caught = null;
    try
    {
      messenger.Send("anything", Message_Gravity.Message_Warning);
    }
    catch (OcctException e)
    {
      caught = e; // e.InnerException: the override's InvalidOperationException
    }
    #endregion

    // Assert
    Assert.That(caught?.InnerException, Is.TypeOf<InvalidOperationException>());
  }

  private sealed class Failing : Message_Printer
  {
    protected override void send(string theString, Message_Gravity theGravity) =>
      throw new InvalidOperationException("no log");
  }
}
