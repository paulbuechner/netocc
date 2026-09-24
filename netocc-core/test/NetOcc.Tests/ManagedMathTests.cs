// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Linq;

// NUnit
using NUnit.Framework;

//
using OCC.Core;
using OCC.Core.gp;
using OCC.Core.Poly;
using OCC.Core.Quantity;

// static usings
using static OCC.Core.Quantity.Quantity_TypeOfColor;

// alias directives
using VecMember = System.Func<OCC.Core.gp.gp_Vec, OCC.Core.gp.gp_Vec, double, double[]>;
using XYZMember = System.Func<OCC.Core.gp.gp_XYZ, OCC.Core.gp.gp_XYZ, double, double[]>;

namespace NetOcc.Tests;

/// <summary>
/// The value types' hand-written members (gp, Quantity_Color, Poly_Triangle) against OCCT's native
/// members for the same math, over pseudo-random inputs; their errors, equality and text.
/// </summary>
[TestFixture]
public class ManagedMathTests
{
  private const int Count = 1000;

  // far above what a native build rounds differently (FMA, x87) at coordinates up to 100
  private const double Tolerance = 1e-9;

  // each managed member or operator over a, b and a scalar s, beside the native member it must
  // match (a ^ b and a * b, the cross and dot products, are OCCT's operators)
  private static TestCaseData[] XYZMembers() =>
  [
    XYZCase("Added", (a, b, s) => Coordinates(a.Added(b)), (a, b, s) =>
    {
      a.Add(b);
      return Coordinates(a);
    }),
    XYZCase("a + b", (a, b, s) => Coordinates(a + b), (a, b, s) =>
    {
      a.Add(b);
      return Coordinates(a);
    }),
    XYZCase("Subtracted", (a, b, s) => Coordinates(a.Subtracted(b)), (a, b, s) =>
    {
      a.Subtract(b);
      return Coordinates(a);
    }),
    XYZCase("a - b", (a, b, s) => Coordinates(a - b), (a, b, s) =>
    {
      a.Subtract(b);
      return Coordinates(a);
    }),
    XYZCase("Multiplied", (a, b, s) => Coordinates(a.Multiplied(s)), (a, b, s) =>
    {
      a.Multiply(s);
      return Coordinates(a);
    }),
    XYZCase("a * s", (a, b, s) => Coordinates(a * s), (a, b, s) =>
    {
      a.Multiply(s);
      return Coordinates(a);
    }),
    XYZCase("s * a", (a, b, s) => Coordinates(s * a), (a, b, s) =>
    {
      a.Multiply(s);
      return Coordinates(a);
    }),
    XYZCase("Reversed", (a, b, s) => Coordinates(a.Reversed()), (a, b, s) =>
    {
      a.Reverse();
      return Coordinates(a);
    }),
    XYZCase("-a", (a, b, s) => Coordinates(-a), (a, b, s) =>
    {
      a.Reverse();
      return Coordinates(a);
    }),
    XYZCase("Crossed", (a, b, s) => Coordinates(a.Crossed(b)), (a, b, s) => Coordinates(a ^ b)),
    XYZCase("Dot", (a, b, s) => [a.Dot(b)], (a, b, s) => [a * b]),
    XYZCase("SquareModulus", (a, b, s) => [a.SquareModulus()], (a, b, s) => [a * a]),
    XYZCase("Modulus", (a, b, s) => [a.Modulus()],
            (a, b, s) => [NativeDistance(new gp_Pnt(), new gp_Pnt(a))]),
  ];

  private static TestCaseData[] VecMembers() =>
  [
    VecCase("Added", (a, b, s) => Coordinates(a.Added(b)), (a, b, s) =>
    {
      a.Add(b);
      return Coordinates(a);
    }),
    VecCase("a + b", (a, b, s) => Coordinates(a + b), (a, b, s) =>
    {
      a.Add(b);
      return Coordinates(a);
    }),
    VecCase("Subtracted", (a, b, s) => Coordinates(a.Subtracted(b)), (a, b, s) =>
    {
      a.Subtract(b);
      return Coordinates(a);
    }),
    VecCase("a - b", (a, b, s) => Coordinates(a - b), (a, b, s) =>
    {
      a.Subtract(b);
      return Coordinates(a);
    }),
    VecCase("Multiplied", (a, b, s) => Coordinates(a.Multiplied(s)), (a, b, s) =>
    {
      a.Multiply(s);
      return Coordinates(a);
    }),
    VecCase("a * s", (a, b, s) => Coordinates(a * s), (a, b, s) =>
    {
      a.Multiply(s);
      return Coordinates(a);
    }),
    VecCase("s * a", (a, b, s) => Coordinates(s * a), (a, b, s) =>
    {
      a.Multiply(s);
      return Coordinates(a);
    }),
    VecCase("Reversed", (a, b, s) => Coordinates(a.Reversed()), (a, b, s) =>
    {
      a.Reverse();
      return Coordinates(a);
    }),
    VecCase("-a", (a, b, s) => Coordinates(-a), (a, b, s) =>
    {
      a.Reverse();
      return Coordinates(a);
    }),
    VecCase("Crossed", (a, b, s) => Coordinates(a.Crossed(b)), (a, b, s) => Coordinates(a ^ b)),
    VecCase("Dot", (a, b, s) => [a.Dot(b)], (a, b, s) => [a * b]),
    VecCase("SquareMagnitude", (a, b, s) => [a.SquareMagnitude()], (a, b, s) => [a * a]),
    VecCase("Magnitude", (a, b, s) => [a.Magnitude()],
            (a, b, s) => [NativeDistance(new gp_Pnt(), new gp_Pnt(a.XYZ()))]),
  ];

  private static TestCaseData XYZCase(string member, XYZMember managed, XYZMember native) =>
    new TestCaseData(managed, native).SetArgDisplayNames(member);

  private static TestCaseData VecCase(string member, VecMember managed, VecMember native) =>
    new TestCaseData(managed, native).SetArgDisplayNames(member);

  [TestCaseSource(nameof(XYZMembers))]
  public void XYZ_MatchesNative(XYZMember managed, XYZMember native)
  {
    // Arrange
    var random = new Random(1);
    var a = RandomXYZs(random);
    var b = RandomXYZs(random);
    var s = RandomScalars(random);

    // Act
    var managedResults =
      Enumerable.Range(0, Count).SelectMany(i => managed(a[i], b[i], s[i])).ToArray();
    var nativeResults =
      Enumerable.Range(0, Count).SelectMany(i => native(a[i], b[i], s[i])).ToArray();

    // Assert
    Assert.That(managedResults, Is.EqualTo(nativeResults).Within(Tolerance));
  }

  [TestCaseSource(nameof(VecMembers))]
  public void Vec_MatchesNative(VecMember managed, VecMember native)
  {
    // Arrange
    var random = new Random(2);
    var a = RandomVectors(random);
    var b = RandomVectors(random);
    var s = RandomScalars(random);

    // Act
    var managedResults =
      Enumerable.Range(0, Count).SelectMany(i => managed(a[i], b[i], s[i])).ToArray();
    var nativeResults =
      Enumerable.Range(0, Count).SelectMany(i => native(a[i], b[i], s[i])).ToArray();

    // Assert
    Assert.That(managedResults, Is.EqualTo(nativeResults).Within(Tolerance));
  }

  [Test]
  public void Vec_ConstructorsMatchNative()
  {
    // Arrange
    var random = new Random(3);
    var xyz = RandomXYZs(random);
    var from = RandomPoints(random);
    var to = RandomPoints(random);

    // Act
    var byNumbers = xyz.SelectMany(c =>
      {
        var vector = new gp_Vec(c.X(), c.Y(), c.Z());
        return new[] { vector.Coord(1), vector.Coord(2), vector.Coord(3) };
      })
      .ToArray();
    var byXYZ = xyz.SelectMany(c => Coordinates(new gp_Vec(c))).ToArray();
    var setXYZ = xyz.SelectMany(c =>
      {
        var vector = new gp_Vec();
        vector.SetXYZ(c);
        return Coordinates(vector);
      })
      .ToArray();
    var byDirection = xyz.SelectMany(c => Coordinates(new gp_Vec(new gp_Dir(c)))).ToArray();
    var normalized = xyz.SelectMany(c => Coordinates(new gp_Vec(c).Normalized())).ToArray();
    var byPoints = Enumerable.Range(0, Count)
      .SelectMany(i => Coordinates(new gp_Vec(from[i], to[i])))
      .ToArray();
    var translation = Enumerable.Range(0, Count)
      .SelectMany(i => Coordinates(new gp_Pnt().Translated(from[i], to[i])))
      .ToArray();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(byNumbers, Is.EqualTo(xyz.SelectMany(Coordinates)),
                  "(x, y, z), as OCCT's Coord(int) reads them");
      Assert.That(byXYZ, Is.EqualTo(setXYZ), "(gp_XYZ), as OCCT's SetXYZ");
      Assert.That(byDirection, Is.EqualTo(normalized).Within(Tolerance),
                  "(gp_Dir), as OCCT normalizes");
      Assert.That(byPoints, Is.EqualTo(translation).Within(Tolerance),
                  "(p1, p2), as OCCT translates from p1 to p2");
    }
  }

  [Test]
  public void Pnt_SquareDistanceMatchesNative()
  {
    // Arrange
    var random = new Random(4);
    var a = RandomPoints(random);
    var b = RandomPoints(random);

    // Act
    var managed = Enumerable.Range(0, Count).Select(i => a[i].SquareDistance(b[i])).ToArray();
    var native = Enumerable.Range(0, Count)
      .Select(i => NativeDistance(a[i], b[i]))
      .Select(distance => distance * distance)
      .ToArray();

    // Assert
    Assert.That(managed, Is.EqualTo(native).Within(Tolerance));
  }

  [Test]
  public void Pnt_IsEqualMatchesNativeDistance()
  {
    // Arrange (tolerances around each distance: OCCT's IsEqual is Distance <= tolerance)
    var random = new Random(5);
    var a = RandomPoints(random);
    var b = RandomPoints(random);
    var tolerances = Enumerable.Range(0, Count)
      .Select(i => NativeDistance(a[i], b[i]) * (0.5 + random.NextDouble()))
      .ToArray();

    // Act
    var managed = Enumerable.Range(0, Count)
      .Select(i => a[i].IsEqual(b[i], tolerances[i]))
      .ToArray();
    var native = Enumerable.Range(0, Count)
      .Select(i => NativeDistance(a[i], b[i]) <= tolerances[i])
      .ToArray();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(managed, Is.EqualTo(native));
      Assert.That(native, Does.Contain(true).And.Contain(false),
                  "tolerances on both sides of the distance");
    }
  }

  [Test]
  public void Pnt_TranslationMatchesNative()
  {
    // Arrange (OCCT translates by a gp_Trsf too)
    var random = new Random(6);
    var points = RandomPoints(random);
    var vectors = RandomVectors(random);
    var translations = vectors.Select(vector =>
      {
        var translation = new gp_Trsf();
        translation.SetTranslation(vector);
        return translation;
      })
      .ToArray();

    // Act
    var translated = Enumerable.Range(0, Count)
      .SelectMany(i => Coordinates(points[i].Translated(vectors[i])))
      .ToArray();
    var translatedInPlace = Enumerable.Range(0, Count)
      .SelectMany(i =>
      {
        var point = points[i];
        point.Translate(vectors[i]);
        return Coordinates(point);
      })
      .ToArray();
    var native = Enumerable.Range(0, Count)
      .SelectMany(i => Coordinates(points[i].Transformed(translations[i])))
      .ToArray();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(translated, Is.EqualTo(native).Within(Tolerance), "Translated");
      Assert.That(translatedInPlace, Is.EqualTo(native).Within(Tolerance), "Translate");
    }
  }

  [Test]
  public void Dir_ReversedMatchesNative()
  {
    // Arrange
    var directions = RandomDirections(new Random(7));

    // Act
    var managed = directions.Select(direction => direction.Reversed()).ToArray();
    var native = directions.Select(direction => -direction).ToArray(); // OCCT's operator-

    // Assert
    Assert.That(managed, Is.EqualTo(native));
  }

  [Test]
  public void Dir_DotMatchesNative()
  {
    // Arrange
    var random = new Random(8);
    var a = RandomDirections(random);
    var b = RandomDirections(random);

    // Act
    var managed = Enumerable.Range(0, Count).Select(i => a[i].Dot(b[i])).ToArray();
    var native = Enumerable.Range(0, Count).Select(i => a[i] * b[i]).ToArray(); // OCCT's operator*

    // Assert
    Assert.That(managed, Is.EqualTo(native).Within(Tolerance));
  }

  [Test]
  public void Ax1_ReversedMatchesNative()
  {
    // Arrange
    var random = new Random(9);
    var locations = RandomPoints(random);
    var directions = RandomDirections(random);
    var axes = Enumerable.Range(0, Count)
      .Select(i => new gp_Ax1(locations[i], directions[i]))
      .ToArray();

    // Act
    var managed = axes.Select(axis => axis.Reversed()).ToArray();
    var native = axes.Select(axis =>
      {
        axis.Reverse();
        return axis;
      })
      .ToArray();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(managed.Select(axis => axis.Location()),
                  Is.EqualTo(native.Select(axis => axis.Location())));
      Assert.That(managed.Select(axis => axis.Direction()),
                  Is.EqualTo(native.Select(axis => axis.Direction())));
    }
  }

  [Test]
  public void Ax1_SettersMatchTheNativeConstructor()
  {
    // Arrange (OCCT's gp_Ax1(gp_Pnt, gp_Dir::D), for each named direction in turn)
    var locations = RandomPoints(new Random(10));
    var named = Enumerable.Range(0, Count).Select(i => (gp_Dir_D)(i % 6)).ToArray();

    // Act
    var managed = Enumerable.Range(0, Count)
      .Select(i =>
      {
        var axis = new gp_Ax1();
        axis.SetLocation(locations[i]);
        axis.SetDirection(new gp_Dir(named[i]));
        return axis;
      })
      .ToArray();
    var native = Enumerable.Range(0, Count)
      .Select(i => new gp_Ax1(locations[i], named[i]))
      .ToArray();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(managed.Select(axis => axis.Location()),
                  Is.EqualTo(native.Select(axis => axis.Location())));
      Assert.That(managed.Select(axis => axis.Direction()),
                  Is.EqualTo(native.Select(axis => axis.Direction())));
    }
  }

  [Test]
  public void Ax2_LocationIsWhatTheNativeConstructorStored()
  {
    // Arrange
    var random = new Random(11);
    var locations = RandomPoints(random);
    var directions = RandomDirections(random);

    // Act
    var frames = Enumerable.Range(0, Count)
      .Select(i => new gp_Ax2(locations[i], directions[i]))
      .ToArray();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(frames.Select(frame => frame.Location()), Is.EqualTo(locations));
      Assert.That(frames.Select(frame => frame.Direction()), Is.EqualTo(directions));
    }
  }

  [Test]
  public void Ax2_SetLocationMatchesNativeTranslation()
  {
    // Arrange (OCCT moves a frame by the vector from its location to the new one)
    var random = new Random(12);
    var locations = RandomPoints(random);
    var directions = RandomDirections(random);
    var targets = RandomPoints(random);
    var frames = Enumerable.Range(0, Count)
      .Select(i => new gp_Ax2(locations[i], directions[i]))
      .ToArray();

    // Act
    var moved = frames.Select((frame, i) =>
      {
        frame.SetLocation(targets[i]);
        return frame;
      })
      .ToArray();
    var translated = frames.Select((frame, i) => frame.Translated(locations[i], targets[i]))
      .ToArray();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(moved.SelectMany(frame => Coordinates(frame.Location())),
                  Is.EqualTo(translated.SelectMany(frame => Coordinates(frame.Location())))
                    .Within(Tolerance));
      Assert.That(moved.Select(frame => frame.Direction()),
                  Is.EqualTo(translated.Select(frame => frame.Direction())));
      Assert.That(moved.Select(frame => frame.XDirection()),
                  Is.EqualTo(translated.Select(frame => frame.XDirection())));
    }
  }

  [Test]
  public void Color_DistancesMatchTheNativeComponents()
  {
    // Arrange
    var random = new Random(13);
    var a = RandomColors(random);
    var b = RandomColors(random);

    // Act
    var squareDistances =
      Enumerable.Range(0, Count).Select(i => a[i].SquareDistance(b[i])).ToArray();
    var distances = Enumerable.Range(0, Count).Select(i => a[i].Distance(b[i])).ToArray();
    var fromComponents = Enumerable.Range(0, Count)
      .Select(i => ComponentSquareDistance(a[i], b[i]))
      .ToArray();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(squareDistances, Is.EqualTo(fromComponents).Within(Tolerance), "SquareDistance");
      Assert.That(distances, Is.EqualTo(fromComponents.Select(Math.Sqrt)).Within(Tolerance),
                  "Distance");
    }
  }

  [Test]
  public void Color_SquareDistanceDecidesLikeNativeIsDifferent()
  {
    // Arrange (pairs about Epsilon() apart: OCCT's IsDifferent compares its own SquareDistance with
    // Epsilon() squared)
    var random = new Random(14);
    var epsilon = Quantity_Color.Epsilon();
    var a = RandomColors(random);
    var b = a.Select(color => Moved(color, epsilon, random)).ToArray();

    // Act
    var managed = Enumerable.Range(0, Count)
      .Select(i => a[i].SquareDistance(b[i]) > epsilon * epsilon)
      .ToArray();
    var native = Enumerable.Range(0, Count).Select(i => a[i].IsDifferent(b[i])).ToArray();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(managed, Is.EqualTo(native));
      Assert.That(native, Does.Contain(true).And.Contain(false),
                  "pairs on both sides of Epsilon()");
    }
  }

  [TestCase(0)]
  [TestCase(4)]
  public void XYZ_CoordOutOfRange_ThrowsLikeNative(int index)
  {
    // Arrange (SetCoord(int, double) is OCCT's, with the same check)
    var xyz = new gp_XYZ(1, 2, 3);

    // Act
    Action managed = () => _ = xyz.Coord(index);
    Action native = () => xyz.SetCoord(index, 0);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(
        managed,
        Throws.TypeOf<OcctException>()
          .With.Property(nameof(OcctException.OcctType))
          .EqualTo("Standard_OutOfRange"));
      Assert.That(
        native,
        Throws.TypeOf<OcctException>()
          .With.Property(nameof(OcctException.OcctType))
          .EqualTo("Standard_OutOfRange"));
    }
  }

  [TestCase(0)]
  [TestCase(4)]
  public void PolyTriangle_ValueOutOfRange_ThrowsLikeNative(int index)
  {
    // Arrange (Set(int, int) is OCCT's, with the same check)
    var triangle = new Poly_Triangle(1, 2, 3);

    // Act
    Action managed = () => _ = triangle.Value(index);
    Action native = () => triangle.Set(index, 4);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(
        managed,
        Throws.TypeOf<OcctException>()
          .With.Property(nameof(OcctException.OcctType))
          .EqualTo("Standard_OutOfRange"));
      Assert.That(
        native,
        Throws.TypeOf<OcctException>()
          .With.Property(nameof(OcctException.OcctType))
          .EqualTo("Standard_OutOfRange"));
    }
  }

  [Test]
  public void EqualValues_HaveEqualHashCodes()
  {
    // Arrange (each value built twice; a reversed zero is -0.0, which equals 0.0)
    object[] values =
    [
      new gp_XYZ(1.5, -2, 3), new gp_Pnt(1.5, -2, 3), new gp_Vec(1.5, -2, 3),
      new gp_Dir(1.5, -2, 3),
      new Quantity_Color(0.25, 0.5, 0.75, Quantity_TOC_RGB), new Poly_Triangle(4, 5, 6),
      new gp_XYZ(0, 1, 2).Reversed(),
    ];
    object[] equal =
    [
      new gp_XYZ(1.5, -2, 3), new gp_Pnt(1.5, -2, 3), new gp_Vec(1.5, -2, 3),
      new gp_Dir(1.5, -2, 3),
      new Quantity_Color(0.25, 0.5, 0.75, Quantity_TOC_RGB), new Poly_Triangle(4, 5, 6),
      new gp_XYZ(0, -1, -2),
    ];

    // Act
    var hashCodes = values.Select(value => value.GetHashCode()).ToArray();
    var equalHashCodes = equal.Select(value => value.GetHashCode()).ToArray();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(equal, Is.EqualTo(values));
      Assert.That(equalHashCodes, Is.EqualTo(hashCodes));
    }
  }

  [Test]
  public void Equals_HoldsForANaNComponent()
  {
    // Arrange (OCCT's range check lets a NaN color component through: NaN fails both of its
    // comparisons)
    var color = new Quantity_Color(double.NaN, 0.5, 0.5, Quantity_TOC_RGB);
    var xyz = new gp_XYZ(double.NaN, 0, 0);

    // Act
    var colorEqualsItself = color.Equals(color);
    var xyzEqualsItself = xyz.Equals(xyz);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(colorEqualsItself, Is.True, "Quantity_Color");
      Assert.That(xyzEqualsItself, Is.True, "gp_XYZ");
    }
  }

  [Test]
  [SetCulture("de-DE")]
  public void ToString_IsCultureInvariant()
  {
    // Arrange (German writes 1.5 as 1,5)
    var xyz = new gp_XYZ(1.5, 2, 3);
    var point = new gp_Pnt(1.5, 2, 3);
    var color = new Quantity_Color(0.5, 0.25, 1, Quantity_TOC_RGB);

    // Act
    string[] texts = [xyz.ToString(), point.ToString(), color.ToString()];

    // Assert
    Assert.That(texts, Is.EqualTo(new[] { "(1.5, 2, 3)", "(1.5, 2, 3)", "(0.5, 0.25, 1)" }));
  }

  // Count coordinate triples in [-100, 100)
  private static gp_XYZ[] RandomXYZs(Random random) =>
    Enumerable.Range(0, Count)
      .Select(_ => new gp_XYZ(Coordinate(random), Coordinate(random), Coordinate(random)))
      .ToArray();

  private static gp_Vec[] RandomVectors(Random random) =>
    RandomXYZs(random).Select(xyz => new gp_Vec(xyz)).ToArray();

  private static gp_Pnt[] RandomPoints(Random random) =>
    RandomXYZs(random).Select(xyz => new gp_Pnt(xyz)).ToArray();

  // normalized by OCCT's constructor
  private static gp_Dir[] RandomDirections(Random random) =>
    RandomXYZs(random).Select(xyz => new gp_Dir(xyz)).ToArray();

  private static double[] RandomScalars(Random random) => Enumerable.Range(0, Count)
    .Select(_ => random.NextDouble() * 20 - 10)
    .ToArray();

  private static double Coordinate(Random random) => random.NextDouble() * 200 - 100;

  // components in [0.1, 0.9): moved by Epsilon(), they stay within [0, 1]
  private static Quantity_Color[] RandomColors(Random random) =>
    Enumerable.Range(0, Count)
      .Select(_ => new Quantity_Color(Component(random), Component(random), Component(random),
                                      Quantity_TOC_RGB))
      .ToArray();

  private static double Component(Random random) => 0.1 + 0.8 * random.NextDouble();

  // each component up to distance away
  private static Quantity_Color Moved(Quantity_Color color, double distance, Random random) =>
    new(color.Red() + distance * (2 * random.NextDouble() - 1),
        color.Green() + distance * (2 * random.NextDouble() - 1),
        color.Blue() + distance * (2 * random.NextDouble() - 1), Quantity_TOC_RGB);

  // from the linear RGB components OCCT's Values returns
  private static double ComponentSquareDistance(Quantity_Color first, Quantity_Color second)
  {
    double r1 = 0, g1 = 0, b1 = 0, r2 = 0, g2 = 0, b2 = 0;
    first.Values(ref r1, ref g1, ref b1, Quantity_TOC_RGB);
    second.Values(ref r2, ref g2, ref b2, Quantity_TOC_RGB);
    return (r1 - r2) * (r1 - r2) + (g1 - g2) * (g1 - g2) + (b1 - b2) * (b1 - b2);
  }

  private static double NativeDistance(gp_Pnt a, gp_Pnt b) =>
    gpModule.NetOcc_gp_Pnt_NativeDistance(in a, in b);

  private static double[] Coordinates(gp_XYZ value) => [value.X(), value.Y(), value.Z()];

  private static double[] Coordinates(gp_Vec value) => [value.X(), value.Y(), value.Z()];

  private static double[] Coordinates(gp_Pnt value) => [value.X(), value.Y(), value.Z()];
}
