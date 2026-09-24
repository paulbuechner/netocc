// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// NUnit
using NUnit.Framework;

//
using NetOcc.Generator.Emit;

namespace NetOcc.Generator.Tests;

/// <summary>
/// The layout of the generated structs' members: the formatter's for these shapes.
/// </summary>
[TestFixture]
public class CsLayoutTests
{
  [Test]
  public void ExpressionMember_ThatFits_IsOneLine()
  {
    // Act
    var member = CsLayout.ExpressionMember("  public readonly double X", [],
                                           "gpModule.NetOcc_gp_Pnt_X", ["in this"]);

    // Assert
    Assert.That(
      member, Is.EqualTo("  public readonly double X() => gpModule.NetOcc_gp_Pnt_X(in this);"));
  }

  [Test]
  public void ExpressionMember_ThatDoesNot_FillsLinesAlignedAfterTheParenthesis()
  {
    // Arrange
    string[] parameters =
    [
      "ref double theA", "ref double theB", "ref double theC", "ref double theD", "ref double theE",
      "ref double theF"
    ];
    string[] arguments =
      ["in this", "ref theA", "ref theB", "ref theC", "ref theD", "ref theE", "ref theF"];

    // Act
    var member = CsLayout.ExpressionMember("  public readonly void Coefficients", parameters,
                                           "gpModule.NetOcc_gp_Parab2d_Coefficients", arguments);

    // Assert
    Assert.That(
      member,
      Is.EqualTo(
        "  public readonly void Coefficients(ref double theA, ref double theB, ref double theC,\n"
        + "                                    ref double theD, ref double theE, ref double theF) =>\n"
        + "    gpModule.NetOcc_gp_Parab2d_Coefficients(in this, ref theA, ref theB, ref theC, ref theD,\n"
        + "                                            ref theE, ref theF);"));
  }
}
