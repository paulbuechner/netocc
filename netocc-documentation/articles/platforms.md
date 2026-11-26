# Platforms

## Frameworks

One AnyCPU assembly: .NET Framework 3.5, 4.5 to 4.8, .NET 6, 8 and 10, and netstandard2.0.

## Natives

`NetOcc` depends on one package of natives per platform, `NetOcc.runtime.<rid>`: NetOcc's native libraries and OCCT's.

| RID | Needs |
|---|---|
| win-x64, win-x86 | the Visual C++ 2015-2022 runtime |
| linux-x64 | glibc 2.35 or newer (Ubuntu 22.04) |
| osx-arm64 | macOS 14 or newer |

.NET apps load the natives from `runtimes/<rid>/native`, or from next to the app when it has a `RuntimeIdentifier`. .NET Framework apps get them in `x86\` or `x64\` next to the app, only for the architecture the app runs as; NetOcc picks the folder for the process.

| .NET Framework project | Natives |
|---|---|
| `PlatformTarget` x86, or an AnyCPU exe with `Prefer32Bit` | `x86\` |
| `PlatformTarget` x64 | `x64\` |
| AnyCPU: a library, a test project, an exe without `Prefer32Bit` | `x86\` and `x64\` |

Without a `PlatformTarget`, the `RuntimeIdentifier` decides. NuGet's copies of a `RuntimeIdentifier`'s natives next to a .NET Framework app are left out (Windows would search them first for every library the process loads), and so are the natives of another architecture that a referenced AnyCPU library hands on.

### Modules

An app that uses some of OCCT's modules can ship their natives only: name them in `NetOccModules`.

```xml
<PropertyGroup>
  <NetOccModules>FoundationClasses;ModelingData;ModelingAlgorithms;DataExchange</NetOccModules>
</PropertyGroup>
```

The modules are FoundationClasses, ModelingData, ModelingAlgorithms, Visualization, ApplicationFramework and DataExchange; the [class index](../classes/index.md) lists each one's packages. The build copies their `NetOcc<Module>` libraries, NetOccRuntime and the OCCT and third-party libraries these load, on .NET Framework and .NET, from every runtime package. List every module whose classes the app uses: a call into another fails with `DllNotFoundException` for its `NetOcc<Module>`. Unset, every module ships.

A STEP read with geometry needs the four above: 62 of the 69 files on Windows, without NetOccApplicationFramework, NetOccVisualization, TKOpenGl, TKMeshVS, brotlienc, hwloc and tbbmalloc_proxy; 62 of the 68 on macOS, the same but hwloc. Geometry alone, without DataExchange, needs 26 of the 68 on macOS.

## NetOcc.Vtk

The same frameworks and RIDs. `NetOcc.Vtk.runtime.<rid>` holds NetOccVtk and OCCT's TKIVtk only: OCCT's other libraries come with `NetOcc.runtime.<rid>`, VTK's with `NetVtk.runtime.<rid>`. Its natives are laid out like NetOcc's, and keep what they load of OCCT's and VTK's when `NetOccModules` or NetVtk's `NetVtkModules` selects modules. On Linux, VTK needs X11, GL and EGL libraries besides; its rendering needs OpenGL 3.2.

## 32-bit

On win-x86, `size_t` is four bytes: a `ulong` above 32 bits throws `OcctException` (`Standard_OutOfRange`) instead of wrapping around, as C `long`, which is 32 bits on Windows, does everywhere.

## Views

The OpenGL driver draws into a window of the platform: an HWND, an X11 window with its display connection (`Aspect_DisplayConnection`), an NSView. The demos host an HWND, so they run on Windows.
