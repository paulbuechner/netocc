# Assemblies

XCAF (Extended CAF) documents hold what a STEP file has beyond geometry: product structure, names, colors, layers, materials, PMI. They are [OCAF documents](ocaf.md) with a fixed layout, managed by tools: `XCAFDoc_ShapeTool` for shapes and assemblies, `XCAFDoc_ColorTool` for colors, `XCAFDoc_LayerTool` for layers.

## The label tree

A table made of a plate and one post placed four times:

```mermaid
flowchart TD
    main["0:1 main"] --> shapes["0:1:1 shapes<br/>XCAFDoc_ShapeTool"]
    main --> colors["0:1:2 colors<br/>XCAFDoc_ColorTool"]
    main --> layers["0:1:3 layers<br/>XCAFDoc_LayerTool"]
    shapes --> plate["0:1:1:1 plate<br/><i>a part: a shape</i>"]
    shapes --> post["0:1:1:2 post<br/><i>a part</i>"]
    shapes --> table["0:1:1:3 table<br/><i>an assembly</i>"]
    table --> c1["0:1:1:3:1<br/><i>a component: plate at its place</i>"]
    table --> c2["0:1:1:3:2 ... 0:1:1:3:5<br/><i>components: post, four places</i>"]
    c1 -.->|refers to| plate
    c2 -.->|refers to| post
```

| Label | `XCAFDoc_ShapeTool` test | Holds |
|---|---|---|
| part | `IsSimpleShape` | a shape, defined once |
| assembly | `IsAssembly` | components |
| component | `IsReference` (`IsComponent`) | a reference to a part or an assembly, and a location |
| free shape | `IsFree` | a part or an assembly nothing refers to: a root |

Names (`TDataStd_Name`) and colors sit on any of them: a component's name is the instance's, and its color overrides its part's.

## Writing

[!code-csharp[](../samples/Xcaf.cs#write-assembly)]

`AddShape(shape, true)` (the default) turns a compound into an assembly with its children as parts; `false` keeps it one part.

## Reading

Walk the tree from the free shapes, following references and composing locations:

[!code-csharp[](../samples/Xcaf.cs#read-assembly)]

Colors come in three kinds: `XCAFDoc_ColorGen` (the whole shape), `XCAFDoc_ColorSurf` (faces), `XCAFDoc_ColorCurv` (edges). Readers put a STEP file's colors where the file had them; ask for more than one kind. Faces can have their own colors: `XCAFDoc_ShapeTool.GetSubShapes` lists the labels of colored sub-shapes.

## Showing a document

`XCAFPrs_AISObject` shows a label with its colors, see [3D views](visualization.md#documents).

> [!IMPORTANT]
> `XCAFApp_Application.GetApplication()` is one per process. Close each document with `application.Close(document)`: OCCT keeps documents it opened until then.
