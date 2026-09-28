// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

// NUnit
using NUnit.Framework;

//
using OCC.Core;
using OCC.Core.AIS;
using OCC.Core.Aspect;
using OCC.Core.BRepAlgoAPI;
using OCC.Core.gp;
using OCC.Core.Graphic3d;
using OCC.Core.Message;
using OCC.Core.NCollection;
using OCC.Core.OpenGl;
using OCC.Core.PrsMgr;
using OCC.Core.Select3D;
using OCC.Core.SelectMgr;
using OCC.Core.V3d;

namespace NetOcc.Tests;

/// <summary>
/// C# subclasses of OCCT classes (directors): OCCT calls their overrides, keeps them alive while it
/// holds them and hands them back as themselves; an exception in an override unwinds OCCT.
/// </summary>
[TestFixture]
public class DirectorTests
{
  [Test]
  public void ProgressIndicator_ReportsABoolean()
  {
    // Arrange
    var progress = new Progress(int.MaxValue);

    // Act
    var fuse = new BRepAlgoAPI_Fuse(Shapes.Box(), Shapes.Cylinder(), progress.Start());

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(fuse.IsDone(), Is.True);
      Assert.That(progress.Shown, Is.GreaterThan(0));
    }
  }

  [Test]
  public void ProgressIndicator_CancelsABoolean()
  {
    // Arrange (asked after its first report, the indicator stops the boolean)
    var progress = new Progress(1);

    // Act
    var fuse = new BRepAlgoAPI_Fuse(Shapes.Box(), Shapes.Cylinder(), progress.Start());

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(fuse.IsDone(), Is.False);
      Assert.That(fuse.HasErrors(), Is.True);
    }
  }

  [Test]
  public void Printer_ReceivesTheMessengersMessages()
  {
    // Arrange
    var printer = new Printer();
    var messenger = new Message_Messenger(printer);

    // Act
    messenger.Send(NonAscii.Text, Message_Gravity.Message_Warning);

    // Assert
    Assert.That(printer.Lines, Is.EqualTo(new[] { NonAscii.Text }));
  }

  [Test]
  public void InteractiveObject_ComputesItsPresentationAndSelection()
  {
    // Arrange (an OpenGL driver left uninitialized: presentations and selection are computed,
    // nothing renders)
    var context = new AIS_InteractiveContext(new V3d_Viewer(new OpenGl_GraphicDriver(null, false)));
    var marker = new Marker();

    // Act (display mode 0, selection mode 0)
    context.Display(marker, 0, 0, false);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(context.IsDisplayed(marker), Is.True);
      Assert.That(marker.Computed, Is.EqualTo(1));
      Assert.That(marker.Selected, Is.EqualTo(1));
    }
  }

  [Test]
  public void ViewController_OwnedByItsProxyIsCalledBack()
  {
    // Arrange (UpdateMouseScroll scales the scroll and hands it to UpdateZoom)
    var controller = new Controller();
    using var point = new BVH_Vec2i(10, 20);
    using var scroll = new Aspect_ScrollDelta(point, 2.0);

    // Act
    var changed = controller.UpdateMouseScroll(scroll);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(changed, Is.True);
      Assert.That(controller.Zooms, Is.EqualTo(1));
    }
  }

  [Test]
  public void Occt_HandsADirectorObjectBackAsItself()
  {
    // Arrange (an owner of the marker, and a sensitive point of that owner)
    var marker = new Marker();
    var owner = new Owner(marker);
    var point = new Select3D_SensitivePoint(owner, new gp_Pnt(1, 2, 3));

    // Act
    var selectable = owner.Selectable();
    var pointOwner = point.OwnerId();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(selectable, Is.SameAs(marker));
      Assert.That(pointOwner, Is.SameAs(owner));
    }
  }

  [Test]
  public void DirectorObject_LivesWhileOcctHoldsIt()
  {
    // Arrange (once Attach returns, only the messenger holds the printer)
    var messenger = new Message_Messenger(new Printer());
    var printer = Attach(messenger);
    Collect();

    // Act
    messenger.Send(NonAscii.Text, Message_Gravity.Message_Warning);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(printer.IsAlive, Is.True);
      Assert.That(((Printer)printer.Target!).Lines, Is.EqualTo(new[] { NonAscii.Text }));
    }
  }

  [Test]
  public void DirectorObject_IsCollectedOnceOcctLetsGo()
  {
    // Arrange
    var messenger = new Message_Messenger(new Printer());
    var printer = Attach(messenger);
    Collect();

    // Act
    Detach(messenger, printer);
    Collect();

    // Assert
    Assert.That(printer.IsAlive, Is.False);
  }

  [Test]
  public void DirectorObject_DisposedWhileOcctHoldsItKeepsWorking()
  {
    // Arrange
    var printer = new Printer();
    var messenger = new Message_Messenger(printer);

    // Act (released only once the messenger lets go)
    printer.Dispose();
    messenger.Send(NonAscii.Text, Message_Gravity.Message_Warning);

    // Assert
    Assert.That(printer.Lines, Is.EqualTo(new[] { NonAscii.Text }));
  }

  [Test]
  public void Override_ThrowingUnwindsOcct()
  {
    // Arrange
    var messenger = new Message_Messenger(new ThrowingPrinter());

    // Act
    Action send = () => messenger.Send(NonAscii.Text, Message_Gravity.Message_Warning);

    // Assert (OCCT unwound as from a C++ exception, and C# gets the override's inside)
    Assert.That(
      send,
      Throws.TypeOf<OcctException>()
        .With.InnerException.TypeOf<InvalidOperationException>()
        .And.InnerException.Message.EqualTo(NonAscii.Text));
  }

  [Test]
  public void Override_ThrowingWithAResultUnwindsOcct()
  {
    // Arrange (displayed in the context's default mode, the object is asked whether it takes it)
    var context = new AIS_InteractiveContext(new V3d_Viewer(new OpenGl_GraphicDriver(null, false)));

    // Act
    Action display = () => context.Display(new Rejecting(), false);

    // Assert
    Assert.That(
      display, Throws.TypeOf<OcctException>().With.InnerException.TypeOf<NotSupportedException>());
  }

  // adds a printer that only the messenger holds afterwards
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static WeakReference Attach(Message_Messenger messenger)
  {
    var printer = new Printer();
    messenger.AddPrinter(printer);
    return new WeakReference(printer);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void Detach(Message_Messenger messenger, WeakReference printer) =>
    messenger.RemovePrinter((Printer)printer.Target!);

  // enough full collections for an object nothing holds to be weakened, collected and finalized
  private static void Collect()
  {
    for (var i = 0; i < 4; i++)
    {
      Garbage.Collect();
    }
  }

  // counts its reports; asked whether to stop, it says yes once it has reported stopAfter times
  private sealed class Progress(int stopAfter) : Message_ProgressIndicator
  {
    public int Shown { get; private set; }

    protected override void Show(Message_ProgressScope theScope, bool isForce) => Shown++;

    protected override bool UserBreak() => Shown >= stopAfter;
  }

  private sealed class Printer : Message_Printer
  {
    public List<string> Lines { get; } = [];

    protected override void send(string theString, Message_Gravity theGravity) =>
      Lines.Add(theString);
  }

  private sealed class ThrowingPrinter : Message_Printer
  {
    protected override void send(string theString, Message_Gravity theGravity) =>
      throw new InvalidOperationException(theString);
  }

  // a point, presented and selectable
  private sealed class Marker : AIS_InteractiveObject
  {
    private static readonly gp_Pnt Position = new(1, 2, 3);

    public int Computed { get; private set; }

    public int Selected { get; private set; }

    public override void ComputeSelection(SelectMgr_Selection theSelection, int theMode)
    {
      Selected++;
      theSelection.Add(new Select3D_SensitivePoint(new Owner(this), Position));
    }

    protected override void Compute(PrsMgr_PresentationManager thePrsMgr,
                                    Graphic3d_Structure thePrs, int theMode)
    {
      Computed++;
      var points = new Graphic3d_ArrayOfPoints(1);
      points.AddVertex(Position);
      thePrs.CurrentGroup().AddPrimitiveArray(points);
    }
  }

  private sealed class Rejecting : AIS_InteractiveObject
  {
    public override bool AcceptDisplayMode(int theMode) =>
      throw new NotSupportedException($"display mode {theMode}");

    public override void ComputeSelection(SelectMgr_Selection theSelection, int theMode)
    {
    }

    protected override void Compute(PrsMgr_PresentationManager thePrsMgr,
                                    Graphic3d_Structure thePrs, int theMode)
    {
    }
  }

  // not handle-managed: its proxy owns it
  private sealed class Controller : AIS_ViewController
  {
    public int Zooms { get; private set; }

    public override bool UpdateZoom(Aspect_ScrollDelta theDelta)
    {
      Zooms++;
      return true;
    }
  }

  private sealed class Owner(SelectMgr_SelectableObject selectable)
    : SelectMgr_EntityOwner(selectable);
}
