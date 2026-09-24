// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// NUnit
using NUnit.Framework;

//
using OCC.Core.ShapeProcess;

namespace NetOcc.Tests;

/// <summary>
/// ShapeProcess's operations by name, through the companion accessor (extras/ShapeProcess.i).
/// </summary>
[TestFixture]
public class ShapeProcessTests
{
  [Test]
  public void OperationFlag_ReportsWhetherTheNameIsKnown()
  {
    // Arrange
    var known = false;
    var unknown = true;

    // Act
    var flag = ShapeProcess.ToOperationFlag("FixShape", ref known);
    ShapeProcess.ToOperationFlag("NoSuchOperation", ref unknown);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(known, Is.True);
      Assert.That(flag, Is.EqualTo(ShapeProcess_Operation.FixShape));
      Assert.That(unknown, Is.False);
    }
  }
}
