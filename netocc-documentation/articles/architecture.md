# How NetOcc works

## From headers to packages

<img class="diagram-light" src="../assets/architecture-pipeline.svg" width="800" height="816" alt="From headers to packages" loading="lazy">
<img class="diagram-dark" src="../assets/architecture-pipeline-dark.svg" width="800" height="816" alt="From headers to packages" loading="lazy">

- **netocc-gen** reads OCCT's headers with libclang and writes an interface file per package: which classes and members to wrap, how their handles, collections and references map. Members that can't be mapped are skipped with a reason.
- **SWIG** turns the interface files into C++ wrapper functions (`extern "C"`, one per member) and the C# proxies that call them.
- **The natives** are one library per OCCT module (`NetOccFoundationClasses`, `NetOccModelingData`, `NetOccModelingAlgorithms`, `NetOccDataExchange`, `NetOccApplicationFramework`, `NetOccVisualization`) plus `NetOccRuntime`, linked against OCCT's `TK*` libraries.

## Packages

<img class="diagram-light" src="../assets/architecture-packages.svg" width="800" height="416" alt="Packages" loading="lazy">
<img class="diagram-dark" src="../assets/architecture-packages-dark.svg" width="800" height="416" alt="Packages" loading="lazy">

## A call

<img class="diagram-light" src="../assets/architecture-call.svg" width="800" height="544" alt="A call" loading="lazy">
<img class="diagram-dark" src="../assets/architecture-call-dark.svg" width="800" height="544" alt="A call" loading="lazy">

Each call crosses into native code once, which costs around 10 ns (`shape.ShapeType()` on x64): nothing against a modeling algorithm, noticeable in loops over millions of nodes, where the bulk copies (`NodesToArray()`, `ToArray()`) help.

## Loading the natives

A module initializer runs before the first call: on .NET it registers a resolver for NetOcc's libraries, on .NET Framework it loads them up front. Both look next to the app:

<img class="diagram-light" src="../assets/architecture-loading.svg" width="800" height="432" alt="Loading the natives" loading="lazy">
<img class="diagram-dark" src="../assets/architecture-loading-dark.svg" width="800" height="432" alt="Loading the natives" loading="lazy">

On Windows each library loads with its own folder first in the search path, so another OCCT on `PATH` doesn't interfere.

## Documentation

`NetOcc.xml`, which IntelliSense shows, is written at pack time from OCCT's doc comments in its headers (LGPL, see the package's NOTICE). It's matched to NetOcc's members by class, member name and parameter names.
