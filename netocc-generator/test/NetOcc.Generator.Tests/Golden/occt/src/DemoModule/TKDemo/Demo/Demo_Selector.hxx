// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: a polymorphic class the libraries don't export, like ShapeAnalysis_BoxBndTreeSelector. Creating it in
// the shim would emit its vtable, which names Reject: not exported either, so no constructors.
#pragma once

class Demo_Selector
{
public:
  Demo_Selector() {}
  virtual ~Demo_Selector() {}
  virtual bool Reject(int theValue) const;
  int Count() const { return 0; }
};
