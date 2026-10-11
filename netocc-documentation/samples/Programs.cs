// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Reflection;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BRepGProp;
using OCC.Core.GProp;
using OCC.Core.IFSelect;
using OCC.Core.RWStl;
using OCC.Core.STEPControl;

namespace NetOcc.Samples;

/// <summary>
/// The start page's programs (Programs/*.cs, each a whole file with its Main): run, and what they
/// print and write checked against the geometry worked out by hand.
/// </summary>
[TestFixture]
public class Programs : Files
{
  [Test]
  public void Bracket_PrintsItsFacesAndThatItsValid()
  {
    // Act
    var output = Run(typeof(Bracket.Program));

    // Assert (the extruded L's 8 faces, and a cylindrical one per hole)
    Assert.That(output, Is.EqualTo("bracket: 10 faces, valid: True"));
  }

  [Test]
  public void Measure_PrintsTheShaftsProperties()
  {
    // Act
    var output = Run(typeof(Measure.Program));

    // Assert (the shaft above the flange: 70 long; the flange's top a ring of 25 less 10)
    var cm3 = Math.PI * (10 * 10 * 70 + 25 * 25 * 10) / 1000;
    var cm2 = Math.PI * (2 * 10 * 70 + 10 * 10 + (25 * 25 - 10 * 10) + 2 * 25 * 10 + 25 * 25) / 100;
    var centre = (10 * 10 * 70 * 45.0 + 25 * 25 * 10 * 5.0) / (10 * 10 * 70 + 25 * 25 * 10);
    Assert.That(
      output,
      Is.EqualTo(string.Join(Environment.NewLine,
                             $"volume: {cm3:F1} cm³, {cm3 * 7.85:F0} g in steel",
                             $"surface: {cm2:F1} cm²",
                             $"centre of mass: {centre:F2} mm up the axis",
                             $"height: {80.0:F1} mm")));
  }

  [Test]
  public void Exchange_WritesTheCylinderAsStepAndStl()
  {
    // Act
    var output = Run(typeof(Exchange.Program));

    // Assert (the STEP file's cylinder, its volume; the STL's triangles)
    var reader = new STEPControl_Reader();
    Assert.That(reader.ReadFile("part.step"), Is.EqualTo(IFSelect_ReturnStatus.IFSelect_RetDone));
    reader.TransferRoots();
    var properties = new GProp_GProps();
    BRepGProp.VolumeProperties(reader.OneShape(), properties);
    using (Assert.EnterMultipleScope())
    {
      Assert.That(output, Is.EqualTo($"wrote part.step{Environment.NewLine}wrote part.stl"));
      Assert.That(properties.Mass(), Is.EqualTo(Math.PI * 10 * 10 * 30).Within(1e-6));
      Assert.That(RWStl.ReadFile("part.stl").NbTriangles(), Is.GreaterThan(50));
    }
  }

  // runs a program's Main and returns what it printed
  private static string Run(Type program)
  {
    var main = program.GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static)!;
    var console = Console.Out;
    using var output = new StringWriter();
    Console.SetOut(output);
    try
    {
      main.Invoke(null, null);
    }
    finally
    {
      Console.SetOut(console);
    }

    return output.ToString().Trim();
  }
}
