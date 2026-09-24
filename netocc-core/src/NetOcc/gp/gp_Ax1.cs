// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

namespace OCC.Core.gp;

/// <summary>
/// OCCT <c>gp_Ax1</c>: location + direction. Managed accessors; the layout and the native members
/// are in gp_Ax1.g.cs.
/// </summary>
public partial struct gp_Ax1
{
  public gp_Ax1(in gp_Pnt theP, in gp_Dir theV)
  {
    loc = theP;
    vdir = theV;
  }

  public readonly gp_Pnt Location() => loc;
  public readonly gp_Dir Direction() => vdir;
  public void SetLocation(in gp_Pnt theP) => loc = theP;
  public void SetDirection(in gp_Dir theV) => vdir = theV;
  public readonly gp_Ax1 Reversed() => new(loc, vdir.Reversed());
}
