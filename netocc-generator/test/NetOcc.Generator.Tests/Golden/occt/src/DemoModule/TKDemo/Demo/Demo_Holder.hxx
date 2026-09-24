// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: references into an object. A member's are C# refs (numbers, structs) or borrowing proxies (classes);
// a static one has no object to borrow from.
#pragma once
#include <Demo_Shape.hxx>
#include <Demo_Vec.hxx>

class Demo_Holder
{
public:
  Demo_Holder() : myCount(0) {}
  int& ChangeCount() { return myCount; }
  Demo_Vec& ChangeOrigin() { return myOrigin; }
  Demo_Shape& ChangeShape() { return myShape; }
  static Demo_Shape& Shared() { static Demo_Shape aShape; return aShape; }

private:
  int        myCount;
  Demo_Vec   myOrigin;
  Demo_Shape myShape;
};
