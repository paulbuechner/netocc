// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BinDrivers;
using OCC.Core.gp;
using OCC.Core.Poly;
using OCC.Core.Standard;
using OCC.Core.TColStd;
using OCC.Core.TDataStd;
using OCC.Core.TDataXtd;
using OCC.Core.TDF;
using OCC.Core.TDocStd;
using OCC.Core.TNaming;
using OCC.Core.XmlDrivers;

// static usings
using static OCC.Core.PCDM.PCDM_ReaderStatus;
using static OCC.Core.PCDM.PCDM_StoreStatus;

namespace NetOcc.Tests;

/// <summary>
/// OCAF persistence (BinOcaf, XmlOcaf) through a non-ASCII path, passed as a UTF-16
/// TCollection_ExtendedString, and through streams: seekable, forward only, encrypting.
/// </summary>
[TestFixture]
public class OcafPersistenceTests
{
  // Latin-1, a CJK character and a surrogate pair: TCollection_ExtendedString is UTF-16
  private const string PartName = NonAscii.WideText;

  private TDocStd_Application _application = null!;
  private string _directory = null!;

  [SetUp]
  public void CreateApplication()
  {
    _application = new TDocStd_Application();
    BinDrivers.DefineFormat(_application);
    XmlDrivers.DefineFormat(_application);
    _directory = NonAscii.CreateTempDirectory();
  }

  [TearDown]
  public void CloseDocuments()
  {
    while (_application.NbDocuments() > 0)
    {
      using var document = _application.GetDocument(1);
      _application.Close(document);
    }

    _application.Dispose();
    Directory.Delete(_directory, true);
  }

  [TestCase("BinOcaf", "cbf")]
  [TestCase("XmlOcaf", "xml")]
  public void SaveAs_CreatesTheFileAtTheUtf16Path(string format, string extension)
  {
    // Arrange
    var path = Path.Combine(_directory, $"document {NonAscii.Text}.{extension}");
    var document = NewSampleDocument(format);

    // Act
    var status = _application.SaveAs(document, path);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(status, Is.EqualTo(PCDM_SS_OK));
      Assert.That(File.Exists(path), Is.True, "document not found at the non-ASCII path");
      Assert.That(document.IsSaved(), Is.True);
    }
  }

  [TestCase("BinOcaf", "cbf")]
  [TestCase("XmlOcaf", "xml")]
  public void Open_RestoresTheSavedAttributes(string format, string extension)
  {
    // Arrange
    var path = Path.Combine(_directory, $"document {NonAscii.Text}.{extension}");
    SaveAndClose(NewSampleDocument(format), path);
    TDocStd_Document? document = null;

    // Act
    var status = _application.Open(path, ref document);

    // Assert
    Assert.That(status, Is.EqualTo(PCDM_RS_OK));
    AssertSample(document!, format);
  }

  [TestCase("BinOcaf")]
  [TestCase("XmlOcaf")]
  public void Open_FromAStream_RestoresWhatSaveAsWroteToOne(string format)
  {
    // Arrange
    var bytes = SaveToBytes(NewSampleDocument(format), stream => stream);
    using var stream = new MemoryStream(bytes);
    TDocStd_Document? document = null;

    // Act
    var status = _application.Open(stream, ref document);

    // Assert
    Assert.That(status, Is.EqualTo(PCDM_RS_OK));
    AssertSample(document!, format);
  }

  [TestCase("BinOcaf")]
  [TestCase("XmlOcaf")]
  public void Open_FromAForwardOnlyStream_RestoresWhatSaveAsWroteToOne(string format)
  {
    // Arrange
    var bytes = SaveToBytes(NewSampleDocument(format),
                            stream => new NonSeekableStream(stream, canRead: false,
                                                            canWrite: true));
    using var stream = new NonSeekableStream(new MemoryStream(bytes));
    TDocStd_Document? document = null;

    // Act
    var status = _application.Open(stream, ref document);

    // Assert
    Assert.That(status, Is.EqualTo(PCDM_RS_OK));
    AssertSample(document!, format);
  }

  [TestCase("BinOcaf")]
  [TestCase("XmlOcaf")]
  public void Open_ThroughACryptoStream_RestoresWhatSaveAsEncrypted(string format)
  {
    // Arrange
    using var aes = Aes.Create();
    aes.Key = new byte[32];
    aes.IV = new byte[16];
    var bytes = SaveToBytes(NewSampleDocument(format),
                            stream => new CryptoStream(stream, aes.CreateEncryptor(),
                                                       CryptoStreamMode.Write));
    using var stream = new CryptoStream(new MemoryStream(bytes), aes.CreateDecryptor(),
                                        CryptoStreamMode.Read);
    TDocStd_Document? document = null;

    // Act
    var status = _application.Open(stream, ref document);

    // Assert
    Assert.That(status, Is.EqualTo(PCDM_RS_OK));
    AssertSample(document!, format);
  }

  [TestCase("BinOcaf")]
  [TestCase("XmlOcaf")]
  public void Open_FromAStream_RestoresReferencesArraysAndATriangulation(string format)
  {
    // Arrange
    TDocStd_Document? saved = null;
    _application.NewDocument(format, ref saved);
    var main = saved!.Main();
    TDF_Reference.Set(main.FindChild(3), main.FindChild(2));
    var references = TDataStd_ReferenceList.Set(main.FindChild(4));
    references.Append(main.FindChild(2));
    references.Append(main.FindChild(1));
    TDataStd_RealArray.Set(main.FindChild(5), 1, 3)
      .ChangeArray(new TColStd_HArray1OfReal(new TColStd_Array1OfReal([0.5, 1.5, 2.5])));
    TDataStd_IntegerArray.Set(main.FindChild(6), 1, 3)
      .ChangeArray(new TColStd_HArray1OfInteger(new TColStd_Array1OfInteger([3, 1, 2])));
    TDataXtd_Triangulation.Set(main.FindChild(7), Triangle());
    var bytes = SaveToBytes(saved, stream => stream);
    using var stream = new MemoryStream(bytes);
    TDocStd_Document? document = null;

    // Act
    var status = _application.Open(stream, ref document);

    // Assert
    Assert.That(status, Is.EqualTo(PCDM_RS_OK));
    var opened = document!.Main();
    var triangulation = Find(opened, 7, TDataXtd_Triangulation.GetID(),
                             TDataXtd_Triangulation.DownCast)
      ?.Get();
    using (Assert.EnterMultipleScope())
    {
      Assert.That(
        Find(opened, 3, TDF_Reference.GetID(), TDF_Reference.DownCast) is { } reference
          ? Ocaf.Entry(reference.Get())
          : null, Is.EqualTo("0:1:2"));
      Assert.That(
        Find(opened, 4, TDataStd_ReferenceList.GetID(), TDataStd_ReferenceList.DownCast)
          ?.List()
          .Select(Ocaf.Entry), Is.EqualTo(new[] { "0:1:2", "0:1:1" }));
      Assert.That(
        Find(opened, 5, TDataStd_RealArray.GetID(), TDataStd_RealArray.DownCast)?.Array().ToArray(),
        Is.EqualTo(new[] { 0.5, 1.5, 2.5 }));
      Assert.That(
        Find(opened, 6, TDataStd_IntegerArray.GetID(), TDataStd_IntegerArray.DownCast)
          ?.Array()
          .ToArray(), Is.EqualTo(new[] { 3, 1, 2 }));
      Assert.That(triangulation?.NbTriangles(), Is.EqualTo(1));
      Assert.That(triangulation?.Node(3).Y(), Is.EqualTo(1.0));
    }
  }

  [Test]
  public void Open_ReportsAMissingFile()
  {
    // Arrange
    var path = Path.Combine(_directory, "fehlt.cbf");
    TDocStd_Document? document = null;

    // Act
    var status = _application.Open(path, ref document);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(status, Is.EqualTo(PCDM_RS_UnknownDocument));
      Assert.That(document, Is.Null);
    }
  }

  [Test]
  public void SaveAs_ExplainsAFailedWrite()
  {
    // Arrange
    var document = NewSampleDocument("BinOcaf");
    var path = Path.Combine(Path.Combine(_directory, "fehlt"), "Dokument.cbf");
    var message = "";

    // Act
    var status = _application.SaveAs(document, path, ref message);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(status, Is.EqualTo(PCDM_SS_Failure));
      Assert.That(message, Is.Not.Empty);
    }
  }

  private TDocStd_Document NewSampleDocument(string format)
  {
    TDocStd_Document? document = null;
    _application.NewDocument(format, ref document);
    var part = document!.Main().FindChild(1);
    TDataStd_Name.Set(part, PartName);
    TDataStd_Integer.Set(part, 42);
    TDataStd_Real.Set(part, 0.125);
    new TNaming_Builder(part).Generated(Shapes.Fused());
    return document;
  }

  private void SaveAndClose(TDocStd_Document document, string path)
  {
    var status = _application.SaveAs(document, path);
    _application.Close(document);
    if (status != PCDM_SS_OK)
    {
      throw new InvalidOperationException($"SaveAs failed: {status}");
    }
  }

  // what SaveAs wrote through the stream wrap makes of a memory stream, once that is disposed
  private byte[] SaveToBytes(TDocStd_Document document, Func<Stream, Stream> wrap)
  {
    using var memory = new MemoryStream();
    using (var stream = wrap(memory))
    {
      var status = _application.SaveAs(document, stream);
      if (status != PCDM_SS_OK)
      {
        throw new InvalidOperationException($"SaveAs failed: {status}");
      }
    }

    return memory.ToArray();
  }

  private static void AssertSample(TDocStd_Document document, string format)
  {
    var part = document.Main().FindChild(1, false);
    using (Assert.EnterMultipleScope())
    {
      Assert.That(document.StorageFormat(), Is.EqualTo(format));
      Assert.That(Ocaf.Name(part), Is.EqualTo(PartName));
      Assert.That(Ocaf.Find(part, TDataStd_Integer.GetID(), TDataStd_Integer.DownCast)?.Get(),
                  Is.EqualTo(42));
      Assert.That(Ocaf.Find(part, TDataStd_Real.GetID(), TDataStd_Real.DownCast)?.Get(),
                  Is.EqualTo(0.125));
      Assert.That(NamedShapeVolume(part), Is.EqualTo(Shapes.FusedVolume).Within(1e-2));
    }
  }

  private static T? Find<T>(TDF_Label main, int tag, Guid id, Func<Standard_Transient, T> downCast)
    where T : TDF_Attribute =>
    Ocaf.Find(main.FindChild(tag, false), id, downCast);

  // one triangle (0 0 0) (1 0 0) (0 1 0)
  private static Poly_Triangulation Triangle()
  {
    var triangulation = new Poly_Triangulation(3, 1, false);
    triangulation.SetNode(1, new gp_Pnt(0, 0, 0));
    triangulation.SetNode(2, new gp_Pnt(1, 0, 0));
    triangulation.SetNode(3, new gp_Pnt(0, 1, 0));
    triangulation.SetTriangle(1, new Poly_Triangle(1, 2, 3));
    return triangulation;
  }

  private static double NamedShapeVolume(TDF_Label label) =>
    Ocaf.Find(label, TNaming_NamedShape.GetID(), TNaming_NamedShape.DownCast) is { } named
      ? Shapes.Volume(named.Get())
      : double.NaN;
}
