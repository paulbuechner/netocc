// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: OCCT 8 style namespace functions, which become a static C# class, namespace types (flat names), and
// an excluded namespace. A deprecated function is [Obsolete].
#pragma once
#include <Demo_Shape.hxx>
#include <Demo_Vec.hxx>
#include <Standard_UUID.hxx>

namespace Demo
{
enum class Status
{
  Done,
  Failed
};

struct Result
{
  Status        Outcome;
  double        Distance;
  Demo_Vec      Nearest;
  Standard_UUID Id;
};

namespace detail
{
struct Token
{
  int Kind;
};

inline int Count() { return 0; }
} // namespace detail

inline Result Nearest(const Demo_Vec& thePoint) { return Result{Status::Done, 0.0, thePoint, {}}; }
inline double Distance(const Demo_Vec& theA, const Demo_Vec& theB) { return theA.X() - theB.X(); }
[[deprecated("use Distance")]] inline double Gap(const Demo_Vec& theA, const Demo_Vec& theB) { return Distance(theA, theB); }
inline const Demo_Shape& Same(const Demo_Shape& theShape) { return theShape; }
inline Demo_Shape& Same(Demo_Shape& theShape) { return theShape; }
} // namespace Demo
