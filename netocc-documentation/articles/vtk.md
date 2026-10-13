# VTK scenes

NetOcc.Vtk puts OCCT shapes into [VTK](https://vtk.org) scenes, next to simulation results: OCCT's TKIVtk on NetOcc and [NetVtk](https://github.com/paulbuechner/netvtk), the C# bindings for VTK. Its classes keep their OCCT names, in `OCC.Core.IVtk`, `IVtkOCC`, `IVtkVTK` and `IVtkTools`; VTK's are NetVtk's, in `Vtk`.

```bash
dotnet add package NetOcc.Vtk
```

It brings NetOcc and NetVtk along. Its runtime package holds TKIVtk and its wrappers only: OCCT's other libraries come with NetOcc's, VTK's with NetVtk's.

> [!IMPORTANT]
> NetOcc.Vtk depends on NetOcc of its own version and on exactly one NetVtk release: the VTK build TKIVtk is compiled against. Another NetVtk version in the same app fails the restore.

## A shape as VTK data

`IVtkTools_ShapeDataSource` is a `vtkPolyDataAlgorithm`: it meshes an `IVtkOCC_Shape`, and its output port connects to any VTK filter or mapper.

[!code-csharp[](../samples-vtk/Scenes.cs#shape-source)]

Its proxy is a NetVtk proxy: `using` releases its reference as for any VTK object, see NetVtk's documentation on lifetimes.

## A field on the shape

The output holds the shape's faces as triangles and its edges as lines; `IVtkTools_DisplayModeFilter` keeps what a display mode shows. A copy of the faces' mesh carries a result like any VTK data set:

[!code-csharp[](../samples-vtk/Scenes.cs#field)]

## Picking

`IVtkTools_ShapePicker` finds the shapes at a display position, and their sub-shapes of the selection mode's type (`SM_Face`, `SM_Edge`, `SM_Vertex`, `SM_Wire`, `SM_Shell`, `SM_Solid`, ...; `SM_Shape` the shapes only). It knows a shape by its actor, which `IVtkTools_ShapeObject.SetShapeSource` registers:

[!code-csharp[](../samples-vtk/Scenes.cs#picking)]

The ids are TKIVtk's; `IVtkOCC_Shape.GetSubShape(id)` gives the sub-shape. `IVtkTools_SubPolyDataFilter` passes on the cells of picked ids, for a highlight.

The [demo](https://github.com/paulbuechner/netocc/tree/main/netocc-vtk/demo) shows a part with a temperature on it in a WPF window, with a scalar bar and click picking.
