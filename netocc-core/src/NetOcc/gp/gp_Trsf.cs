// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

namespace OCC.Core.gp;

/// <summary>
/// OCCT <c>gp_Trsf</c>: non-degenerate rigid/scale transformation (<c>scale</c>, <c>shape</c>, 4
/// bytes padding, <c>matrix</c>, <c>loc</c>). Managed field reads; setters and composition run in
/// OCCT (gp_Trsf.g.cs). <c>default(gp_Trsf)</c> has scale 0 and is invalid; <c>new gp_Trsf()</c> is
/// the identity.
/// </summary>
public partial struct gp_Trsf
{
  public readonly double ScaleFactor() => scale;
  public readonly gp_TrsfForm Form() => shape;
  public readonly gp_XYZ TranslationPart() => loc;
  public readonly gp_Mat HVectorialPart() => matrix;
}
