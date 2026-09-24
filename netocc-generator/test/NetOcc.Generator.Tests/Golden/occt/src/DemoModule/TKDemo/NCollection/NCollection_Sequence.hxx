// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: a collection template netocc-core instantiates with %occt_sequence.
#pragma once

template <class TheItemType>
class NCollection_Sequence
{
public:
  NCollection_Sequence() {}
  int Length() const { return 0; }
};
