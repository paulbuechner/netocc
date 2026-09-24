// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: an alias of a package OCCT 8 dropped (like TColgp), which gets a module for its collections.
#pragma once
#include <Demo_Vec.hxx>
#include <NCollection_Array1.hxx>

typedef NCollection_Array1<Demo_Vec> TColDemo_Array1OfVec;
