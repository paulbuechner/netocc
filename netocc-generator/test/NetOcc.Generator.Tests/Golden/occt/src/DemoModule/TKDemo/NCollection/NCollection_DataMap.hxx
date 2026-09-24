// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: a map netocc-core instantiates with %occt_datamap.
#pragma once
#include <NCollection_DefaultHasher.hxx>

template <class TheKeyType, class TheItemType, class Hasher = NCollection_DefaultHasher<TheKeyType>>
class NCollection_DataMap
{
public:
  NCollection_DataMap() {}
  int Extent() const { return 0; }
};
