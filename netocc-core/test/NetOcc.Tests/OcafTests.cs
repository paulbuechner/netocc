// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Runtime.CompilerServices;

// NUnit
using NUnit.Framework;

//
using OCC.Core;
using OCC.Core.TColStd;
using OCC.Core.TDataStd;
using OCC.Core.TDF;
using OCC.Core.TDocStd;
using OCC.Core.TNaming;

// static usings
using static OCC.Core.TNaming.TNaming_Evolution;

namespace NetOcc.Tests;

/// <summary>
/// OCAF in memory: labels, attributes by GUID, UTF-8/UTF-16 strings, transactions, shapes,
/// lifetime.
/// </summary>
[TestFixture]
public class OcafTests
{
  // every byte distinct, so a swapped or reordered GUID field shows
  private static readonly Guid UserId = new("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");

  private TDocStd_Application _application = null!;
  private TDocStd_Document _document = null!;
  private TDF_Label _main = null!;

  [SetUp]
  public void CreateDocument()
  {
    _application = new TDocStd_Application();
    TDocStd_Document? document = null;
    _application.NewDocument("BinOcaf", ref document);
    _document = document!;
    _main = _document.Main();
  }

  [TearDown]
  public void CloseDocuments()
  {
    _main.Dispose();
    while (_application.NbDocuments() > 0)
    {
      using var document = _application.GetDocument(1);
      _application.Close(document);
    }

    _document.Dispose();
    _application.Dispose();
  }

  [Test]
  public void Main_IsTheFirstChildOfTheRoot()
  {
    // Arrange
    var root = _document.GetData().Root();

    // Act
    var entry = Ocaf.Entry(_main);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(entry, Is.EqualTo("0:1"));
      Assert.That(_main.Depth(), Is.EqualTo(1));
      Assert.That(_main.Father(), Is.EqualTo(root));
      Assert.That(root.IsRoot(), Is.True);
    }
  }

  [Test]
  public void NewChild_TakesTheNextFreeTag()
  {
    // Arrange
    var first = TDF_TagSource.NewChild(_main);

    // Act
    var second = TDF_TagSource.NewChild(_main);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(first.Tag(), Is.EqualTo(1));
      Assert.That(second.Tag(), Is.EqualTo(2));
      Assert.That(Ocaf.Entry(second), Is.EqualTo("0:1:2"));
      Assert.That(_main.NbChildren(), Is.EqualTo(2));
    }
  }

  [Test]
  public void Label_EqualsAnotherProxyOfTheSameNode()
  {
    // Arrange
    var label = _main.FindChild(7);

    // Act
    var again = _main.FindChild(7, false);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(again, Is.Not.SameAs(label));
      Assert.That(again, Is.EqualTo(label));
      Assert.That(again.GetHashCode(), Is.EqualTo(label.GetHashCode()));
      Assert.That(_main.FindChild(8), Is.Not.EqualTo(label));
    }
  }

  [Test]
  public void Entry_ResolvesToItsLabel()
  {
    // Arrange
    var label = _main.FindChild(3).FindChild(4);
    var resolved = new TDF_Label();

    // Act
    TDF_Tool.Label(_document.GetData(), "0:1:3:4", resolved);

    // Assert
    Assert.That(resolved, Is.EqualTo(label));
  }

  [Test]
  public void Get_FindsTheDocumentOfALabel()
  {
    // Arrange
    var label = _main.FindChild(1);

    // Act
    var document = TDocStd_Document.Get(label);

    // Assert
    Assert.That(document, Is.EqualTo(_document));
  }

  [Test]
  public void Name_RoundTripsUtf16()
  {
    // Arrange (Latin-1, a CJK character and a surrogate pair)
    const string text = NonAscii.WideText;

    // Act
    TDataStd_Name.Set(_main, text);

    // Assert
    Assert.That(Ocaf.Name(_main), Is.EqualTo(text));
  }

  [Test]
  public void AsciiString_RoundTripsUtf8()
  {
    // Arrange
    const string text = NonAscii.Text + "/part-7";

    // Act
    var attribute = TDataStd_AsciiString.Set(_main, text);

    // Assert
    Assert.That(attribute.Get(), Is.EqualTo(text));
  }

  [Test]
  public void FindAttribute_ReturnsTheAttributeThatWasSet()
  {
    // Arrange
    var set = TDataStd_Integer.Set(_main, 42);
    TDF_Attribute? attribute = null;

    // Act
    var found = _main.FindAttribute(TDataStd_Integer.GetID(), ref attribute);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(found, Is.True);
      Assert.That(attribute, Is.EqualTo(set));
      Assert.That(TDataStd_Integer.DownCast(attribute).Get(), Is.EqualTo(42));
    }
  }

  [Test]
  public void FindAttribute_LeavesTheVariableNullWhenMissing()
  {
    // Arrange
    TDataStd_Real.Set(_main.FindChild(1), 1.5);
    TDF_Attribute? attribute = null;

    // Act
    var found = _main.FindAttribute(TDataStd_Real.GetID(), ref attribute);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(found, Is.False);
      Assert.That(attribute, Is.Null);
    }
  }

  [Test]
  public void FindAttribute_OnANullLabelThrowsAndKeepsTheVariable()
  {
    // Arrange
    var nullLabel = new TDF_Label();
    TDF_Attribute kept = TDataStd_Integer.Set(_main, 1);
    var attribute = kept;

    // Act
    Action find = () => nullLabel.FindAttribute(TDataStd_Integer.GetID(), ref attribute);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(
        find,
        Throws.TypeOf<OcctException>()
          .With.Property(nameof(OcctException.OcctType))
          .EqualTo("Standard_NullObject"));
      Assert.That(attribute, Is.SameAs(kept));
    }
  }

  [Test]
  public void GetID_MatchesTheGuidOcctDeclares()
  {
    // Arrange (TDataStd_Name.cxx)
    var declared = new Guid("2a96b608-ec8b-11d0-bee7-080009dc3333");

    // Act
    var id = TDataStd_Name.GetID();

    // Assert
    Assert.That(id, Is.EqualTo(declared));
  }

  [Test]
  public void UserAttribute_KeepsItsGuid()
  {
    // Arrange
    var id = UserId;

    // Act
    var attribute = TDataStd_UAttribute.Set(_main, id);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(attribute.ID(), Is.EqualTo(id));
      Assert.That(_main.IsAttribute(id), Is.True);
    }
  }

  [Test]
  public void Undo_RestoresTheCommittedValue()
  {
    // Arrange
    var counter = CommitTwoValues(1, 2);

    // Act
    var undone = _document.Undo();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(undone, Is.True);
      Assert.That(counter.Get(), Is.EqualTo(1));
      Assert.That(_document.GetAvailableRedos(), Is.EqualTo(1));
    }
  }

  [Test]
  public void Redo_ReappliesTheUndoneValue()
  {
    // Arrange
    var counter = CommitTwoValues(1, 2);
    _document.Undo();

    // Act
    var redone = _document.Redo();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(redone, Is.True);
      Assert.That(counter.Get(), Is.EqualTo(2));
    }
  }

  [Test]
  public void AbortCommand_DiscardsTheChanges()
  {
    // Arrange (with the default undo limit 0, OpenCommand opens no transaction)
    _document.SetUndoLimit(10);
    _document.OpenCommand();
    TDataStd_Name.Set(_main, "draft");

    // Act
    _document.AbortCommand();

    // Assert
    Assert.That(_main.IsAttribute(TDataStd_Name.GetID()), Is.False);
  }

  [Test]
  public void NamedShape_KeepsTheGeneratedShape()
  {
    // Arrange
    var box = Shapes.Box();
    var builder = new TNaming_Builder(_main);

    // Act
    builder.Generated(box);
    var named = Ocaf.Find(_main, TNaming_NamedShape.GetID(), TNaming_NamedShape.DownCast);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(named, Is.Not.Null);
      Assert.That(named?.Evolution(), Is.EqualTo(TNaming_PRIMITIVE));
      Assert.That(named?.Get().IsSame(box), Is.True);
    }
  }

  [Test]
  public void Attributes_OutliveTheirCollectedProxies()
  {
    // Arrange
    NameChildren(_main, 100);

    // Act
    Garbage.Collect();

    // Assert
    var names = Enumerable.Range(1, 100).Select(tag => Ocaf.Name(_main.FindChild(tag, false)));
    Assert.That(names, Is.EqualTo(Enumerable.Range(1, 100).Select(tag => $"child {tag}")));
  }

  [Test]
  public void Application_KeepsADocumentUntilItIsClosed()
  {
    // Arrange
    NewNamedDocument("kept");

    // Act
    Garbage.Collect();

    // Assert
    var names = Enumerable.Range(1, _application.NbDocuments())
      .Select(index => Ocaf.Name(_application.GetDocument(index).Main()));
    Assert.That(names, Is.EquivalentTo(new[] { null, "kept" }));
  }

  [Test]
  public void Proxies_KeepTheTreeOfAClosedDocumentAlive()
  {
    // Arrange
    TDocStd_Document? document = null;
    _application.NewDocument("BinOcaf", ref document);
    var label = document!.Main().FindChild(1);
    var name = TDataStd_Name.Set(label, "survivor");
    var builder = new TNaming_Builder(label);
    builder.Generated(Shapes.Box());
    _application.Close(document);
    document.Dispose();
    Garbage.Collect();

    // Act (without the keep-alive the tree is gone here, and releasing the builder's
    // TNaming_NamedShape reads freed memory)
    var text = name.Get();
    var tag = label.Tag();
    builder.Dispose();
    name.Dispose();
    label.Dispose();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(text, Is.EqualTo("survivor"));
      Assert.That(tag, Is.EqualTo(1));
    }
  }

  [Test]
  public void CommitCommand_RecordsTheLabelsItTouched()
  {
    // Arrange
    _document.SetUndoLimit(10);
    _document.OpenCommand();
    TDataStd_Real.Set(_main.FindChild(1), 1.5);
    TDataStd_Name.Set(_main.FindChild(2), "touched");

    // Act
    _document.CommitCommand();
    using var undos = _document.GetUndos();
    var touched = undos.Last().AttributeDeltas().Select(delta => Ocaf.Entry(delta.Label()));

    // Assert
    Assert.That(touched.Distinct(), Is.EquivalentTo(new[] { "0:1:1", "0:1:2" }));
  }

  [Test]
  public void Undo_RestoresAReferenceListAndAnArray()
  {
    // Arrange
    _document.SetUndoLimit(10);
    _document.OpenCommand();
    var references = TDataStd_ReferenceList.Set(_main.FindChild(4));
    references.Append(_main.FindChild(1));
    references.Append(_main.FindChild(2));
    var values = TDataStd_RealArray.Set(_main.FindChild(5), 1, 3);
    values.ChangeArray(new TColStd_HArray1OfReal(new TColStd_Array1OfReal([1.0, 2.0, 3.0])));
    _document.CommitCommand();
    _document.OpenCommand();
    references.Remove(_main.FindChild(1));
    references.Append(_main.FindChild(3));
    values.SetValue(2, 20.0);
    _document.CommitCommand();

    // Act
    _document.Undo();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(
        Ocaf.Find(_main.FindChild(4), TDataStd_ReferenceList.GetID(),
                  TDataStd_ReferenceList.DownCast)
          ?.List()
          .Select(Ocaf.Entry), Is.EqualTo(new[] { "0:1:1", "0:1:2" }));
      Assert.That(
        Ocaf.Find(_main.FindChild(5), TDataStd_RealArray.GetID(), TDataStd_RealArray.DownCast)
          ?.Array()
          .ToArray(), Is.EqualTo(new[] { 1.0, 2.0, 3.0 }));
    }
  }

  [Test]
  public void Undo_RestoresTheShapeANamedShapeHadBefore()
  {
    // Arrange
    _document.SetUndoLimit(10);
    var label = _main.FindChild(1);
    _document.OpenCommand();
    new TNaming_Builder(label).Generated(Shapes.Box());
    _document.CommitCommand();
    _document.OpenCommand();
    new TNaming_Builder(label).Generated(Shapes.Cylinder());
    _document.CommitCommand();

    // Act
    _document.Undo();

    // Assert
    var named = Ocaf.Find(label, TNaming_NamedShape.GetID(), TNaming_NamedShape.DownCast);
    Assert.That(Shapes.Volume(named!.Get()), Is.EqualTo(Shapes.BoxVolume).Within(1e-6));
  }

  [Test]
  public void CopyLabel_CopiesTheAttributesOfTheSubtree()
  {
    // Arrange
    var source = _main.FindChild(1);
    TDataStd_Name.Set(source, "source");
    TDataStd_Real.Set(source.FindChild(1), 2.5);
    new TNaming_Builder(source.FindChild(2)).Generated(Shapes.Box());
    var target = _main.FindChild(2);
    using var copy = new TDF_CopyLabel(source, target);

    // Act
    copy.Perform();

    // Assert
    var named = Ocaf.Find(target.FindChild(2, false), TNaming_NamedShape.GetID(),
                          TNaming_NamedShape.DownCast);
    using (Assert.EnterMultipleScope())
    {
      Assert.That(copy.IsDone(), Is.True);
      Assert.That(Ocaf.Name(target), Is.EqualTo("source"));
      Assert.That(
        Ocaf.Find(target.FindChild(1, false), TDataStd_Real.GetID(), TDataStd_Real.DownCast)?.Get(),
        Is.EqualTo(2.5));
      Assert.That(Shapes.Volume(named!.Get()), Is.EqualTo(Shapes.BoxVolume).Within(1e-6));
    }
  }

  // The holders below keep attributes of their tree, a NamedShape among them: released after the
  // tree, they would read freed memory.
  [Test]
  public void CopyLabel_ThatPerformed_OutlivesItsClosedDocument()
  {
    // Arrange
    var document = DocumentWithABox();
    var copy = PerformedCopy(document);
    CloseAndCollect(document);

    // Act
    var done = copy.IsDone();
    copy.Dispose();
    Garbage.Collect();

    // Assert
    Assert.That(done, Is.True);
  }

  [Test]
  public void RelocationTable_OfACopy_OutlivesItsClosedDocument()
  {
    // Arrange
    var document = DocumentWithABox();
    var relocation = RelocationTableOfACopy(document);
    CloseAndCollect(document);

    // Act
    var selfRelocate = relocation.SelfRelocate();
    relocation.Dispose();
    Garbage.Collect();

    // Assert (TDF_CopyLabel relocates what it doesn't copy to itself)
    Assert.That(selfRelocate, Is.True);
  }

  [Test]
  public void Delta_OfTheUndoStack_OutlivesItsClosedDocument()
  {
    // Arrange
    var document = DocumentWithABox();
    var delta = LastUndo(document);
    CloseAndCollect(document);

    // Act
    var empty = delta.IsEmpty();
    delta.Dispose();
    Garbage.Collect();

    // Assert
    Assert.That(empty, Is.False);
  }

  [Test]
  public void DataSet_OfAClosure_OutlivesItsClosedDocument()
  {
    // Arrange
    var document = DocumentWithABox();
    var dataSet = ClosureOfTheBox(document);
    CloseAndCollect(document);

    // Act
    var empty = dataSet.IsEmpty();
    dataSet.Dispose();
    Garbage.Collect();

    // Assert
    Assert.That(empty, Is.False);
  }

  private TDataStd_Integer CommitTwoValues(int first, int second)
  {
    _document.SetUndoLimit(10);
    _document.OpenCommand();
    var counter = TDataStd_Integer.Set(_main, first);
    _document.CommitCommand();
    _document.OpenCommand();
    counter.Set(second);
    _document.CommitCommand();
    return counter;
  }

  // Separate, non-inlined methods: the proxies they create are unreachable afterwards.
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void NameChildren(TDF_Label parent, int count)
  {
    for (var tag = 1; tag <= count; tag++)
    {
      TDataStd_Name.Set(parent.FindChild(tag), $"child {tag}");
    }
  }

  // a document with a box at 0:1:1, generated in a committed command
  [MethodImpl(MethodImplOptions.NoInlining)]
  private TDocStd_Document DocumentWithABox()
  {
    TDocStd_Document? document = null;
    _application.NewDocument("BinOcaf", ref document);
    document!.SetUndoLimit(10);
    document.OpenCommand();
    new TNaming_Builder(document.Main().FindChild(1)).Generated(Shapes.Box());
    document.CommitCommand();
    return document;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static TDF_CopyLabel PerformedCopy(TDocStd_Document document)
  {
    var copy = new TDF_CopyLabel(document.Main().FindChild(1), document.Main().FindChild(2));
    copy.Perform();
    return copy;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static TDF_RelocationTable RelocationTableOfACopy(TDocStd_Document document)
  {
    using var copy = PerformedCopy(document);
    return copy.RelocationTable();
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static TDF_Delta LastUndo(TDocStd_Document document)
  {
    using var undos = document.GetUndos();
    return undos.Last();
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static TDF_DataSet ClosureOfTheBox(TDocStd_Document document)
  {
    // Closure starts from the labels of the set, which it makes its roots
    var dataSet = new TDF_DataSet();
    dataSet.AddLabel(document.Main().FindChild(1));
    TDF_ClosureTool.Closure(dataSet);
    return dataSet;
  }

  // closed, and its proxies collected: only what the test holds is left
  private void CloseAndCollect(TDocStd_Document document)
  {
    _application.Close(document);
    document.Dispose();
    Garbage.Collect();
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void NewNamedDocument(string name)
  {
    TDocStd_Document? document = null;
    _application.NewDocument("BinOcaf", ref document);
    TDataStd_Name.Set(document!.Main(), name);
  }
}
