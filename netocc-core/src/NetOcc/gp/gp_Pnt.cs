// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

namespace OCC.Core.gp;

/// <summary>
/// OCCT <c>gp_Pnt</c>: a 3D point. Managed math; the layout and the native members are in
/// gp_Pnt.g.cs.
/// </summary>
public partial struct gp_Pnt : IEquatable<gp_Pnt>
{
  public gp_Pnt()
  {
    // OCCT's default constructor, the origin: written here, so the generator leaves out a native
    // call for it
  }

  public gp_Pnt(double theXp, double theYp, double theZp) => coord = new(theXp, theYp, theZp);

  public gp_Pnt(in gp_XYZ theCoord) => coord = theCoord;

  public readonly double X() => coord.X();
  public readonly double Y() => coord.Y();
  public readonly double Z() => coord.Z();
  public void SetX(double theX) => coord.SetX(theX);
  public void SetY(double theY) => coord.SetY(theY);
  public void SetZ(double theZ) => coord.SetZ(theZ);

  public void SetCoord(double theXp, double theYp, double theZp) =>
    coord.SetCoord(theXp, theYp, theZp);

  public readonly void Coord(ref double theXp, ref double theYp, ref double theZp) =>
    coord.Coord(ref theXp, ref theYp, ref theZp);

  public readonly double Coord(int theIndex) => coord.Coord(theIndex);
  public readonly gp_XYZ XYZ() => coord;
  public void SetXYZ(in gp_XYZ theCoord) => coord = theCoord;

  public readonly double SquareDistance(in gp_Pnt theOther) =>
    coord.Subtracted(theOther.coord).SquareModulus();

  public readonly double Distance(in gp_Pnt theOther) => Math.Sqrt(SquareDistance(theOther));

  public readonly bool IsEqual(in gp_Pnt theOther, double theLinearTolerance) =>
    Distance(theOther) <= theLinearTolerance;

  public void Translate(in gp_Vec theV) => coord = coord.Added(theV.XYZ());
  public readonly gp_Pnt Translated(in gp_Vec theV) => new(coord.Added(theV.XYZ()));

  public readonly bool Equals(gp_Pnt other) => coord.Equals(other.coord);
  public override readonly bool Equals(object obj) => obj is gp_Pnt other && Equals(other);
  public override readonly int GetHashCode() => coord.GetHashCode();
  public override readonly string ToString() => coord.ToString();
}
