// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;

//
using OCC.Core.STEPCAFControl;
using OCC.Core.TDF;
using OCC.Core.TDocStd;
using OCC.Core.XCAFApp;
using OCC.Core.XCAFDoc;

// static usings
using static OCC.Core.IFSelect.IFSelect_ReturnStatus;

namespace NetOcc.Viewer;

/// <summary>
/// A STEP file read into an XCAF document: shapes with their names, colors and assembly structure.
/// </summary>
public sealed class StepDocument : IDisposable
{
  private readonly XCAFApp_Application _application;
  private TDocStd_Document? _document;

  private StepDocument(XCAFApp_Application application, TDocStd_Document document,
                       IReadOnlyList<TDF_Label> roots)
  {
    _application = application;
    _document = document;
    Roots = roots;
  }

  /// <summary>The shapes no other shape refers to: the parts and top assemblies.</summary>
  public IReadOnlyList<TDF_Label> Roots { get; }

  /// <summary>Reads a STEP file with its colors, names and layers.</summary>
  /// <exception cref="InvalidDataException">
  /// The file isn't STEP, or holds nothing OCCT can transfer.
  /// </exception>
  public static StepDocument Read(string path)
  {
    var application = XCAFApp_Application.GetApplication();
    TDocStd_Document? document = null;
    application.NewDocument("MDTV-XCAF", ref document);
    try
    {
      // the reader holds the whole STEP model: gone right after the transfer, not whenever the
      // finalizer runs
      using var reader = new STEPCAFControl_Reader();
      reader.SetColorMode(true);
      reader.SetNameMode(true);
      reader.SetLayerMode(true);
      if (reader.ReadFile(path) != IFSelect_RetDone || !reader.Transfer(document))
      {
        throw new InvalidDataException(
          $"{Path.GetFileName(path)} holds no STEP shapes OCCT can read.");
      }

      using var roots = new TDF_LabelSequence();
      XCAFDoc_DocumentTool.ShapeTool(document!.Main()).GetFreeShapes(roots);
      return new StepDocument(application, document, [.. roots]);
    }
    catch
    {
      // the application keeps every document it opened until it's closed
      application.Close(document);
      throw;
    }
  }

  /// <summary>Closes the document; its shapes must no longer be shown.</summary>
  public void Dispose()
  {
    if (_document is not null)
    {
      _application.Close(_document);
      _document = null;
    }
  }
}
