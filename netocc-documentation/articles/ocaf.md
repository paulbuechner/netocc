# Documents

OCAF (the Open CASCADE Application Framework) stores application data in a tree of labels carrying attributes, with undo, redo and files for free. [XCAF](xcaf.md) is built on it.

<img class="diagram-light" src="../assets/ocaf-labels.svg" width="800" height="548" alt="An OCAF document's label tree" loading="lazy">
<img class="diagram-dark" src="../assets/ocaf-labels-dark.svg" width="800" height="548" alt="An OCAF document's label tree" loading="lazy">

A label is a place in the tree, named by its path of tags (its entry, `0:1:2`). An attribute is data on a label, at most one per attribute class (per GUID).

## Labels and attributes

[!code-csharp[](../samples/Ocaf.cs#labels)]

[!code-csharp[](../samples/Ocaf.cs#children)]

| Attribute | Holds |
|---|---|
| `TDataStd_Name` | a name |
| `TDataStd_Integer`, `TDataStd_Real`, `TDataStd_AsciiString` | a value |
| `TDataStd_IntegerArray`, `TDataStd_RealArray`, `TDataStd_ExtStringArray` | an array |
| `TDataStd_TreeNode` | a link into a tree other than the labels' |
| `TDataStd_UAttribute` | nothing: a marker, found by its GUID |
| `TNaming_NamedShape` | a shape, with its history (`TNaming_Builder`: `Generated`, `Modify`, `Delete`) |
| `TDF_Reference` | a link to another label |

`FindAttribute` takes the class's GUID (`TDataStd_Real.GetID()`) and gives a `TDF_Attribute`: `DownCast` it. `Set` on a label with the attribute already there changes it.

## Undo and redo

Changes between `OpenCommand` and `CommitCommand` form one undoable step:

<img class="diagram-light" src="../assets/ocaf-undo.svg" width="800" height="412" alt="Undo and redo" loading="lazy">
<img class="diagram-dark" src="../assets/ocaf-undo-dark.svg" width="800" height="412" alt="Undo and redo" loading="lazy">

[!code-csharp[](../samples/Ocaf.cs#undo)]

`AbortCommand()` drops the changes since `OpenCommand()`. With `SetNestedTransactionMode(true)`, commands nest.

## Files

[!code-csharp[](../samples/Ocaf.cs#save-open)]

| Format | Drivers | File |
|---|---|---|
| `BinOcaf` | `BinDrivers.DefineFormat(application)` | binary, `.cbf` |
| `XmlOcaf` | `XmlDrivers.DefineFormat(application)` | XML, `.xml` |
| `BinXCAF` | `BinXCAFDrivers.DefineFormat(application)` | binary XCAF, `.xbf` |
| `XmlXCAF` | `XmlXCAFDrivers.DefineFormat(application)` | XML XCAF, `.xml` |

`SaveAs` and `Open` also take a `System.IO.Stream`.

> [!IMPORTANT]
> Close documents through their application, `application.Close(document)`: the application holds a raw pointer to each document it opened, which only `Close` clears. Disposing the proxy isn't enough.
