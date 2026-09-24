// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

namespace OCC.Core.gp;

/// <summary>
/// OCCT <c>gp_Ax2</c>: right-handed coordinate system. Managed accessors; the constructors run in
/// OCCT (X direction computed or validated) like the rest of gp_Ax2.g.cs.
/// </summary>
public partial struct gp_Ax2
{
  public readonly gp_Ax1 Axis() => axis;
  public readonly gp_Pnt Location() => axis.Location();
  public readonly gp_Dir Direction() => axis.Direction();
  public readonly gp_Dir XDirection() => vxdir;
  public readonly gp_Dir YDirection() => vydir;
  public void SetLocation(in gp_Pnt theP) => axis.SetLocation(theP);
}
