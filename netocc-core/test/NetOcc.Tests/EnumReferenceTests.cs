// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// NUnit
using NUnit.Framework;

//
using OCC.Core.TopAbs;

// static usings
using static OCC.Core.TopAbs.TopAbs_ShapeEnum;

namespace NetOcc.Tests;

/// <summary>A non-const C++ enum&amp; is a C# ref parameter: read and written, never out.</summary>
[TestFixture]
public class EnumReferenceTests
{
  [Test]
  public void ShapeTypeFromString_WritesTheEnum()
  {
    // Arrange
    var type = TopAbs_SHAPE;

    // Act
    var found = TopAbs.ShapeTypeFromString("FACE", ref type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(found, Is.True);
      Assert.That(type, Is.EqualTo(TopAbs_FACE));
    }
  }

  [Test]
  public void ShapeTypeFromString_KeepsTheValueOnFailure()
  {
    // Arrange
    var type = TopAbs_EDGE;

    // Act
    var found = TopAbs.ShapeTypeFromString("no such type", ref type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(found, Is.False);
      Assert.That(type, Is.EqualTo(TopAbs_EDGE), "the input survives a call that doesn't write");
    }
  }
}
