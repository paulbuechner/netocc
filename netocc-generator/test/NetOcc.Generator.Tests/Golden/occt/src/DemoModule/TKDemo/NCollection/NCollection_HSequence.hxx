// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: a handle-managed collection, %occt_hsequence.
#pragma once
#include <NCollection_Sequence.hxx>
#include <Standard_Transient.hxx>

template <class TheItemType>
class NCollection_HSequence : public NCollection_Sequence<TheItemType>, public Standard_Transient
{
public:
  NCollection_HSequence() {}
};
