// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

namespace OCC.Core.gp;

/// <summary>
/// OCCT <c>gp_Vec</c>: a 3D vector. Managed math; the layout and the native members are in
/// gp_Vec.g.cs.
/// </summary>
public partial struct gp_Vec : IEquatable<gp_Vec>
{
  public gp_Vec()
  {
    // OCCT's default constructor, the null vector: written here, so the generator leaves out a
    // native call for it
  }

  public gp_Vec(double theXv, double theYv, double theZv) => coord = new(theXv, theYv, theZv);

  public gp_Vec(in gp_XYZ theCoord) => coord = theCoord;

  public gp_Vec(in gp_Dir theV) => coord = theV.XYZ();

  /// <summary>Vector from theP1 to theP2.</summary>
  public gp_Vec(in gp_Pnt theP1, in gp_Pnt theP2) => coord = theP2.XYZ().Subtracted(theP1.XYZ());

  public readonly double X() => coord.X();
  public readonly double Y() => coord.Y();
  public readonly double Z() => coord.Z();
  public void SetX(double theX) => coord.SetX(theX);
  public void SetY(double theY) => coord.SetY(theY);
  public void SetZ(double theZ) => coord.SetZ(theZ);

  public void SetCoord(double theXv, double theYv, double theZv) =>
    coord.SetCoord(theXv, theYv, theZv);

  public readonly void Coord(ref double theXv, ref double theYv, ref double theZv) =>
    coord.Coord(ref theXv, ref theYv, ref theZv);

  public readonly gp_XYZ XYZ() => coord;

  public readonly double Magnitude() => coord.Modulus();
  public readonly double SquareMagnitude() => coord.SquareModulus();
  public readonly gp_Vec Added(in gp_Vec theOther) => new(coord.Added(theOther.coord));
  public readonly gp_Vec Subtracted(in gp_Vec theOther) => new(coord.Subtracted(theOther.coord));
  public readonly gp_Vec Multiplied(double theScalar) => new(coord.Multiplied(theScalar));
  public readonly gp_Vec Reversed() => new(coord.Reversed());
  public readonly double Dot(in gp_Vec theOther) => coord.Dot(theOther.coord);
  public readonly gp_Vec Crossed(in gp_Vec theRight) => new(coord.Crossed(theRight.coord));

  public static gp_Vec operator +(gp_Vec a, gp_Vec b) => a.Added(b);
  public static gp_Vec operator -(gp_Vec a, gp_Vec b) => a.Subtracted(b);
  public static gp_Vec operator -(gp_Vec a) => a.Reversed();
  public static gp_Vec operator *(gp_Vec a, double s) => a.Multiplied(s);
  public static gp_Vec operator *(double s, gp_Vec a) => a.Multiplied(s);

  public readonly bool Equals(gp_Vec other) => coord.Equals(other.coord);
  public override readonly bool Equals(object obj) => obj is gp_Vec other && Equals(other);
  public override readonly int GetHashCode() => coord.GetHashCode();
  public override readonly string ToString() => coord.ToString();
}
