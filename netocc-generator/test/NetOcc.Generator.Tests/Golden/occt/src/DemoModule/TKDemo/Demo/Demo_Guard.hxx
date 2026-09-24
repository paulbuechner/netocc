// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: class template instances no alias names are classes of their own, named after their arguments. The
// headers never instantiate Demo_Guard<Demo_Vec> (a reference needs no definition), so the parse does.
#pragma once
#include <Demo_Shape.hxx>
#include <Demo_Vec.hxx>

template <typename T>
class Demo_Guard
{
public:
  explicit Demo_Guard(T* theObject) : myObject(theObject) {}
  bool IsNull() const { return myObject == nullptr; }

private:
  T* myObject;
};

class Demo_Editor
{
public:
  Demo_Editor() {}
  Demo_Guard<Demo_Shape> Edit(Demo_Shape& theShape) { return Demo_Guard<Demo_Shape>(&theShape); }
  static bool Check(const Demo_Guard<Demo_Vec>& theGuard) { return true; }
};
