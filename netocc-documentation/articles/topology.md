# Topology

A shape is topology (what is connected to what) over geometry (where it is): faces lie on surfaces, edges on curves, vertices at points. `TopoDS` holds the topology, `BRep_Tool` reads the geometry behind it.

## The shape types

`TopoDS_Shape` is the base of all; each type is made of the one below:

```mermaid
flowchart TD
    compound["TopoDS_Compound<br/><i>any shapes, grouped</i>"]
    compsolid["TopoDS_CompSolid<br/><i>solids sharing faces</i>"]
    solid["TopoDS_Solid<br/><i>a volume bounded by shells</i>"]
    shell["TopoDS_Shell<br/><i>faces joined at edges</i>"]
    face["TopoDS_Face<br/><i>part of a surface, bounded by wires</i>"]
    wire["TopoDS_Wire<br/><i>edges joined at vertices</i>"]
    edge["TopoDS_Edge<br/><i>part of a curve</i>"]
    vertex["TopoDS_Vertex<br/><i>a point</i>"]
    compound --> compsolid --> solid --> shell --> face --> wire --> edge --> vertex
```

`ShapeType()` tells which one a `TopoDS_Shape` is; `TopoDS.Solid`, `TopoDS.Face`, `TopoDS.Edge` and so on cast to it, and throw `Standard_TypeMismatch` for the wrong type.

## What a shape is

A `TopoDS_Shape` is a light value: a reference to the shared `TopoDS_TShape` that holds the topology, a location and an orientation. Two shapes can share the TShape and still differ:

```mermaid
classDiagram
    class TopoDS_Shape {
        TShape() the shared topology and geometry
        Location() TopLoc_Location, where it's placed
        Orientation() TopAbs_Orientation, FORWARD or REVERSED
    }
    class TopoDS_TShape {
        sub-shapes
        geometry via BRep_TFace, BRep_TEdge, BRep_TVertex
    }
    TopoDS_Shape --> TopoDS_TShape : refers to
```

[!code-csharp[](../samples/Topology.cs#identity)]

| Compare | Same TShape | Same location | Same orientation |
|---|---|---|---|
| `IsPartner` | yes | | |
| `IsSame` | yes | yes | |
| `IsEqual` | yes | yes | yes |

Maps and `TopExp` use `IsSame`: a face and its reversed twin are one entry.

## Exploring

[!code-csharp[](../samples/Topology.cs#explore)]

| Tool | Gives | Duplicates |
|---|---|---|
| `TopExp_Explorer(shape, type)` | every sub-shape of a type, at any depth | yes: once per occurrence |
| `TopExp.MapShapes(shape, type, map)` | the same, into a `TopTools_IndexedMapOfShape` numbered from 1 | no |
| `TopoDS_Iterator(shape)` | the direct children only | yes |
| `TopExp.MapShapesAndAncestors` | each sub-shape with the shapes containing it | no |

[!code-csharp[](../samples/Topology.cs#ancestors)]

`TopExp.FirstVertex(edge)`, `LastVertex`, `Vertices(edge, first, last)` (fills two `TopoDS_Vertex`) and `CommonVertex` answer the usual questions about edges.

## Geometry behind the topology

```mermaid
flowchart LR
    vertex[TopoDS_Vertex] -->|BRep_Tool.Pnt| pnt[gp_Pnt]
    edge[TopoDS_Edge] -->|BRep_Tool.Curve| curve["Geom_Curve<br/>+ range first..last"]
    edge -->|BRep_Tool.CurveOnSurface| pcurve["Geom2d_Curve<br/>on each face"]
    face[TopoDS_Face] -->|BRep_Tool.Surface| surface[Geom_Surface]
    face -->|BRep_Tool.Triangulation| mesh[Poly_Triangulation]
```

[!code-csharp[](../samples/Topology.cs#geometry)]

Every sub-shape carries a tolerance: the distance within which its geometry is considered to meet its neighbours'. Tolerances grow as algorithms work; `BRep_Tool.Tolerance` reads them.

> [!TIP]
> For evaluating a face or an edge, `BRepAdaptor_Surface` and `BRepAdaptor_Curve` apply the shape's location and bounds for you: `new BRepAdaptor_Surface(face).Value(u, v)`.

## Building bottom-up

Vertices, edges, wires, faces: each `BRepBuilderAPI_Make*` takes the level below.

[!code-csharp[](../samples/Topology.cs#bottom-up)]

| Builder | From |
|---|---|
| `BRepBuilderAPI_MakeVertex` | a `gp_Pnt` |
| `BRepBuilderAPI_MakeEdge` | two points, a `gp_Lin`/`gp_Circ`, a `Geom_Curve` with or without a range |
| `BRepBuilderAPI_MakeWire` | up to four edges, or `Add` edges and wires; they must connect |
| `BRepBuilderAPI_MakePolygon` | points, closed with `true` or `Close()` |
| `BRepBuilderAPI_MakeFace` | a planar wire, a `Geom_Surface` with bounds, a surface and a wire |
| `BRepBuilderAPI_MakeShell`, `MakeSolid` | a surface; shells |

Faces from wires need planar wires; for anything else build the surface first, or use `BRepFill_Filling` or `BRepOffsetAPI_MakeFilling`.

## Compounds

[!code-csharp[](../samples/Topology.cs#compound)]

A compound is the usual result of reading a file with several shapes, and a cheap way to pass many shapes as one.
