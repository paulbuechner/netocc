// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Globalization;

namespace OCC.Core.Quantity;

/// <summary>
/// OCCT <c>Quantity_Color</c>: linear RGB as three floats (<c>myRgb</c>, an
/// <c>NCollection_Vec3&lt;float&gt;</c>). Managed reads and distances; the constructors run in OCCT
/// (color-space conversion, <c>Standard_OutOfRange</c> for components outside [0, 1]) like the rest
/// of Quantity_Color.g.cs. The parameterless constructor is OCCT's default color, yellow;
/// <c>default(Quantity_Color)</c> is black.
/// </summary>
public partial struct Quantity_Color : IEquatable<Quantity_Color>
{
  /// <summary>Linear red component.</summary>
  public readonly double Red() => myRgb_v_0;

  /// <summary>Linear green component.</summary>
  public readonly double Green() => myRgb_v_1;

  /// <summary>Linear blue component.</summary>
  public readonly double Blue() => myRgb_v_2;

  public readonly double SquareDistance(in Quantity_Color theColor)
  {
    double dr = (double)myRgb_v_0 - theColor.myRgb_v_0,
      dg = (double)myRgb_v_1 - theColor.myRgb_v_1,
      db = (double)myRgb_v_2 - theColor.myRgb_v_2;
    return dr * dr + dg * dg + db * db;
  }

  public readonly double Distance(in Quantity_Color theColor) =>
    Math.Sqrt(SquareDistance(theColor));

  /// <summary>
  /// Exact comparison (for dictionaries; NaN equals NaN); OCCT's tolerant one is <c>IsEqual</c>.
  /// </summary>
  public readonly bool Equals(Quantity_Color other) =>
    myRgb_v_0.Equals(other.myRgb_v_0)
    && myRgb_v_1.Equals(other.myRgb_v_1)
    && myRgb_v_2.Equals(other.myRgb_v_2);

  public override readonly bool Equals(object obj) => obj is Quantity_Color other && Equals(other);

  public override readonly int GetHashCode() =>
    unchecked((myRgb_v_0.GetHashCode() * 397 ^ myRgb_v_1.GetHashCode()) * 397
              ^ myRgb_v_2.GetHashCode());

  public override readonly string ToString() =>
    string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})", myRgb_v_0, myRgb_v_1, myRgb_v_2);
}
