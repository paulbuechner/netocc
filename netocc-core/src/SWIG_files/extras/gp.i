// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * gp: companion of the generated gp.i, included before its classes.
 * The value types are generated C# structs (src/NetOcc/gp/*.g.cs) with hand-written managed math (*.cs). This keeps
 * OCCT's own Distance for the tests, which compare it with the managed one.
 */

%inline %{
inline double NetOcc_gp_Pnt_NativeDistance(const gp_Pnt& theSelf, const gp_Pnt& theOther) { return theSelf.Distance(theOther); }
%}
