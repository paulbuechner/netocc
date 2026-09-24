// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: raw pointers. A class is its proxy (a member's return borrows from the object), numbers and structs a
// C# array unless the callee may keep them (an address then), void*, functions and undefined classes addresses.
#pragma once
#include <Demo_Shape.hxx>
#include <Demo_Vec.hxx>
#include <NCollection_Array1.hxx>
#include <Standard_OStream.hxx>

// only declared: a pointer to it is opaque
class Demo_Cursor;

typedef double (*Demo_Function)(double theX, void* theData);

class Demo_Buffer
{
public:
  // kept by the object: addresses
  Demo_Buffer(double* theData, int theLength) : myData(theData), myLength(theLength) {}
  void SetData(double* theData) { myData = theData; }
  // read or written for the call: arrays
  double Sum(const double* theValues, int theCount) const { return 0.0; }
  void Fill(double theValues[3]) const {}
  bool Solve(const double theMatrix[3][3]) const { return true; }
  void Corners(Demo_Vec theCorners[4]) const {}
  void Load(const NCollection_Array1<Demo_Vec>* thePoles = nullptr) {}
  Demo_Shape* Shape() { return &myShape; }
  static Demo_Shape* Empty() { return nullptr; }
  const double* Data() const { return myData; }
  void* Address() const { return myData; }
  void Apply(Demo_Function theFunction, void* theData) {}
  void Walk(Demo_Cursor* theCursor) {}
  bool Next(const char*& theText) const { return false; }
  void Swap(Demo_Shape*& theShape) {}
  void Label(const char16_t* theText) {}
  void Report(Standard_OStream* theStream = nullptr) const {}

private:
  double*    myData;
  int        myLength;
  Demo_Shape myShape;
};
