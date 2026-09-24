// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Text;

// NUnit
using NUnit.Framework;

//
using OCC.Core;
using OCC.Core.Approx;
using OCC.Core.BRepMesh;
using OCC.Core.FSD;
using OCC.Core.Geom;
using OCC.Core.GeomAdaptor;
using OCC.Core.gp;
using OCC.Core.Graphic3d;
using OCC.Core.NCollection;
using OCC.Core.TCollection;
using OCC.Core.TDF;

// static usings
using static OCC.Core.BRepMesh.BRepMesh_DegreeOfFreedom;
using static OCC.Core.Graphic3d.Graphic3d_FrameStatsCounter;
using static OCC.Core.Storage.Storage_OpenMode;

namespace NetOcc.Tests;

/// <summary>
/// Numbers, characters, arrays, GUIDs and transients, mostly by reference: a byte's ref return,
/// char as its byte (by value too), C long as a C# long, a C array by reference as a C# array of
/// its length, Standard_GUID&amp; as ref Guid, a transient's T&amp; as its proxy.
/// </summary>
[TestFixture]
public class ReferenceKindTests
{
  private string _directory = null!;

  [SetUp]
  public void CreateDirectory() => _directory = NonAscii.CreateTempDirectory();

  [TearDown]
  public void DeleteDirectory() => Directory.Delete(_directory, true);

  [Test]
  public void ByteReference_WritesThrough()
  {
    // Arrange
    using var color = new Graphic3d_Vec3ub(1, 2, 3);

    // Act
    color.x() = 200;

    // Assert
    Assert.That(color.x(), Is.EqualTo(200));
  }

  [Test]
  public void CLong_IsALong()
  {
    // Arrange
    var file = new FSD_BinaryFile();
    file.Open(Path.Combine(_directory, "data.bin"), Storage_VSWrite);
    file.PutInteger(42);

    // Act
    var position = file.Tell();
    file.Close();

    // Assert
    Assert.That(position, Is.EqualTo(4));
  }

  [Test]
  public void CharReference_IsItsByte()
  {
    // Arrange (written, then read back)
    var path = Path.Combine(_directory, "data.bin");
    var writer = new FSD_BinaryFile();
    writer.Open(path, Storage_VSWrite);
    writer.PutCharacter((byte)'A');
    writer.Close();
    var reader = new FSD_BinaryFile();
    reader.Open(path, Storage_VSRead);
    byte value = 0;

    // Act
    reader.GetCharacter(ref value);
    reader.Close();

    // Assert
    Assert.That(value, Is.EqualTo((byte)'A'));
  }

  [Test]
  public void Character_IsItsByte()
  {
    // Arrange (the first byte of a non-ASCII text's UTF-8, which a C# char would carry through a
    // code page)
    using var text = new TCollection_HAsciiString(NonAscii.Text);
    var expected = Encoding.UTF8.GetBytes(NonAscii.Text)[0];

    // Act
    var first = text.Value(1);

    // Assert
    Assert.That(first, Is.EqualTo(expected));
  }

  [Test]
  public void ArrayReference_IsAnArrayOfItsLength()
  {
    // Arrange (BRepMesh_Triangle takes and gives its edges as int (&)[3], their orientations as
    // bool (&)[3])
    var triangle = new BRepMesh_Triangle([1, 2, 3], [true, false, true], BRepMesh_Free);
    var edges = new int[3];
    var orientations = new bool[3];

    // Act
    triangle.Edges(edges, orientations);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(edges, Is.EqualTo(new[] { 1, 2, 3 }));
      Assert.That(orientations, Is.EqualTo(new[] { true, false, true }));
    }
  }

  [Test]
  public void ArrayReference_ChecksTheLength()
  {
    // Arrange
    var triangle = new BRepMesh_Triangle();

    // Act
    Action edges = () => triangle.Edges(new int[2], new bool[3]);

    // Assert
    Assert.That(edges, Throws.TypeOf<ArgumentException>());
  }

  [Test]
  public void BoolArrayReference_ChecksTheLength()
  {
    // Arrange (bool arrays go through bytes; the length is checked first)
    var triangle = new BRepMesh_Triangle();

    // Act
    Action edges = () => triangle.Edges(new int[3], new bool[2]);

    // Assert
    Assert.That(edges, Throws.TypeOf<ArgumentException>());
  }

  [Test]
  public void SizeReferenceReturn_WritesThrough()
  {
    // Arrange (a ref UIntPtr into the object: size_t is four bytes on x86)
    var stats = new Graphic3d_FrameStatsDataTmp();

    // Act
    stats.ChangeCounterValue(Graphic3d_FrameStatsCounter_NbLayers) = new UIntPtr(7);

    // Assert
    Assert.That(stats.CounterValue(Graphic3d_FrameStatsCounter_NbLayers), Is.EqualTo(7UL));
  }

  [Test]
  [Platform("32-Bit-Process")]
  public void Size_ThatA32BitSizeCantHold_Throws()
  {
    // Arrange (as for a C long: an OcctException, not a value wrapped around)
    const ulong alignment = 1UL << 40;

    // Act
    Action allocator = () => new NCollection_AlignedAllocator(alignment);

    // Assert
    Assert.That(allocator,
                Throws.TypeOf<OcctException>()
                  .With.Property(nameof(OcctException.OcctType))
                  .EqualTo("Standard_OutOfRange"));
  }

  [Test]
  public void GuidReference_IsWrittenBack()
  {
    // Arrange
    var guid = Guid.NewGuid();
    var progId = "NetOcc.Test." + guid.ToString("N");
    TDF.AddLinkGUIDToProgID(guid, progId);
    var found = Guid.Empty;

    // Act
    var known = TDF.GUIDFromProgID(progId, ref found);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(known, Is.True);
      Assert.That(found, Is.EqualTo(guid));
    }
  }

  [Test]
  public void TransientReference_IsItsProxy()
  {
    // Arrange (a bounded curve: the function measures it whole)
    var adaptor = new GeomAdaptor_Curve(new Geom_Line(new gp_Ax1(gp.Origin(), gp.DX())), 0, 10);
    var function = new Approx_CurvlinFunc(adaptor, 1e-9);

    // Act
    var length = function.Length(adaptor, 0, 5);

    // Assert
    Assert.That(length, Is.EqualTo(5).Within(1e-9));
  }
}
