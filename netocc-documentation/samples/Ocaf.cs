// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BinDrivers;
using OCC.Core.BRepPrimAPI;
using OCC.Core.PCDM;
using OCC.Core.TDataStd;
using OCC.Core.TDF;
using OCC.Core.TDocStd;
using OCC.Core.TNaming;
using OCC.Core.TopoDS;

namespace NetOcc.Samples;

/// <summary>OCAF: documents, labels, attributes, undo, and files.</summary>
[TestFixture]
public class Ocaf : Files
{
  [Test]
  public void LabelsAndAttributes()
  {
    // Act
    #region labels
    // an application knows the formats; BinDrivers adds "BinOcaf", OCCT's binary format
    var application = new TDocStd_Application();
    BinDrivers.DefineFormat(application);

    TDocStd_Document? document = null;
    application.NewDocument("BinOcaf", ref document);
    var main = document!.Main(); // the label 0:1

    // labels form a tree of tags: FindChild(tag, true) makes the child if it's missing
    var width = main.FindChild(1, true); // 0:1:1
    TDataStd_Name.Set(width, "width");
    TDataStd_Real.Set(width, 40.0);

    var part = main.FindChild(2, true); // 0:1:2
    TDataStd_Name.Set(part, "part");
    new TNaming_Builder(part).Generated(new BRepPrimAPI_MakeBox(40, 20, 10).Shape());

    // attributes are found by their class's GUID
    TDF_Attribute? attribute = null;
    var value = width.FindAttribute(TDataStd_Real.GetID(), ref attribute)
      ? TDataStd_Real.DownCast(attribute)!.Get()
      : 0;

    TopoDS_Shape? shape = null;
    if (part.FindAttribute(TNaming_NamedShape.GetID(), ref attribute))
    {
      shape = TNaming_NamedShape.DownCast(attribute)!.Get();
    }

    // entries name labels as text
    var entry = "";
    TDF_Tool.Entry(part, ref entry); // "0:1:2"
    #endregion

    #region children
    var names = new List<string>();
    for (var child = new TDF_ChildIterator(main); child.More(); child.Next())
    {
      if (child.Value().FindAttribute(TDataStd_Name.GetID(), ref attribute))
      {
        names.Add(TDataStd_Name.DownCast(attribute)!.Get());
      }
    }
    #endregion

    application.Close(document);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(value, Is.EqualTo(40.0));
      Assert.That(shape?.IsNull(), Is.False);
      Assert.That(entry, Is.EqualTo("0:1:2"));
      Assert.That(names, Is.EqualTo(new[] { "width", "part" }));
    }
  }

  [Test]
  public void Undo()
  {
    // Arrange
    var application = new TDocStd_Application();
    BinDrivers.DefineFormat(application);
    TDocStd_Document? document = null;
    application.NewDocument("BinOcaf", ref document);
    var width = document!.Main().FindChild(1, true);
    TDataStd_Real.Set(width, 40.0);

    // Act
    #region undo
    document.SetUndoLimit(10); // without a limit, commands record nothing

    document.OpenCommand();
    TDataStd_Real.Set(width, 55.0); // changes the attribute that's there
    document.CommitCommand();

    var before = document.GetAvailableUndos(); // 1
    document.Undo();                           // width is 40 again
    document.Redo();                           // and 55
    document.Undo();
    #endregion

    TDF_Attribute? attribute = null;
    width.FindAttribute(TDataStd_Real.GetID(), ref attribute);
    var value = TDataStd_Real.DownCast(attribute)!.Get();
    application.Close(document);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(before, Is.EqualTo(1));
      Assert.That(value, Is.EqualTo(40.0));
    }
  }

  [Test]
  public void SaveAndOpen()
  {
    // Arrange
    var application = new TDocStd_Application();
    BinDrivers.DefineFormat(application);
    TDocStd_Document? document = null;
    application.NewDocument("BinOcaf", ref document);
    TDataStd_Real.Set(document!.Main().FindChild(1, true), 40.0);

    // Act
    #region save-open
    var saved = application.SaveAs(document, "model.cbf"); // PCDM_SS_OK
    application.Close(document); // Close, not just Dispose: OCCT keeps documents open

    TDocStd_Document? opened = null;
    var status = application.Open("model.cbf", ref opened); // PCDM_RS_OK
    #endregion

    TDF_Attribute? attribute = null;
    opened!.Main().FindChild(1).FindAttribute(TDataStd_Real.GetID(), ref attribute);
    var value = TDataStd_Real.DownCast(attribute)!.Get();
    application.Close(opened);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(saved, Is.EqualTo(PCDM_StoreStatus.PCDM_SS_OK));
      Assert.That(status, Is.EqualTo(PCDM_ReaderStatus.PCDM_RS_OK));
      Assert.That(value, Is.EqualTo(40.0));
    }
  }
}
