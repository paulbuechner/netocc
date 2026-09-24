// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

namespace OCC.Core.Demo;

// Golden fixture: the hand-written partial of the Demo_Vec struct; the generator leaves X() out.
public partial struct Demo_Vec
{
    public readonly double X() => myX;
}
