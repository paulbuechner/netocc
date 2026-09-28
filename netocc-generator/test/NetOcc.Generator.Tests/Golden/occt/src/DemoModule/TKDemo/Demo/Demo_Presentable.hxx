// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: a director class (config directors), which C# subclasses. Its virtual members, inherited and protected
// ones too, are declared virtual (pure ones = 0) and its protected constructor is wrapped; a member C# can't override
// (neither parameters nor a result) is logged; its base keeps its declarations; a concrete class deriving from it is
// notabstract.
#pragma once
#include <Demo_Vec.hxx>
#include <Standard_Transient.hxx>

class Demo_Drawable : public Standard_Transient
{
public:
  virtual void Select(int theMode) = 0;
  virtual void Clear() {}
  void         Redraw() { Draw(0); }

protected:
  Demo_Drawable() {}
  virtual void Draw(int theMode) = 0;
};

class Demo_Presentable : public Demo_Drawable
{
public:
  virtual bool     Accepts(int theMode) const { return theMode == 0; }
  virtual Demo_Vec Center() const { return Demo_Vec(); }

protected:
  Demo_Presentable() {}
};

class Demo_Marker : public Demo_Presentable
{
public:
  Demo_Marker() {}
  void Select(int) override {}

protected:
  void Draw(int) override {}
};
