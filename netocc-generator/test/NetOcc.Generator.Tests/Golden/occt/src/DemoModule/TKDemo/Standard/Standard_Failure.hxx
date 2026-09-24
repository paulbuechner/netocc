// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: the root exception class, a proxy without a base (std::exception isn't wrapped).
#pragma once

class Standard_Failure
{
public:
  Standard_Failure() {}
  const char* what() const { return "failure"; }
};
