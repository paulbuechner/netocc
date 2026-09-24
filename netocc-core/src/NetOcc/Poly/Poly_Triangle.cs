// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Globalization;

namespace OCC.Core.Poly;

/// <summary>
/// OCCT <c>Poly_Triangle</c>: three 1-based node indices (<c>myNodes[3]</c>). Managed accessors;
/// the layout and the native members are in Poly_Triangle.g.cs.
/// </summary>
public partial struct Poly_Triangle : IEquatable<Poly_Triangle>
{
  public Poly_Triangle()
  {
    // OCCT's default constructor, all indices 0: written here, so the generator leaves out a native
    // call for it
  }

  public Poly_Triangle(int theN1, int theN2, int theN3) => Set(theN1, theN2, theN3);

  public void Set(int theN1, int theN2, int theN3)
  {
    myNodes_0 = theN1;
    myNodes_1 = theN2;
    myNodes_2 = theN3;
  }

  public readonly void Get(ref int theN1, ref int theN2, ref int theN3)
  {
    theN1 = myNodes_0;
    theN2 = myNodes_1;
    theN3 = myNodes_2;
  }

  /// <summary>1-based, like OCCT.</summary>
  public readonly int Value(int theIndex) => theIndex switch
  {
    1 => myNodes_0,
    2 => myNodes_1,
    3 => myNodes_2,
    _ => throw new OcctException("Standard_OutOfRange",
                                 "Poly_Triangle::Value() - index is out of range"),
  };

  public readonly bool Equals(Poly_Triangle other) => myNodes_0 == other.myNodes_0
                                                      && myNodes_1 == other.myNodes_1
                                                      && myNodes_2 == other.myNodes_2;

  public override readonly bool Equals(object obj) => obj is Poly_Triangle other && Equals(other);

  public override readonly int GetHashCode() =>
    unchecked((myNodes_0 * 397 ^ myNodes_1) * 397 ^ myNodes_2);

  public override readonly string ToString() =>
    string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})", myNodes_0, myNodes_1, myNodes_2);
}
