// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: an abstract transient (no constructors) with a value-type return and an enum reference, and nested
// types (OCCT 8's evaluation results): flat names, the plain-data struct a C# struct, an enum on unsigned short, a
// default argument naming a nested enumerator.
#pragma once
#include <Demo_Kind.hxx>
#include <Demo_Vec.hxx>
#include <Standard_Transient.hxx>

class Demo_Curve : public Standard_Transient
{
public:
  struct ResD1
  {
    Demo_Vec Point;
    Demo_Vec D1;
  };

  enum class Continuity
  {
    C0,
    C1
  };

  enum class Flags : unsigned short
  {
    None     = 0,
    Reserved = 0xF000
  };

  virtual double Length() const = 0;
  virtual ResD1 EvalD1(double theU) const { return ResD1(); }
  virtual Continuity Smoothness() const { return Continuity::C1; }
  virtual void       Smooth(Continuity theTo = Continuity::C0) {}
  Flags              State() const { return Flags::None; }
  void               State(Flags& theFlags) const { theFlags = State(); }
  virtual Demo_Vec Tangent(double theU) const = 0;
  Demo_Kind Kind() const { return Demo_Kind_Line; }
  void Kind(Demo_Kind& theKind) const { theKind = Kind(); }
};
