# Documents

OCAF (the Open CASCADE Application Framework) stores application data in a tree of labels carrying attributes, with undo, redo and files for free. [XCAF](xcaf.md) is built on it.

```mermaid
flowchart TD
    app["TDocStd_Application<br/><i>formats, open documents</i>"] --> doc["TDocStd_Document<br/><i>undo, redo, file</i>"]
    doc --> root["0 root label"]
    root --> main["0:1 main label"]
    main --> l1["0:1:1"]
    main --> l2["0:1:2"]
    l1 --- a1(["TDataStd_Name: width"])
    l1 --- a2(["TDataStd_Real: 40.0"])
    l2 --- a3(["TDataStd_Name: part"])
    l2 --- a4(["TNaming_NamedShape: a box"])
```

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

```mermaid
sequenceDiagram
    participant App as your code
    participant Doc as TDocStd_Document
    App->>Doc: SetUndoLimit(10)
    App->>Doc: OpenCommand()
    App->>Doc: TDataStd_Real.Set(width, 55)
    Note over Doc: attributes back up<br/>their old values
    App->>Doc: CommitCommand()
    App->>Doc: Undo()
    Note over Doc: width is 40 again
    App->>Doc: Redo()
```

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
