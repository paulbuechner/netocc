// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: a collection template netocc-core instantiates with %occt_array1.
#pragma once

template <class TheItemType>
class NCollection_Array1
{
public:
  NCollection_Array1() {}
  int Length() const { return 0; }
};
