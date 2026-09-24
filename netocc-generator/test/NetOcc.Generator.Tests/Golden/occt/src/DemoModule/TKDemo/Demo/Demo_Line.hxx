// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: a concrete transient with handles, a default argument and a member that is never defined.
#pragma once
#include <Demo_Curve.hxx>

class Demo_Line : public Demo_Curve
{
public:
  Demo_Line(const Demo_Vec& theDirection, double theLength = 1.0) : myDirection(theDirection), myLength(theLength) {}
  double Length() const override { return myLength; }
  Demo_Vec Tangent(double theU) const override { return myDirection; }
  static opencascade::handle<Demo_Line> Unit();
  static opencascade::handle<Demo_Curve> Copy(const opencascade::handle<Demo_Curve>& theCurve) { return theCurve; }

private:
  Demo_Vec myDirection;
  double myLength;
};
