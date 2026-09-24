// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BinDrivers;
using OCC.Core.TDataStd;
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
/// TCollection_ExtendedString.
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
    var part = document!.Main().FindChild(1, false);
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

  private static double NamedShapeVolume(TDF_Label label) =>
    Ocaf.Find(label, TNaming_NamedShape.GetID(), TNaming_NamedShape.DownCast) is { } named
      ? Shapes.Volume(named.Get())
      : double.NaN;
}
