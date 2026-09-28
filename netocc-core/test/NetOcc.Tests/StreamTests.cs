// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Text;

// NUnit
using NUnit.Framework;

//
using OCC.Core;
using OCC.Core.BinTools;
using OCC.Core.BRep;
using OCC.Core.BRepMesh;
using OCC.Core.BRepPrimAPI;
using OCC.Core.BRepTools;
using OCC.Core.gp;
using OCC.Core.TopoDS;

namespace NetOcc.Tests;

/// <summary>
/// C++ streams are C# streams, lent for the call: OCCT writes into and reads from memory.
/// </summary>
[TestFixture]
public class StreamTests
{
  // a finely meshed sphere: BinTools writes its triangulation too, several copy chunks
  private static TopoDS_Shape MeshedSphere()
  {
    var sphere = new BRepPrimAPI_MakeSphere(10).Shape();
    _ = new BRepMesh_IncrementalMesh(sphere, 0.01);
    return sphere;
  }

  private static byte[] BinaryOf(TopoDS_Shape shape)
  {
    using var stream = new MemoryStream();
    BinTools.Write(shape, stream);
    return stream.ToArray();
  }

  [Test]
  public void BRepTools_RoundTripsAShape()
  {
    // Arrange
    var box = new BRepPrimAPI_MakeBox(1, 2, 3).Shape();
    using var stream = new MemoryStream();

    // Act
    BRepTools.Write(box, stream);
    stream.Position = 0;
    var read = new TopoDS_Shape();
    BRepTools.Read(read, stream, new BRep_Builder());

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(stream.Length, Is.GreaterThan(0));
      Assert.That(Shapes.Volume(read), Is.EqualTo(6.0).Within(1e-9));
    }
  }

  [Test]
  public void BinTools_RoundTripsAShape()
  {
    // Arrange (a binary format)
    var cylinder = new BRepPrimAPI_MakeCylinder(1, 2).Shape();
    using var stream = new MemoryStream();

    // Act
    BinTools.Write(cylinder, stream);
    stream.Position = 0;
    var read = new TopoDS_Shape();
    BinTools.Read(read, stream);

    // Assert
    Assert.That(Shapes.Volume(read), Is.EqualTo(Shapes.Volume(cylinder)).Within(1e-9));
  }

  [Test]
  public void Read_LeavesTheStreamWhereOcctStopped()
  {
    // Arrange (two shapes, one after the other)
    using var stream = new MemoryStream();
    BRepTools.Write(new BRepPrimAPI_MakeBox(1, 1, 1).Shape(), stream);
    BRepTools.Write(new BRepPrimAPI_MakeBox(1, 1, 2).Shape(), stream);
    stream.Position = 0;
    var first = new TopoDS_Shape();
    var second = new TopoDS_Shape();

    // Act
    BRepTools.Read(first, stream, new BRep_Builder());
    BRepTools.Read(second, stream, new BRep_Builder());

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(Shapes.Volume(first), Is.EqualTo(1.0).Within(1e-9));
      Assert.That(Shapes.Volume(second), Is.EqualTo(2.0).Within(1e-9),
                  "the second read starts after the first shape");
    }
  }

  [Test]
  public void Read_StartsWhereTheStreamStands()
  {
    // Arrange (a 1x1x1 box first: read from the start, the stream would give that one)
    using var stream = new MemoryStream();
    BRepTools.Write(new BRepPrimAPI_MakeBox(1, 1, 1).Shape(), stream);
    var start = stream.Position;
    BRepTools.Write(new BRepPrimAPI_MakeBox(1, 1, 2).Shape(), stream);
    stream.Position = start;
    var read = new TopoDS_Shape();

    // Act
    BRepTools.Read(read, stream, new BRep_Builder());

    // Assert
    Assert.That(Shapes.Volume(read), Is.EqualTo(2.0).Within(1e-9));
  }

  [Test]
  public void Read_FromANonSeekableStream_ReadsWhatASeekableOneDoes()
  {
    // Arrange (no Length to size one buffer: the stream is copied chunk by chunk; OCCT's reader
    // turns a few 0.0 into -0.0, so the shapes read compare with each other, not with the original)
    var bytes = BinaryOf(MeshedSphere());
    using var seekable = new MemoryStream(bytes);
    var fromSeekable = new TopoDS_Shape();
    BinTools.Read(fromSeekable, seekable);
    using var stream = new NonSeekableStream(new MemoryStream(bytes));
    var read = new TopoDS_Shape();

    // Act
    BinTools.Read(read, stream);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(bytes, Has.Length.GreaterThan(2 * NativeStreams.ChunkSize));
      Assert.That(BinaryOf(read), Is.EqualTo(BinaryOf(fromSeekable)),
                  "written again, the same bytes");
    }
  }

  [Test]
  public void Read_FromAnUnreadableStream_Throws()
  {
    // Arrange
    var bytes = BinaryOf(new BRepPrimAPI_MakeBox(1, 2, 3).Shape());
    using var stream = new NonSeekableStream(new MemoryStream(bytes), canRead: false);
    var shape = new TopoDS_Shape();

    // Act
    Action read = () => BinTools.Read(shape, stream);

    // Assert
    Assert.That(read, Throws.TypeOf<ArgumentException>());
  }

  [Test]
  public void DumpJson_WritesText()
  {
    // Arrange
    var point = new gp_Pnt(1.5, 2, 3);
    using var stream = new MemoryStream();

    // Act
    point.DumpJson(stream);
    var text = Encoding.UTF8.GetString(stream.ToArray());

    // Assert
    Assert.That(text, Does.Contain("gp_Pnt").And.Contain("1.5"));
  }

  [Test]
  public void Write_OfSeveralChunks_MatchesTheFileOverload()
  {
    // Arrange (the file overload writes through OCCT's own binary file stream)
    var sphere = MeshedSphere();
    var directory = NonAscii.CreateTempDirectory();
    try
    {
      var file = Path.Combine(directory, "sphere.bin");
      BinTools.Write(sphere, file);
      using var stream = new MemoryStream();

      // Act
      BinTools.Write(sphere, stream);

      // Assert
      using (Assert.EnterMultipleScope())
      {
        Assert.That(stream.Length, Is.GreaterThan(2 * NativeStreams.ChunkSize));
        Assert.That(stream.ToArray(), Is.EqualTo(File.ReadAllBytes(file)));
      }
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Test]
  public void Write_ToAReadOnlyStream_Throws()
  {
    // Arrange
    var box = new BRepPrimAPI_MakeBox(1, 2, 3).Shape();
    using var stream = new MemoryStream(new byte[16], writable: false);

    // Act
    Action write = () => BRepTools.Write(box, stream);

    // Assert
    Assert.That(write, Throws.TypeOf<ArgumentException>());
  }
}
