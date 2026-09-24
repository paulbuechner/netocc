// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: the default hasher of the maps, a template argument C# doesn't see.
#pragma once

template <class TheKeyType>
struct NCollection_DefaultHasher
{
  int operator()(const TheKeyType&) const { return 0; }
};
