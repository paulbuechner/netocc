// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: a value class (copyable proxy); mutable reference returns and operators are left out, streams lent;
// a deprecated overload is [Obsolete], its default overloads too.
#pragma once
#include <Standard_OStream.hxx>

class Demo_Shape
{
public:
  Demo_Shape() : myOrientation(0) {}
  bool IsNull() const { return true; }
  Demo_Shape Reversed() const { return *this; }
  [[deprecated("use Reversed()")]] void Reversed(Demo_Shape& theResult, bool theDeep = false) const {}
  Demo_Shape& Reverse() { return *this; }
  void SetName(const char* theName) {}
  void DumpJson(Standard_OStream& theStream, int theDepth = -1) const {}
  Standard_OStream& Print(Standard_OStream& theStream) const { return theStream; }
  bool operator==(const Demo_Shape& theOther) const { return myOrientation == theOther.myOrientation; }

private:
  int myOrientation;
};
