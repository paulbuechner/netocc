# Troubleshooting

## Loading

**`DllNotFoundException` for `NetOccFoundationClasses` or another `NetOcc*` library**

- Windows: install the [Visual C++ 2015-2022 runtime](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist) for the process's architecture.
- Check that the platform has natives: win-x64, win-x86, linux-x64, osx-arm64. There are none for other platforms yet.
- Linux: glibc 2.35 or newer (`ldd --version`); `ldd runtimes/linux-x64/native/libNetOccFoundationClasses.so` lists missing system libraries.
- .NET Framework: the build copies the natives into `x64\` and `x86\` next to the app; check they're there.

**`BadImageFormatException`**

A 32-bit process found 64-bit natives or the reverse. With AnyCPU on .NET Framework, NetOcc picks `x64\` or `x86\` itself: check both folders exist, or build for one platform.

## Crashes

**The process exits without an exception**

A crash in native code, which .NET can't catch, where C++ would crash too. A common cause is `ShapeType()` on a `TopoDS_Shape` whose `IsNull()` is true. See [Errors](errors.md#what-isnt-checked).

**A crash at exit or in the finalizer thread, with a view open**

Views and drivers were left to the finalizer, which runs on another thread without their GL context. Dispose them on the UI thread, see [Object lifetimes](lifetimes.md#threads).

## Results

**A STEP file reads as an empty shape**

Check `ReadFile`'s status, then what `TransferRoots()` returns: 0 means no root translated. `STEPControl_Reader.NbRootsForTransfer()` counts the roots. Assembly structure, names and colors need the XCAF reader, see [Assemblies](xcaf.md).

**A boolean or fillet fails**

Check the input first (`BRepCheck_Analyzer`, `ShapeFix_Shape`), then try `SetFuzzyValue` with a small tolerance for booleans on shapes that almost touch. See [Analysis and repair](analysis.md#checking-and-repairing).

**Meshing again has no effect**

A shape keeps its triangulation; a coarser deflection keeps the finer one there. `BRepTools.Clean(shape)` removes it, see [Meshing](meshing.md#meshing-again).

## Performance

**Memory grows**

The garbage collector sees only the proxies, not the native memory behind them. Dispose meshers and large algorithms when done, and close documents through their application, see [Object lifetimes](lifetimes.md).

**Loops over nodes or shapes are slow**

Each call crosses into native code. Read triangulations with `NodesToArray()` and `TrianglesToArray()`, and collections with `ToArray()`, instead of an element per call.
