// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: an exception class, a proxy like other classes (a thrown one reaches C# as OcctException). A macro
// declares it, like DEFINE_STANDARD_EXCEPTION: the default has no tokens in the header, its value comes from the AST.
#pragma once
#include <Standard_Failure.hxx>

#define DEMO_DEFINE_EXCEPTION(C1, C2)                                                              \
  class C1 : public C2                                                                             \
  {                                                                                                \
  public:                                                                                          \
    C1(const char* theMessage = "") {}                                                             \
    const char* ExceptionType() const { return #C1; }                                              \
  };

DEMO_DEFINE_EXCEPTION(Demo_Error, Standard_Failure)
