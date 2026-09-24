// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: a protected base re-exposed with a using-declaration (like BRepAlgoAPI_Algo), and collections.
#pragma once
#include <Demo_Shape.hxx>
#include <Demo_Vec.hxx>
#include <NCollection_Array1.hxx>
#include <NCollection_DataMap.hxx>
#include <NCollection_HSequence.hxx>
#include <NCollection_Sequence.hxx>

class Demo_Options
{
public:
  bool HasErrors() const { return false; }
  void SetFuzzy(double theFuzz) { myFuzz = theFuzz; }

protected:
  Demo_Options() : myFuzz(0.0) {}

private:
  double myFuzz;
};

class Demo_Algo : protected Demo_Options
{
public:
  Demo_Algo() {}
  using Demo_Options::HasErrors;
  void Perform(const Demo_Shape& theShape, double theTolerance = 1.0e-7) {}
  void Perform(const NCollection_Sequence<Demo_Shape>& theShapes) {}
  const NCollection_Sequence<Demo_Shape>& Results() const { return myResults; }
  opencascade::handle<NCollection_HSequence<Demo_Shape>> History() const { return {}; }
  void SetPoles(const NCollection_Array1<Demo_Vec>& thePoles) {}
  // a map of collections: the sequence is instantiated too
  const NCollection_DataMap<int, NCollection_Sequence<Demo_Shape>>& Groups() const { return myGroups; }
  // no alias names NCollection_Sequence<double>: NCollection_Sequence_double
  void SetWeights(const NCollection_Sequence<double>& theWeights) {}

private:
  NCollection_Sequence<Demo_Shape> myResults;
  NCollection_DataMap<int, NCollection_Sequence<Demo_Shape>> myGroups;
};
