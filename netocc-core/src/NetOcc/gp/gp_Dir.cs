// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

namespace OCC.Core.gp;

/// <summary>
/// OCCT <c>gp_Dir</c>: a unit vector. Managed accessors; the constructors run in OCCT
/// (normalization, <c>Standard_ConstructionError</c> for a null vector) like the rest of
/// gp_Dir.g.cs. <c>default(gp_Dir)</c> is all-zero and invalid; use a constructor.
/// </summary>
public partial struct gp_Dir : IEquatable<gp_Dir>
{
  // already unit length: no normalization, no native call
  private gp_Dir(in gp_XYZ theUnitCoord, bool _) => coord = theUnitCoord;

  public readonly double X() => coord.X();
  public readonly double Y() => coord.Y();
  public readonly double Z() => coord.Z();

  public readonly void Coord(ref double theXv, ref double theYv, ref double theZv) =>
    coord.Coord(ref theXv, ref theYv, ref theZv);

  public readonly double Coord(int theIndex) => coord.Coord(theIndex);
  public readonly gp_XYZ XYZ() => coord;

  public readonly double Dot(in gp_Dir theOther) => coord.Dot(theOther.coord);
  public readonly gp_Dir Reversed() => new(coord.Reversed(), true);

  public readonly bool Equals(gp_Dir other) => coord.Equals(other.coord);
  public override readonly bool Equals(object obj) => obj is gp_Dir other && Equals(other);
  public override readonly int GetHashCode() => coord.GetHashCode();
  public override readonly string ToString() => coord.ToString();
}
