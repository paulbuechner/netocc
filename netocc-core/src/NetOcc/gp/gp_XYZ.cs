// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Globalization;

namespace OCC.Core.gp;

/// <summary>
/// OCCT <c>gp_XYZ</c>: three doubles. Managed math; the layout and the native members are in
/// gp_XYZ.g.cs.
/// </summary>
public partial struct gp_XYZ : IEquatable<gp_XYZ>
{
  public gp_XYZ()
  {
    // OCCT's default constructor, all zero: written here, so the generator leaves out a native call
    // for it
  }

  public gp_XYZ(double theX, double theY, double theZ)
  {
    x = theX;
    y = theY;
    z = theZ;
  }

  public readonly double X() => x;
  public readonly double Y() => y;
  public readonly double Z() => z;
  public void SetX(double theX) => x = theX;
  public void SetY(double theY) => y = theY;
  public void SetZ(double theZ) => z = theZ;

  public void SetCoord(double theX, double theY, double theZ)
  {
    x = theX;
    y = theY;
    z = theZ;
  }

  public readonly void Coord(ref double theX, ref double theY, ref double theZ)
  {
    theX = x;
    theY = y;
    theZ = z;
  }

  /// <summary>1-based component access, like OCCT (1 = X, 2 = Y, 3 = Z).</summary>
  public readonly double Coord(int theIndex) => theIndex switch
  {
    1 => x,
    2 => y,
    3 => z,
    _ => throw new OcctException("Standard_OutOfRange", "gp_XYZ::Coord() - index is out of range"),
  };

  public readonly double Modulus() => Math.Sqrt(SquareModulus());
  public readonly double SquareModulus() => x * x + y * y + z * z;

  public readonly gp_XYZ Added(in gp_XYZ theOther) =>
    new(x + theOther.x, y + theOther.y, z + theOther.z);

  public readonly gp_XYZ Subtracted(in gp_XYZ theOther) =>
    new(x - theOther.x, y - theOther.y, z - theOther.z);

  public readonly gp_XYZ Multiplied(double theScalar) =>
    new(x * theScalar, y * theScalar, z * theScalar);

  public readonly gp_XYZ Reversed() => new(-x, -y, -z);

  public readonly double Dot(in gp_XYZ theOther) =>
    x * theOther.x + y * theOther.y + z * theOther.z;

  public readonly gp_XYZ Crossed(in gp_XYZ theOther) =>
    new(y * theOther.z - z * theOther.y, z * theOther.x - x * theOther.z,
        x * theOther.y - y * theOther.x);

  public static gp_XYZ operator +(gp_XYZ a, gp_XYZ b) => a.Added(b);
  public static gp_XYZ operator -(gp_XYZ a, gp_XYZ b) => a.Subtracted(b);
  public static gp_XYZ operator -(gp_XYZ a) => a.Reversed();
  public static gp_XYZ operator *(gp_XYZ a, double s) => a.Multiplied(s);
  public static gp_XYZ operator *(double s, gp_XYZ a) => a.Multiplied(s);

  /// <summary>
  /// Exact comparison (for dictionaries; NaN equals NaN); use a tolerance for geometry.
  /// </summary>
  public readonly bool Equals(gp_XYZ other) =>
    x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z);

  public override readonly bool Equals(object obj) => obj is gp_XYZ other && Equals(other);

  public override readonly int GetHashCode() =>
    unchecked((x.GetHashCode() * 397 ^ y.GetHashCode()) * 397 ^ z.GetHashCode());

  public override readonly string ToString() =>
    string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})", x, y, z);
}
