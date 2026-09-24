# NetOcc

[Open CASCADE Technology](https://dev.opencascade.org) (OCCT) 8.0.1 for .NET: C# bindings generated from OCCT's headers. OCCT's names don't change, each OCCT package is a namespace (`gp_Pnt` is `OCC.Core.gp.gp_Pnt`), and IntelliSense shows OCCT's documentation.

- **Frameworks:** .NET Framework 3.5 and 4.5+, .NET 6, 8 and 10, netstandard2.0, in one AnyCPU assembly.
- **Platforms:** win-x64, win-x86, linux-x64 and osx-arm64. The natives come with the package (`NetOcc.runtime.<rid>`).
- **Covered:** all 362 packages of OCCT's FoundationClasses, ModelingData, ModelingAlgorithms, ApplicationFramework and DataExchange modules and of Visualization's TKService, TKV3d, TKOpenGl and TKMeshVS: geometry, topology, booleans, fillets, offsets, sweeps, shape healing, meshing, OCAF and XCAF documents, STEP, IGES, STL, glTF, OBJ, PLY and VRML files, and 3D views.

## Requirements

| Platform | Needs |
|---|---|
| Windows | the Visual C++ 2015-2022 runtime |
| Linux | glibc 2.35 or newer |
| macOS | 14 or newer, on Apple silicon |

## Example

A block with a hole, rounded, measured, and written as STEP and STL:

```csharp
using OCC.Core.BRepAlgoAPI;
using OCC.Core.BRepFilletAPI;
using OCC.Core.BRepGProp;
using OCC.Core.BRepMesh;
using OCC.Core.BRepPrimAPI;
using OCC.Core.GProp;
using OCC.Core.gp;
using OCC.Core.STEPControl;
using OCC.Core.StlAPI;
using OCC.Core.TopAbs;
using OCC.Core.TopExp;
using OCC.Core.TopoDS;
using OCC.Core.TopTools;

// a 60 x 40 x 20 block with a hole through it
var block = new BRepPrimAPI_MakeBox(60, 40, 20).Shape();
var axis = new gp_Ax2(new gp_Pnt(30, 20, -1), new gp_Dir(0, 0, 1));
var hole = new BRepPrimAPI_MakeCylinder(axis, 8, 22).Shape();
var part = new BRepAlgoAPI_Cut(block, hole).Shape();

// round every edge by 1.5, each once
var edges = new TopTools_IndexedMapOfShape();
TopExp.MapShapes(part, TopAbs_ShapeEnum.TopAbs_EDGE, edges);
var fillet = new BRepFilletAPI_MakeFillet(part);
foreach (var edge in edges)
{
  fillet.Add(1.5, TopoDS.Edge(edge));
}

var rounded = fillet.Shape();

// its volume, then the part as STEP and, triangulated, as STL
var properties = new GProp_GProps();
BRepGProp.VolumeProperties(rounded, properties);
Console.WriteLine($"volume: {properties.Mass():F1} mm³");

var step = new STEPControl_Writer();
step.Transfer(rounded, STEPControl_StepModelType.STEPControl_AsIs);
step.Write("part.step");

new BRepMesh_IncrementalMesh(rounded, 0.1);
new StlAPI_Writer().Write(rounded, "part.stl");
```

## Documentation

- [Guide](https://paulbuechner.github.io/netocc/articles/introduction.html): geometry, topology, modeling, meshing, files, documents and 3D views; every example compiles against the package in CI, and all but the on-screen views run there.
- [From C++ to C#](https://paulbuechner.github.io/netocc/articles/mapping.html): how OCCT's signatures look in C#, so OCCT's own documentation and samples apply.
- [Class index](https://paulbuechner.github.io/netocc/classes/index.html): every type, linked to OCCT's reference manual.
- [Source and demos](https://github.com/paulbuechner/netocc), [changes](https://github.com/paulbuechner/netocc/blob/main/netocc-core/CHANGELOG.md).

## License

NetOcc is MIT licensed. The IntelliSense documentation holds the doc comments of OCCT's headers, and the runtime packages hold OCCT's libraries: both are LGPL-2.1 with the Open CASCADE exception. `NOTICE` and `OCCT-LICENSE.txt` in the package have the details.

NetOcc is not affiliated with, endorsed by, or sponsored by Open Cascade SAS. "Open CASCADE" and "Open CASCADE Technology" are trademarks of Open Cascade SAS.
