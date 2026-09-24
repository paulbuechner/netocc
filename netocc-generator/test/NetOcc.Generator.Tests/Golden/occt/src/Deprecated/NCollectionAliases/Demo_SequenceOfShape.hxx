// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: OCCT 8's deprecated aliases name the collections, in the package they're named after. Only the
// ones signatures use are instantiated.
#pragma once
#include <Demo_Kind.hxx>
#include <Demo_Shape.hxx>
#include <NCollection_DataMap.hxx>
#include <NCollection_HSequence.hxx>
#include <NCollection_Sequence.hxx>

typedef NCollection_Sequence<Demo_Shape>  Demo_SequenceOfShape;
typedef NCollection_HSequence<Demo_Shape> Demo_HSequenceOfShape;
typedef NCollection_Sequence<Demo_Kind>   Demo_SequenceOfKind;
typedef NCollection_DataMap<int, NCollection_Sequence<Demo_Shape>> Demo_DataMapOfIntegerSequenceOfShape;
