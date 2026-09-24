// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: a value type (a C# struct with this layout), configured in modules.yaml. X() is hand-written
// (partials/Demo_Vec.cs); the rest becomes thunks. operator+= has no C# counterpart.
#pragma once

class Demo_Vec
{
public:
  Demo_Vec() : myX(0.0), myY(0.0), myZ(0.0) {}
  Demo_Vec(double theX, double theY, double theZ = 0.0) : myX(theX), myY(theY), myZ(theZ) {}
  double X() const { return myX; }
  void SetX(double theX) { myX = theX; }
  Demo_Vec Added(const Demo_Vec& theOther) const { return Demo_Vec(myX + theOther.myX, myY + theOther.myY, myZ + theOther.myZ); }
  static Demo_Vec Zero() { return Demo_Vec(); }
  Demo_Vec operator+(const Demo_Vec& theOther) const { return Added(theOther); }
  Demo_Vec operator-() const { return Demo_Vec(-myX, -myY, -myZ); }
  void operator+=(const Demo_Vec& theOther) { *this = Added(theOther); }

private:
  double myX;
  double myY;
  double myZ;
};
