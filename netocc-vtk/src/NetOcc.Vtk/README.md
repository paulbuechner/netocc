# NetOcc.Vtk

OCCT shapes in [VTK](https://vtk.org) scenes: C# bindings for Open CASCADE Technology 8.0.1's TKIVtk (`IVtk`, `IVtkOCC`, `IVtkVTK`, `IVtkTools`) on [NetOcc](https://www.nuget.org/packages/NetOcc) and [NetVtk](https://www.nuget.org/packages/NetVtk).

```bash
dotnet add package NetOcc.Vtk
```

- `IVtkTools_ShapeDataSource` is a `vtkPolyDataAlgorithm`: feed it an `IVtkOCC_Shape` and connect its output to any VTK pipeline, next to your simulation results.
- `IVtkTools_ShapePicker` picks shapes and their faces, edges and vertices in a `vtkRenderer`.
- Built on exactly one NetVtk version, whose VTK build TKIVtk is compiled against.

MIT. OCCT is LGPL-2.1 with the Open CASCADE exception, VTK BSD-3-Clause; the runtime packages carry their notices.
