# Design decisions

Why NetOcc is built the way it is. How the parts fit: [How NetOcc works](architecture.md). The traps behind many of these decisions: [Pitfalls](pitfalls.md).

## Decided

| Question | Decision | Why |
|---|---|---|
| Wrapping | SWIG with NetOcc's own C# typemaps, driven by a C# generator on libclang (ClangSharp) | SWIG writes the C++ shim and the P/Invoke proxies, which run on every OS; C++/CLI runs on Windows only |
| Names | OCCT's, unchanged; a namespace per package, `OCC.Core.<Package>` | C++ samples and OCCT's manual carry over line by line |
| Math values | `gp`, `Bnd_Box(2d)`, `Quantity_Color(RGBA)`, `Poly_Triangle` and OCCT's plain data as C# structs with OCCT's layout | no native allocation per point, bulk copies of meshes |
| References | C++ `&` is `ref`, never `out`; a struct's `const&` is `in` | OCCT reads some references before writing them |
| Handles | SWIG's `ref`/`unref` on `Standard_Transient`: each proxy holds one reference | OCCT's own reference counting decides lifetimes |
| Strings | `TCollection_AsciiString` UTF-8, `TCollection_ExtendedString` UTF-16, both `string`; `Standard_GUID` is `System.Guid` | Unicode paths work on every platform; OCCT 8's `Standard_UUID` has `Guid`'s layout |
| OCAF lifetime | label, attribute and `TNaming_Builder` proxies keep their `TDF_Data` alive | finalizers run in any order, long after `Close` |
| Subclassing | SWIG directors for the classes OCCT is meant to be derived from (netocc-gen's `directors`: progress indicators, printers, presentations, owners, the view controller), not every class | each director gets a C++ subclass overriding all its virtual members, a C# delegate per member and a lookup of overrides; lifetime, identity and exceptions need the runtime's care (`Directors.i`, `Directors.cs`) |
| Director lifetime | a GC handle, strong from the moment the object is passed to OCCT until a sweep after a full collection finds only its proxy's reference; a disposed object held by OCCT is released once OCCT lets go | OCCT holds by reference count, which the GC doesn't see, and `Standard_Transient`'s counting isn't virtual: nothing tells C# when OCCT takes or drops a reference |
| Exceptions in overrides | caught at the callback, parked under a number, rethrown in C++ right after the callback (a result's or parameter's conversion) as `NetOcc_ManagedException`; the outer call's `OcctException` carries it as `InnerException` | a .NET exception can't unwind through C++; OCCT unwinds as from its own |
| Progress | trailing `Message_ProgressRange` defaults wrapped, as overloads | a C# `Message_ProgressIndicator` hands out ranges to report and cancel through |
| Platforms | win-x64, win-x86, linux-x64, osx-arm64; one AnyCPU assembly | win-x86, since .NET Framework's AnyCPU apps often run as 32-bit processes |
| Frameworks | net35, net45, net452, net462, net6.0, net8.0, net10.0, and netstandard2.0 | .NET Framework apps from 3.5 on get a build of their own; netstandard2.0 serves libraries |
| OCCT | vcpkg manifest, built from source with OCCT's precondition checks on, binary-cached | the same OCCT on every platform, and checks that throw instead of crashing |
| SWIG | 4.5, from conda-forge into `netocc-core/.tools` | PyPI's wheel lagged behind; no conda install needed |
| Native libraries | one per OCCT module (`NetOcc<Module>`) and `NetOccRuntime`, no dots in the names | a Windows DLL exports at most 65,535 functions (the wrappers have about 71,000); `LoadLibrary` takes a dot for the extension |
| Packages | `NetOcc` (managed) and one `NetOcc.runtime.<rid>` per platform, no split by OCCT module | one reference for users; each package stays well under nuget.org's 250 MB limit, which all of them together nearly reach (8.0.1.1-next.1: about 207 MB), and holds one platform's natives and notices. `NetOcc` depends on all four, so an app without a RuntimeIdentifier runs anywhere; `dotnet publish -r <rid>` copies one platform's natives |
| Versions | `<OCCT version>.<revision>`, e.g. `8.0.1.2` | a version names the OCCT it wraps |
| Tests | NUnit 4 (`Assert.That`, Arrange/Act/Assert), over xUnit v3, TUnit and MSTest | runs on every framework NetOcc targets but net35, which runs NUnitLite on CLR 2 |
| License | MIT for everything NetOcc writes; OCCT stays LGPL-2.1 with its exception | see [Licensing](licensing.md) |

## How netocc-gen reads OCCT

- **A semantic parse.** One libclang translation unit per package, through its module header, with the real preprocessor: no regular expressions rewriting OCCT's macros, and types resolved as the compiler resolves them.
- **Transients by their base.** A class deriving from `Standard_Transient` is handle-managed; no macro scanning.
- **OCCT's own lists.** `bootstrap` reads the toolkit map from OCCT's `MODULES.cmake`, `TOOLKITS.cmake` and `PACKAGES.cmake`.
- **Only what links.** A member is wrapped when it's in the DLLs' export tables (by its mangled name), defined in the headers with calls that link, pure virtual, or a template instance.
- **Committed, deterministic output.** The `.i` files, module headers and value-type structs are committed in netocc-core, declarations only, so netocc-core builds without the generator. OCCT's doc comments go only into the package's IntelliSense file, at pack time.
- **Golden tests** run `generate` over a fake OCCT tree; no test needs OCCT.
- **Every skip has a reason,** in the skip logs and in [Skipped members](skipped.md).

## The typemap library

Hand-written, in `netocc-core/src/SWIG_files/common/`; every generated module includes `NetOcc.i` first.

| File | Covers |
|---|---|
| `NetOcc.i` | the common setup: one SWIG module per package, C# namespaces, the per-module `using`s (`%netocc_csimports`); includes the rest |
| `Types.i` | numbers by value and by reference (`ref`, never `out`), pointers to them as C# arrays, addresses (`IntPtr`), `size_t` and C `long` range checks |
| `Exceptions.i` | every wrapper catches OCCT's and the standard library's exceptions and hands them to C#, which throws `OcctException` after the call |
| `Strings.i` | UTF-8 (`const char*`, `std::string`, `TCollection_AsciiString`) and UTF-16 (`const char16_t*`, `TCollection_ExtendedString`) strings, both ways |
| `Guid.i` | `Standard_GUID` as `System.Guid`, through OCCT 8's `Standard_UUID` |
| `Handles.i` | transients: a proxy owns one reference (`ref`/`unref`), `Handle(T)` and the proxy both ways, `DownCast` |
| `ValueTypes.i` | value types as C# structs, returned through a thread-local buffer; value classes, whose `const&` returns are owned copies |
| `Collections.i` | the NCollection templates and one macro each, which netocc-gen calls per instantiation: indexers, enumerators, bulk copies |
| `References.i` | references members return (C# `ref`s, borrowing proxies that keep their owner) and the keeps of arguments an object refers to |
| `Streams.i` | `std::ostream&` and `std::istream&` as a `System.IO.Stream`, through seekable native memory streams |
| `Std.i` | the standard library types OCCT's signatures use: arrays, optionals, bitsets, pairs, complex numbers |

## Value types

- **Which:** the packages' `value_types` in netocc-generator's `config/modules.yaml` (42 at OCCT 8.0.1), and plain data by itself (101): public fields only, each a number, an enum, a value type or plain data, no bases, no virtuals.
- **Layout:** explicit offsets from libclang. The shim `static_assert`s size, alignment and triviality on every platform, so a struct has one layout everywhere: no `size_t`, `long` or bit fields.
- **Methods in two tiers:** native thunks for everything but trivial math, and managed code only for trivial math written from scratch (accessors, arithmetic, `Distance`). Porting OCCT's inline implementations to C# would make that code an LGPL derivative, and native calls keep OCCT's exact semantics.
- **Returns:** no struct crosses the C ABI by value. On x86 a `__stdcall` export's decoration counts the hidden return pointer, which .NET's signature lacks. A thunk copies its result into a thread-local buffer and returns the address, which C# reads after the exception check.
- **Defaults:** a parameterless constructor calls OCCT (`gp_Dir()` is +Z), but `default(T)` and `new T[n]` still zero-fill: a zero direction is invalid.
- **Bulk data:** `Poly_Triangulation.NodesToArray()` and `TrianglesToArray()` copy a mesh in one call.

## OCCT through vcpkg

- **Manifest mode:** `netocc-core/vcpkg.json` depends on `opencascade` (FreeType, RapidJSON; no FreeImage, TBB or VTK), `vcpkg-configuration.json` pins the registry's git baseline, and an override pins OCCT's version.
- **Nothing global:** `build.py occt` puts the install, buildtree, packages, downloads, registry cache and binary cache under `netocc-core/.vcpkg/`.
- **Overlay triplets:** release builds only, and OCCT's precondition checks on (`BUILD_RELEASE_DISABLE_EXCEPTIONS=OFF`): release OCCT compiles them out by default, and bad input then crashes instead of throwing.
- **Caching:** CI keeps each triplet's binary archives in the Actions cache, pruned to what the install uses and saved when the set changes.
- **The toolkit map** comes from vcpkg's buildtree, which keeps OCCT's CMake lists the install leaves out.
- **Not chosen:** building OCCT ourselves (then FreeType and RapidJSON are ours to build), Open Cascade's installers (Windows only), conda-forge's OCCT (conda in a .NET build).

## Milestones

- **Prototype:** 30 packages written by hand, the typemap library, OCCT through vcpkg on four platforms, 77 tests on every framework, CLR 2 included. It found x86's struct returns, OCAF's tree lifetime and release OCCT's missing checks.
- **Generator:** the prototype's packages generated with the tests unchanged; callability from the export tables; golden tests; companions (`extras/<Pkg>.i`) for what can't be derived.
- **All of five modules:** 344 packages, value types and plain data, collections by OCCT's aliases, maps, streams, reference returns, raw pointers, class template instances, deprecated members, exception classes and standard library types. Skips went from 9,859 to 445, each accounted for.
- **Shipping:** NuGet packages, CI on four runners with vcpkg caches, publishing through Trusted Publishing.
- **Monorepo:** the generator, this site and the demos in one repository; keeping alive what constructors and members keep, all of BRepMesh, Visualization (362 packages), the WPF and Avalonia viewers, OCCT's documentation in IntelliSense.
- **Later:** SWIG's import order fixed at the source (Warning 401), versions from OCCT's, changelog entries with release PRs, returned views keeping their owner, a code review pass (the generator split into partial files, build.py's release rules under unit tests).

## Open

- Public fields of classes that aren't plain data (`BRepGraphInc_VertexDef`'s) aren't wrapped.
- Class template instances of `size_t` are skipped: NetOcc names instances by their canonical types, which spell `size_t` per platform (`BRepGraph_LayerTopoSupplement::AttachedTo`).
- Local builds have no shared vcpkg binary cache; CI uses the Actions cache.

## References

- [SWIG](https://www.swig.org/doc.html): the C# chapter, `ref`/`unref`, typemaps.
- [ClangSharp](https://github.com/dotnet/ClangSharp): libclang for .NET.
- [vcpkg binary caching](https://learn.microsoft.com/vcpkg/reference/binarycaching).
