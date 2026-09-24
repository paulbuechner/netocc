// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: a deprecated class (Standard_DEPRECATED in OCCT, like Message_ProgressSentry): an [Obsolete] proxy.
#pragma once

class [[deprecated("use Demo_Shape")]] Demo_Old
{
public:
  Demo_Old() {}
  int Value() const { return 0; }
};
