// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;

// NUnit
using NUnit.Framework;

//
using OCC.Core.AIS;
using OCC.Core.Aspect;
using OCC.Core.Font;
using OCC.Core.gp;
using OCC.Core.Graphic3d;
using OCC.Core.OpenGl;
using OCC.Core.V3d;

namespace NetOcc.Tests;

/// <summary>
/// Visualization without rendering (CI has no GPU): presentations, the interactive context on an
/// OpenGL driver left uninitialized, the camera, code points, and a native window through
/// Aspect_Window.FromNativeHandle.
/// </summary>
[TestFixture]
public class VisualizationTests
{
  private const uint WsPopup = 0x80000000;

  [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName,
                                              uint style, int x, int y, int width, int height,
                                              IntPtr parent, IntPtr menu, IntPtr instance,
                                              IntPtr param);

  [DllImport("user32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool DestroyWindow(IntPtr window);

  [Test]
  public void AisShape_HoldsItsShape()
  {
    // Arrange
    var box = Shapes.Box();

    // Act
    var presentation = new AIS_Shape(box);

    // Assert
    Assert.That(presentation.Shape().IsSame(box), Is.True);
  }

  [Test]
  public void Context_DisplaysAShapeWithoutAView()
  {
    // Arrange (no display connection and no GL context: presentations are computed, nothing
    // renders)
    var driver = new OpenGl_GraphicDriver(null, false);
    var context = new AIS_InteractiveContext(new V3d_Viewer(driver));
    var shape = new AIS_Shape(Shapes.Box());

    // Act
    context.Display(shape, false);

    // Assert
    Assert.That(context.IsDisplayed(shape), Is.True);
  }

  [Test]
  public void OpenGlRenderer_IsThereBeforeAnyContext()
  {
    // Arrange (the classes a view renders into an application's own OpenGL surface with)
    var driver = new OpenGl_GraphicDriver(null, false);

    // Act
    var context = new OpenGl_Context();
    var frameBuffer = new OpenGl_FrameBuffer();
    var texture = new OpenGl_Texture();

    // Assert (none initialized: no GL context yet)
    using (Assert.EnterMultipleScope())
    {
      Assert.That(driver.GetSharedContext(), Is.Null);
      Assert.That(context.IsValid(), Is.False);
      Assert.That(frameBuffer.IsValid(), Is.False);
      Assert.That(texture.IsValid(), Is.False);
    }
  }

  [Test]
  public void Camera_LooksFromItsEyeToItsCenter()
  {
    // Arrange
    var camera = new Graphic3d_Camera();

    // Act
    camera.SetEye(new gp_Pnt(0, 0, 10));
    camera.SetCenter(new gp_Pnt(0, 0, 0));

    // Assert
    Assert.That(camera.Direction().IsEqual(new gp_Dir(0, 0, -1), 1e-12), Is.True);
  }

  [Test]
  public void CodePoint_IsAUint()
  {
    // Arrange (U+4E00 is the first CJK unified ideograph, U+0041 a Latin A)
    const uint ideograph = 0x4E00;
    const uint latin = 0x41;

    // Act
    var cjk = Font_FTFont.IsCharFromCJK(ideograph);
    var notCjk = Font_FTFont.IsCharFromCJK(latin);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(cjk, Is.True);
      Assert.That(notCjk, Is.False);
    }
  }

  [Test]
  public void CodePointReference_IsAUint()
  {
    // Arrange (a const char32_t&: a carriage return is a command symbol, a Latin A isn't)
    const uint carriageReturn = 0x0D;
    const uint latin = 0x41;

    // Act
    var command = Font_TextFormatter.IsCommandSymbol(carriageReturn);
    var notCommand = Font_TextFormatter.IsCommandSymbol(latin);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(command, Is.True);
      Assert.That(notCommand, Is.False);
    }
  }

  [Test]
  [Platform("Win")]
  public void NativeWindow_WrapsAWindowsHandle()
  {
    // Arrange (a popup window that is never shown)
    var handle = CreateWindowEx(0, "STATIC", "", WsPopup, 0, 0, 64, 48, IntPtr.Zero, IntPtr.Zero,
                                IntPtr.Zero, IntPtr.Zero);
    try
    {
      int width = 0, height = 0;

      // Act
      var window = Aspect_Window.FromNativeHandle(handle, null);
      window.Size(ref width, ref height);

      // Assert
      using (Assert.EnterMultipleScope())
      {
        Assert.That(window.NativeHandle(), Is.EqualTo(handle));
        Assert.That(width, Is.EqualTo(64));
        Assert.That(height, Is.EqualTo(48));
      }
    }
    finally
    {
      DestroyWindow(handle);
    }
  }
}
