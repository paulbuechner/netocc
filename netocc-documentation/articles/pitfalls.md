# Pitfalls

What went wrong while building NetOcc, and why: traps in exposing OCCT to C#, most of them whatever the wrapper technology (C++/CLI or SWIG and P/Invoke). The rules that avoid them are in the CLAUDE.md files of netocc-core (Gotchas) and netocc-generator (Mapping rules); the decisions they led to are in [Design decisions](design.md), what stays out in [Skipped members](skipped.md).

## Memory and identity

- **Handle lifetime.** `Standard_Transient` objects carry their own reference count. The C# proxy increments it when it wraps the object and decrements it in `Dispose`/the finalizer. Value classes are copied instead.
- **Borrowed references.** A `const T&` return points into its parent; once the parent is collected, the proxy dangles. Return owned copies, or keep a parent reference in the child proxy. The reverse also bites: C++ keeping a raw pointer to a C#-owned object. `new BRepGraph_FaceIterator(graph).Count()` crashed on x86: nothing used `graph` after the constructor, so the GC finalized it mid-enumeration.
- **Identity.** Two proxies can wrap one native object, so `ReferenceEquals` means nothing. Override `Equals`/`GetHashCode`: native pointer for transients, `IsEqual` + `std::hash<TopoDS_Shape>` for shapes.
- **Downcasting.** A C# cast between proxy types never reaches the C++ object. Generate `DownCast` helpers (`Geom_Line.DownCast(curve)`).
- **Finalizer thread.** Finalizers run on the GC thread, in any order. Dispose viewer and GL-backed objects deterministically (`using`). OCAF needs more than that, see [OCAF](#ocaf).
- **What an object keeps must outlive its finalizer.** Holding a kept argument in the proxy's field isn't enough: when both become garbage in one collection, the argument's finalizer may run first, and the object's destructor then uses freed memory (`BRepGraph_MutGuard`'s clears its item in the graph). Hold it in a table until a long weak reference (tracking resurrection) says the proxy is collected. Only for arguments the object can't own: a member's argument may be borrowed from the object, and a strong table would keep both forever.
- **OCCT's doc comments.** libclang attaches `//!` comments to the next declaration (`Cursor.ParsedComment`), trailing `//!<` to the previous one, and parses the Doxygen commands (`@param`, `@return`, `@code`, `\c`). C# parameter names are the generator's safe form of OCCT's (SWIG keeps them), which pins overloads better than types. The text is LGPL: generate it at pack time, ship the license with it, never commit it.
- **Enums without a fixed type differ per ABI.** MSVC makes them `int`, the Itanium ABI (Linux, macOS) `unsigned int` when no value is negative. Parsing on Linux gave `csbase uint` and a different skip log (the golden test failed there only). Only a written type counts (`enum X : T`, `enum class`): libclang has no `isFixed`, the declaration's tokens tell (a `:` before `{`).
- **Linking OCCT's reference manual.** Doxygen names pages after the C++ name: a capital becomes `_` and its lower letter, `_` becomes `__`, `::` becomes `_1_1` (`class_b_rep_prim_a_p_i___make_box.html`); structs are `struct...html`, namespaces `namespace...html`; enums live on their header's page (`gp___trsf_form_8hxx.html`) or their scope's (a class, struct or namespace: `namespace_math_opt.html`). Template instances and alias-named ones have no page: link the template, and for a non-public nested template the class around it. The versioned URLs redirect (301) to occt3d.com, which answers 404 for missing pages: sample links with `curl -L`.
- **`GC` inside `namespace OCC.Core`** is OCCT's package `OCC.Core.GC`, not `System.GC`: runtime code there writes `global::System.GC`.
- **Hosting the OCCT view.** A child window of the app's own class (`CS_OWNDC`, as OpenGL needs) gets the mouse directly; a `static` control passes clicks to its parent (`HTTRANSPARENT`). Avalonia's `NativeControlHost` creates the native control only after the window shows (and again when it moves), so work that needs the view waits for it. `Aspect_VKeyMouse` and `Aspect_VKeyFlags` are anonymous enums: C# repeats their values.
- **SWIG's overload check isn't C#'s.** SWIG ranks overloads by their `typecheck` typemaps; a type without one ranks as a pointer, and two pointer-ranked parameters differ only by C++ type (a handle isn't its class). `char16_t` against `const char16_t*` was ambiguous to SWIG (Warning 516, one dropped silently) though C# tells a char from a string. Comparing by C# signatures instead is too coarse the other way: it took `Start()` for a shadow of static `Start(handle)`.

## Value types (structs)

- **Zero-initialized structs are invalid.** `default(gp_Dir)`, `new gp_Trsf[n]` and unassigned fields are all-zero: a zero direction, a zero scale. They also differ from OCCT's defaults: `default(Quantity_Color)` is black, while `new Quantity_Color()` is OCCT's default color, yellow. A parameterless constructor can't prevent that, so always construct explicitly.
- **Mutating a copy.** `SetX`/`Transform` on a copy (a property result, a `List<T>` indexer, a `foreach` variable) changes only the copy. Prefer the `-ed` variants (`Transformed`, `Translated`) that return a new value.
- **Layout drift.** The C# layout must match C++ on every compiler and OCCT version. Private members rule out `offsetof`, so `static_assert` size and alignment in the shim and run a layout-probe test per RID.
- **Equality.** The default `ValueType.Equals` uses reflection and boxes, so generate `IEquatable<T>`. OCCT's tolerant `IsEqual(other, tol)` is not `Equals`.
- **Ported math is LGPL.** Rewriting OCCT's inline implementations in C# makes that code an OCCT derivative. Keep managed code to trivial math written from scratch, and let the rest call native.

## Calls and data

- **Chatty calls.** Every interop call costs, so walking triangulation nodes one by one is slow. Add bulk helpers that copy a whole mesh into pinned arrays in one call; with struct types it's a single memcpy.
- **Out-params.** C# `out` can't carry an input value, and some OCCT refs are in/out. Map C++ `&` to `ref`, never `out`.
- **Type widths differ per platform:**
  - C `long` is 32-bit on Windows and 64-bit on Linux/macOS; `wchar_t` is 16 vs 32 bits.
  - `size_t` is 32-bit on x86. SWIG maps it to a 32-bit `uint` by default, which is wrong on 64-bit.
  - One managed assembly serves every RID, so the interop layer may use only fixed-width or pointer-sized types.
- **Strings.** Default P/Invoke `char*` marshaling is ANSI on Windows, while OCCT reads paths as UTF-8. `C:\Daten\Übersicht\Teil.step` breaks unless strings are marshaled as UTF-8 explicitly. `TCollection_ExtendedString` is UTF-16 (`char16_t`): pass it as `LPWStr`, which is UTF-16 on every .NET platform, unlike `wchar_t`.
- **SWIG typemap fallback.** For a typemap method missing on `const T&`, SWIG falls back to the `T&` typemap. An `argout` written for `Handle(T)&` out-params got applied to every `const Handle(T)&` parameter and broke unrelated modules. Give every in/out typemap an empty `const T&` twin.
- **C# signature collisions.** Overloads that map to one C# signature don't compile: `const char*` and `const TCollection_AsciiString&` both become `string`. Wrap one of them.
- **Module headers.** OCCT headers often only forward-declare the classes in their signatures, but the handle casts in the wrapper need complete types. The aggregate `_module.hxx` must include the header of every class used in wrapped signatures, not just the package's own headers.

## Errors

- **Exceptions.** Catch `Standard_Failure`, `std::exception` and `...` in the native shim. A C++ exception that escapes through P/Invoke becomes an opaque `SEHException` on Windows and aborts the process on Linux/macOS.
- **Release OCCT skips its own checks.** OCCT's CMake defaults to `BUILD_RELEASE_DISABLE_EXCEPTIONS=ON`, which defines `No_Exception` and compiles every `Standard_*_Raise_if` precondition out of the TK DLLs. Bad input then crashes instead of throwing. Inline header code compiled into the shim still checks, so the same call can behave differently depending on where it's compiled. Build OCCT with the option `OFF` (a per-port block in the vcpkg triplet does it).
- **Null shapes.** OCCT's inline accessors don't check `myTShape`: `ShapeType()` on a null `TopoDS_Shape` is an access violation (0xC0000005) that ends the .NET process, as it crashes C++ (`Handle::operator->` doesn't check). No guard: where C++ crashes, NetOcc may too. Exploring a null shape, `Location()` and `NbChildren()` are safe. The docs warn.
- **Signals.** Don't call `OSD::SetSignal()` in a .NET process. It installs signal/SEH handlers that fight the runtime's own; .NET uses SIGSEGV for `NullReferenceException` on Linux/macOS. A native access violation stays fatal, as in C++, so run crash-prone batch work in a worker process.

## OCAF

- **The tree dies under live attributes.** `~TDF_Data` frees the label nodes, but it never detaches attributes that something else still references. A surviving attribute keeps a dangling label and its whole sibling chain (`myNext`). Releasing it later reads freed memory: `TNaming_NamedShape`'s destructor calls `Clear()` → `Label().Root()`. C++ gets away with it through scope order, but .NET finalizers run in any order, long after `Close`: a `TNaming_Builder` finalizer crashed the test host with an AccessViolation.
  - **Fix:** label, attribute and `TNaming_Builder` proxies each hold one native reference on their `TDF_Data`, and drop it after releasing their own object. The tree then outlives every proxy that points into it.
  - **Hidden holders:** a class that keeps attribute handles in private members needs the same treatment. The generator can't see private members, so these go on a config list.
- **Close through the application.** `TDocStd_Owner` keeps a raw pointer to its document. Only `TDocStd_Application::Close` clears it; the document's destructor doesn't. On a kept-alive tree whose document died unclosed, `TDocStd_Document::Get(label)` returns a dangling document.
- **Undo limit 0 means no transactions.** With the default limit 0, `OpenCommand` opens no TDF transaction and `AbortCommand` rolls nothing back. Call `SetUndoLimit(n > 0)` first.
- **Documents live until closed.** `NewDocument` and `Open` register the document with the application, so dropping the proxy doesn't free it. For `XCAFApp_Application`, a process-wide singleton, that means the whole process lifetime.
- **Status codes, not exceptions.** `Open`/`SaveAs` report failures as `PCDM_ReaderStatus`/`PCDM_StoreStatus`:
  - a missing file gives `PCDM_RS_UnknownDocument`;
  - an unwritable path gives `PCDM_SS_Failure`, with the reason in `SaveAs`'s status-message overload.

  A format exists only after `BinDrivers::DefineFormat(app)` / `XmlDrivers::DefineFormat(app)`.
- **GUIDs.** OCCT 8's `Standard_UUID` (uint32, 2× uint16, 8 bytes) has `System.Guid`'s memory layout, so `Standard_GUID` maps to `Guid` without byte shuffling. Older OCCT declared `unsigned long Data1`, which is 64-bit on Linux, so check the layout per OCCT version.

## 32-bit (x86)

- **AnyCPU often means 32-bit.** .NET Framework AnyCPU exes with Prefer32Bit, or with an inferred x86 RID, run as 32-bit processes. With x64-only natives they get a `BadImageFormatException`. Ship win-x86 and pick the native flavor at runtime (`IntPtr.Size`).
- **Struct returns on x86.** A `__stdcall` export that returns a struct by value is decorated `_Name@N`, where `N` counts the hidden return-buffer pointer. .NET computes `N` from the managed signature, which lacks that pointer, so the call fails with `EntryPointNotFoundException`, on x86 only. Fix: never return structs by value across the C ABI. Copy the result into a thread-local buffer, return its address, and read it in C# after the pending-exception check.
- **Calling convention.** x86 is the only target with two conventions (cdecl/stdcall). A mismatch between an export and its `DllImport` corrupts the stack on x86 only, while every x64/arm64 test passes. Use SWIG's `SWIGSTDCALL` for our own thunks too, and test x86 in CI.
- **Address space.** A 32-bit process gets 2–4 GB. Large STEP assemblies or fine meshes run out of memory, so x86 is for compatibility and x64 for heavy work.

## Build and deployment

- **vcpkg needs system autotools off Windows.** On Linux and macOS, OCCT pulls in fontconfig, whose `gperf` dependency builds with autotools. It needs `autoconf-archive` besides `autoconf`/`automake`/`libtool` from apt or brew. In Docker, a mirror caught mid-sync fails `apt-get update` with "Hash Sum mismatch": fetch only the components you need, and retry.
- **Old frameworks and current tooling.** Traps when adding them:
  - **.NET 6:** VSTest 18 and NUnit3TestAdapter 6 ship only net462 + net8.0 builds. net6.0 tests need Microsoft.NET.Test.Sdk 17.13.0 and NUnit3TestAdapter 5.2.0, the last releases with a netcoreapp3.1 build, pinned per target framework.
  - **.NET Framework 4.6.2:** it has no `System.ValueTuple`, and the package is notorious for binding-redirect trouble on 4.6.x. It also has no `RuntimeInformation`, which arrived in 4.7.1. Keep tuple syntax out of library code, and branch the loader with `#if NETFRAMEWORK`.
  - **`dotnet test --arch`** sets a RID, which before net8.0 implies self-contained (warning NETSDK1201). Set `SelfContained=false` so the tests run on the installed runtimes.
  - **.NET Framework 3.5** is CLR 2, and the modern C# features we use all work there: module initializers, `in` parameters, the `unmanaged` constraint. The gaps are APIs:
    - `Environment.Is64BitProcess` → `IntPtr.Size`;
    - `Path.Combine` with more than two parts → fold two-part calls;
    - `AppContext.BaseDirectory` → `AppDomain.CurrentDomain.BaseDirectory`;
    - `Encoding.GetString(byte*, int)` → `new string(sbyte*, int, int, Encoding)`.

    NUnit 4 and VSTest can't host CLR 2, so a plain net35 console program tests it. The machine needs the .NET Framework 3.5 Windows feature.
  - **.NET 6 on macOS:** install the runtime with `dotnet-install.sh --runtime dotnet --skip-non-versioned-files` into the SDK's dotnet root. Homebrew's `dotnet@6` is a separate keg: the net6.0 test host doesn't find it, and it takes over `/opt/homebrew/bin/dotnet`. Microsoft's .NET 6 SDK pkg replaces the `dotnet` host with the 6.0 one; newer SDKs still run through their own hostfxr.
  - **Testing another library build:** `SetTargetFramework` on a ProjectReference skips the SDK's framework negotiation. That negotiation is also what removes the RID from library references, so without `GlobalPropertiesToRemove="RuntimeIdentifier;SelfContained"` the test gets a RID-specific build instead of the AnyCPU one.
- **PyPI's SWIG wheel doesn't install on MSYS2 Python.** In Git Bash, `C:\msys64\ucrt64\bin\python` can shadow CPython (platform `mingw_x86_64_ucrt`): no `win_amd64` wheels, and venvs get a `bin/` layout. `build.py tools` takes SWIG from conda-forge through micromamba instead, which works under any interpreter.
- **Size and build time.** Wrap per package; a full wrapper is large (NetOcc.dll is about 12.5 MB per framework) and its shims take long to compile and link. Building OCCT itself takes a long time too, so use vcpkg binary caching.
- **Exact OCCT match.** Shims are ABI-bound to the OCCT build they were compiled against. Ship the TK libs with the wrapper and move versions in lockstep, as `find_package(OpenCASCADE <version> EXACT)` does. On Windows, an older OCCT on `PATH` (e.g. installed by another CAD tool) can win the DLL search and crash the app.
- **.NET Framework and native NuGet assets.** net4x projects don't reliably pick up `runtimes/<rid>/native`. Ship a `buildTransitive/net4x/*.targets` that copies the natives (into `x86/` + `x64/` for AnyCPU).
- **No dots in native library names.** In `DllImport("NetOcc.Native")`, `LoadLibrary` treats `.Native` as the extension and won't append `.dll`. .NET Core's probing also tries `<name>.dll`, but relying on each runtime's name variations is fragile. Hence `NetOccFoundationClasses` & co.
- **A Windows DLL exports at most 65,535 functions.** One wrapper library for five OCCT modules reached 71,092 exports and failed with LNK1189. Build one library per OCCT module (`-dllimport` per SWIG module); the linker names the count, so watch it as wrapping grows.
- **Native dependency lookup.** The OS loader, not .NET, resolves the shim's TK dependencies:
  - Put them next to the shim.
  - Load from subfolders with `LoadLibraryEx(..., LOAD_WITH_ALTERED_SEARCH_PATH)`.
  - Set RPATH `$ORIGIN` (Linux) / `@loader_path` (macOS).
  - Test from a clean consumer project on every RID.

## Packaging and CI

- **Symlink chains in native packages.** A Linux/macOS install has `libX.so -> libX.so.8.0 -> libX.so.8.0.1`. NuGet (a zip) and MSBuild copies store every name as a full file, which triples the size. Keep one file per library, named as the loader asks for it: the SONAME (`readelf -d`) or the install name (`otool -D`).
- **Runtime packages need `lib/` placeholders.** A package with only `runtimes/` and `build/net35/` is treated as .NET Framework-only (NU1701 on netstandard2.0, failing restores on net6.0+). Add empty `lib/net35/_._` and `lib/netstandard2.0/_._`; don't add only the `lib/net35/_._` that NU5127 suggests.
- **Backslashes in `PackagePath`.** Packed on Linux or macOS, a trailing backslash (`runtimes\<rid>\native\`) gives zip entries like `runtimes/<rid>/native//libX.so`. NuGet normalizes them on extraction, so restores still work. Forward slashes pack cleanly on every OS, `/` for the package root too.
- **`dotnet` command-line properties.** `-p:Name=a;b` and `-p:Name=a,b` both split into separate properties (MSB1006). An escaped `%3B` survived, but broke restore's sources. Pass lists through an environment variable, which MSBuild reads as a property.
- **Restore sources.** Right after two `dotnet pack` runs, `dotnet restore --source <folder> --source https://api.nuget.org/...` took nuget.org for a path relative to the project (NU1301). Run by hand it worked. A generated `nuget.config` passed with `--configfile` always works.
- **CLR 2 tests.** VSTest can't host CLR 2 and NUnit 4 needs 4.6.2. A net35 test target works as an exe on NUnit 3.14 that runs itself through NUnitLite (`new AutoRun().Execute(args)`). Test code then compiles for net35:
  - no tuples;
  - no `Marshal.SizeOf<T>`;
  - two-part `Path.Combine` only;
  - no `Environment.Is64BitProcess`;
  - a static extension (C# 14) for `Assert.EnterMultipleScope`.
- **vcpkg ABI and triplets.** Removing comments from a triplet didn't change the package ABI: a fresh install restored OCCT from the binary cache. A new vcpkg tool checkout or compiler, as in a rebuilt Docker image, does change it.
- **vcpkg binary cache in actions/cache.** vcpkg's package ABIs also hash the runner image's tools. The windows-2025 image update from 20260907.229 to 20260922.246 changed every ABI, even with the same MSVC 14.51.36231.
  - A key over the repo's vcpkg files, saved only on a miss, then restores useless archives, rebuilds OCCT (about 1 h) and saves nothing, in every run.
  - What works: save under a new key whenever the archive set changed, and restore the newest by prefix.
  - Prune the archives no install uses (`Abi:` in `<install root>/vcpkg/status`); the cache then stays at one set (Windows about 200 MB).
- **Keeping Actions caches alive.** GitHub evicts caches unused for 7 days. Scheduled runs can start late or be dropped at load peaks, so a weekly cron races the eviction: run twice a week, off the full hour. Public repos disable schedules after 60 days without activity.- **`dotnet test` on a solution builds only the test projects** and what they reference (SDK 10): the demos job's `dotnet test NetOcc.Demos.slnx` never compiled the WPF and Avalonia viewers. `dotnet build` the solution, then `dotnet test --no-build`.
- **A multi-target pack runs in the outer build**, which has no default items: `<None Update="README.md" Pack="true" />` finds nothing there and pack fails with NU5039. `Include` it (the inner builds then list it twice, harmlessly).
- **nuget.org READMEs.** nuget.org renders no mermaid and resolves no relative links: a repository README makes a poor package README. Keep a separate one for users, with absolute links, and test that its example is a tested one.
- **License expressions with OCCT's terms.** `MIT AND LGPL-2.1-only WITH OCCT-exception-1.0` packs without NU5124: NuGet's SPDX list has the Open CASCADE exception.
- **nuget.org Trusted Publishing.** The policy takes the workflow file name only (`build.yml`), not its path. The temporary API key lives one hour, so log in right before the push. Only the publish job needs `id-token: write`.
- **`git add` fails on a path that matches nothing.** create-pull-request's `add-paths` listed `netocc-core/changes`, which git doesn't have while no entries wait (it keeps no empty folders), so the first release PR, made from `[Unreleased]` without entries, failed there. List the folder only when `git ls-files` finds something in it.

## Generating from headers

- **Scaling to all packages.** What broke going from 30 to 344 packages:
  - **libclang stops at a fatal error.** A missing include (`GeomBndLib_InfiniteHelpers.pxx`, not installed) ends the parse silently, and the headers after it look fine. Retry without the header that led there.
  - **Header bugs hide in function bodies.** `MathLin_Jacobi.hxx` sets a member `EigenResult` doesn't have. With `SkipFunctionBodies`, clang doesn't see it; MSVC does, in every wrapper that includes it.
  - **Destructors mangle differently.** For MSVC, `clang_Cursor_getMangling` names a destructor `??_D` (vbase destructor), while DLLs export `??1`. `clang_Cursor_getCXXManglings` lists all variants.
  - **`%nodefaultdtor` plus a constructor crashes.** The owning proxy's `Dispose` throws `MethodAccessException` on the finalizer thread, which kills the process.
  - **C++ ambiguity counts private overloads.** `BOPAlgo_ParallelAlgo` has a public `Perform()` and a private `Perform(const Message_ProgressRange& = {})`; overload resolution comes before the access check.
  - **Out-of-line nested classes** (`class BRepGraph::ShapesView { ... };` at file scope) look like top-level classes unless the semantic parent is checked. Visit them with their class instead.
  - **SWIG constructors with `ref` + `pre`/`post` typemaps** call a `SwigConstruct` helper without `ref`, unless the typemap has `cshin="ref $csinput"`.
  - **Namespace-function defaults** (`= THE_ZERO_TOL`) only resolve inside the namespace; put the shim struct there.
  - **Collection aliases live outside the packages.** OCCT 8 moved them to `src/Deprecated/NCollectionAliases/` (972 headers), so a package's own parse never sees `TColgp_Array1OfPnt`. Parse each package's alias headers with it, typedefs only. `TColgp`, `TColGeom` and `TColGeom2d` aren't packages anymore.
  - **Typemaps are positional.** A module's own collection macros after its classes left `SWIGTYPE_p_*` types in that module only, since the others import it first, and its `const&` returns got no owned-copy typemap. Emit them before the classes; `build.py generate` now fails on any `SWIGTYPE_p_*`.
  - **Vtables of classes the DLLs don't export.** An inline constructor of a polymorphic class makes MSVC emit the vtable in the shim, and the vtable names every virtual function. One that isn't exported fails to link (LNK2001, `ShapeAnalysis_BoxBndTreeSelector::Reject`). An exported constructor sets OCCT's own vtable.
  - **Unchecked NCollection accessors.** `NCollection_Array2::Value` checks only the flat position: a column past the end reads the next row. `Array1::First`/`Last` read index 0 of an empty array. Check both in `%extend`.
  - **Import cycles reach the module itself.** Four Step modules imported themselves through a package cycle. Keep the module out of its own import closure.
  - **Template arguments are canonical.** libclang spells every argument of a specialization, defaults included: `NCollection_Map<TDF_Label, NCollection_DefaultHasher<TDF_Label>>`. Spell the `%template` the same way, and skip platform-spelled builtins (`size_t` is `unsigned long long` on Windows, `unsigned long` on Linux).
  - **Commas in SWIG macros.** A macro argument ends at a comma, template brackets or not. Pass `NCollection_DataMap< K, V, H >` on to another macro as `%arg(...)`, and don't instantiate collections whose arguments hold commas.
  - **Streams without callbacks.** A `std::streambuf` that calls into C# per write breaks on managed exceptions (fatal across native frames off Windows) and on `seekp`/`tellg`, which binary writers and readers use. Buffer in native memory, seekable, and copy the bytes before or after the call.
  - **References into an object need its owner.** A proxy of a `T&` return points into the owner; without a managed reference to the owner's proxy, the GC finalizes the owner and the borrowed proxy writes into freed memory. Give every proxy an owner field and set it in the `csout` of `SWIGTYPE&`, members only. A number or struct `T&` is a C# `ref` return (`ref *(T*)ptr`), which also runs on CLR 2. Never for value-type thunks: `this` is a C# struct the GC may move.
  - **Implicit constructors aren't declarations.** With a global `%nodefaultctor`, a class that declares no constructor (`BRep_Builder`) had none in C#. Emit the implicit `T()`, checked like an inline one.
  - **Generated on Windows, built everywhere.** Headers that compile only on Windows reach the output: `OSD_WNT.hxx` includes `windows.h`, and the Linux build stops there. Leave out OCCT's WNT headers; portable ones guard `windows.h` with `_WIN32`, so an include-based rule would drop `Standard_Mutex.hxx`.
  - **Wrapping more exposes header bugs.** Once `HLRTopoBRep_DSFiller::Insert` mapped (a map parameter), its module header included `Contap_Contour.hxx`, which uses `Adaptor2d_Curve2d` without including it. A `prelude` on the declaring package fixes every user.
  - **Flat names can be taken.** Nested and namespace types get flat names (`Geom_Curve::ResD1` as `Geom_Curve_ResD1`), but `BVH::RadixSorter` sits next to the class template `BVH_RadixSorter` at file scope. Check every flat name against the file-scope names of the translation unit.
  - **A C# import loses to a sibling namespace.** The class `BRepGraph` (package BRepGraph) is used from `OCC.Core.BRepGraphInc`. With `using OCC.Core.BRepGraph;` above a file-scoped namespace, `BRepGraph` resolves to the namespace `OCC.Core.BRepGraph` first (CS0118). Usings inside the namespace are searched before the enclosing namespaces, which is where SWIG puts them.
  - **libclang sign-extends enumerators.** `InitVal` of `Bits_Reserved = 0xF000` in `enum BitLayout : uint16_t` is -4096; read `UnsignedInitVal` for unsigned underlying types, and give the C# enum that base (`%typemap(csbase)`).
  - **One layout for every target.** libclang computes layouts for win-x64, where `size_t` is `unsigned long long`. A plain-data struct holding one (`MathUtils_PolyResult`) failed its layout guard on x86. The canonical type can't tell; walk the typedef sugar for `size_t`, `ptrdiff_t` & co., and refuse `long`, `wchar_t` and `long double`.
  - **Parsers in public headers.** StepFile installs its bison parser and flex scanner (`step.tab.hxx`, `namespace step`), whose members don't all link. Exclude such namespaces in config.
  - **Uninstantiated templates have no members.** A class template instance exists in the AST with its members only once something instantiates it, and libclang can't. `using BRepGraph_FaceIterator = BRepGraph_Iterator<...>;` alone gives an empty declaration. A second parse with `static_assert(sizeof(::Alias) != 0)` after the includes instantiates the class, not its member definitions.
  - **Generated files outlive their types.** A struct or nested header the generator no longer writes stays in the output and still compiles into the build. Delete the generator's own outputs it didn't write in a run.
  - **Out typemaps reach constructors.** SWIG's `new_T` wrapper returns `T*` through the out typemap of `T*`. One that adds a reference for returned transients, as a handle return does, counted twice with the `ref` feature: new objects started at 2 and leaked. Put such typemaps on a spelling constructors never use (`T* const`).
  - **Pointers to width typedefs.** libclang gives canonical types, so `intptr_t*` is `long long*` on win-x64 but `int*` on x86, and `uint64_t*` is `unsigned long*` on Linux. By value that converts implicitly; a pointer doesn't, and the x86 build failed. Spell pointers to typedefs as written, and give SWIG no typedefs for them: it writes the resolved type into the wrappers (`NetOcc_Address<const unsigned long long*>`), which broke Linux. Typemaps can name the typedefs instead.
  - **Declared here, defined there.** A translation unit may only forward-declare a class another package defines (`GeomEval_RepCurveDesc::Base` in Geom). It is still that package's proxy. Only a class no header defines (`TNaming_Node`, `Message_ProgressScope::NullString`) is opaque: a pointer to it is an address.
  - **Pinned arrays dangle when kept.** A C# array is pinned for the call only, and constructors and setters keep pointers (VrmlData nodes, `BRepGProp_UFunction`'s coefficients). Such pointers are addresses the caller allocates natively.
  - **MSVC: `reinterpret_cast` from `void**` to `const int**`** counts as casting away constness ("conversion loses qualifiers"). Go through `void*` with `static_cast`.
  - **`$1_ltype` drops qualifiers.** For a `const char*&` parameter SWIG declares `char** arg`; a typemap that assigns a `const char**` needs a cast.
  - **SWIG preprocesses `%extend` bodies.** Its preprocessor doesn't define `_WIN32`, so `#if defined(_WIN32)` in an `%extend` body always took the X11 branch (Windows then failed on `Xw_Window`). Platform dispatch goes into a `%{ %}` helper the body calls.
  - **An empty value class needs its copies too.** `%occt_valueclass` was only emitted for classes with constructors or instance members; `Graphic3d_ValidatedCubeMapOrder` has neither, so a static member's `const&` return of it took the borrowing `csout`, whose `ret.netoccOwner = this` doesn't compile in a static method.
  - **`SWIGTYPE_T`, not just `SWIGTYPE_p_T`.** A by-value type without typemaps becomes `SWIGTYPE_T`; the build's guard looked for `SWIGTYPE_p_` only and let one through (a kept-argument typemap overriding an address).
  - **A class-scope typedef's spelling needs its class.** Naming an instance through a typedef in another class (`OpenGl_Context::OpenGl_ResourcesMap`) makes the alias need that class's header; the includes of the template and arguments don't bring it. A package parsed whole while its config lists a few classes leaked such names: filter at the parse.
  - **Typedefs that differ per platform.** Resolving `Aspect_Drawable` (`void*` on Windows, `unsigned long` on X11) on the Windows parse writes a type no other platform converts: keep the name, map it pointer-sized.
  - **SWIG's overloads count the object.** An instance member and a static overload taking the object first and the same arguments collide in SWIG's wrappers (Warning 516), and SWIG keeps the static one: skip the instance member, logged.
  - **Bit fields have no portable layout.** `MeshVS_TwoColors` packs six bit fields into 8 bytes on MSVC; libclang's field offsets said 9. A record with bit fields isn't plain data.
  - **A delegating constructor imports the vtable.** MSVC emits a polymorphic class's vtable in the shim for an ordinary inline constructor, but for an inline delegating one it references the vtable the DLL should export (the class's virtual functions are exported). OCCT doesn't export `Select3D_SensitiveCircle`'s vtable: LNK2019. The export tables tell; a first rule that treated every inline constructor this way wrongly dropped 443.
  - **A constructor's `pre`/`post` run in a static helper.** SWIG converts a constructor's arguments in `SwigConstruct<Class>`, before the proxy exists: no `this`. Hand values to the constructor body (`csconstruct`) through a thread-static, as `NativeKeep` does.
  - **`%apply`/`%clear` work inside a class body.** Around one declaration they scope a type-and-name typemap to it alone (`NETOCC_KEEP`).
  - **CLR 2 writes `bool[]` back as four-byte BOOLs.** `[In, Out]` with `MarshalAs(LPArray, ArraySubType = U1)` round-trips on .NET Framework 4 and .NET, but not on CLR 2: copy through a pinned `byte[]`.
  - **SWIG's argument checks return early.** A null class reference (or a failed `in` check) returns from the wrapper before the later parameters' `in` typemaps run. C# `post` code must not trust what those would have written: a sentinel a later `in` parks in a `ref` slot never gets there. Compare the slot with what C# passed instead.
  - **A C# `char` goes through a code page.** SWIG maps C++ `char` to C# `char`, which P/Invoke converts with the ANSI code page: a UTF-8 lead byte comes back as `Ã`. `%apply unsigned char { char }` passes the byte.
  - **Typemap code runs outside `%exception`'s try.** An `in` typemap that must fail (a C `long` out of range) sets the pending exception and returns `$null`; a C++ throw there would leave the `extern "C"` wrapper.
  - **C++17 elides a copy into `new`.** `new T(f())` constructs a non-copyable, non-movable `T` that `f` returns by value in place; an `%extend` accessor so gives C# a proxy SWIG's by-value return (which copies) can't.
  - **A typemap's locals belong to one type.** In `%typemap(in) const A&, A& (A temp)` the local is `A&`'s only, and the `const A&` wrapper doesn't declare `temp`: give each type its list, `const A& ($*1_ltype temp), A& ($*1_ltype temp)`.
  - **C# member variables use `csvarout`, not `csout`.** A `std::pair` with `first`/`second` as members would miss the value-type, string and bitset return typemaps; `%extend` accessors returning by value reuse them.
  - **`%arg()` normalizes spaces.** A type passed through `%arg(NCollection_Map< K, H >::Iterator)` comes out as `NCollection_Map< K,H >::Iterator` in the wrapper: harmless, but a byte-for-byte diff of SWIG's C++ shows it. Macros work inside a template's `%extend`, which is expanded before parsing.
  - **`%csattributes Class` marks the constructor.** The class name also names its constructor, and the feature lands there; a class attribute takes `%typemap(csattributes) Class`. A member's feature matches by parameters and `const`, and reaches the default-argument overloads only when it spells the defaults: write it as the declaration.
  - **Template names aren't the text before `<`.** `NCollection_DynamicArray<T>::DynamicIterator<true>` is an instance of the nested template, not of `NCollection_DynamicArray`: cut the last argument list only. Cutting at the first `<` gave the iterator the array's name, and the wrappers didn't compile.
  - **Member bodies of an instance aren't compiled until used.** `IntPolyh_Array<T>::Dump` calls `T::Dump()`, which some `T` lack; libclang never instantiates it, the wrapper does. An explicit instantiation (`template class X<T>;`) in a check parse instantiates every member: lay each error at the member whose body holds it or its instantiation note. The note at the explicit instantiation's line tells which instance.
  - **Their vtables too.** A polymorphic instance the DLLs don't export gets its vtable in the shim once a constructor is wrapped, and an instantiated virtual member may call an unexported function (`ShapePersistent_Poly::pPolygon2D::Import`, LNK2019). Uninstantiated, the virtual's body names no callee; check the vtable in the check parse.
  - **`const char*` string overloads read Latin-1.** `TCollection_ExtendedString(const char*, bool isMultiByte = false)` copies each byte to a character, so a UTF-8 C# string through a `const char*` overload of an ExtendedString API comes out as mojibake. Where overloads collide in C#, wrap the UTF-16 one (`TCollection_ExtendedString`, `const char16_t*`).
  - **Const twins aren't interchangeable for classes.** The const one returns an owned copy, the non-const one a borrowed proxy; a copy of a view (`BRepGraph::ShapesView`) holds a raw pointer, a borrowed iterator item dangles once the list goes. Prefer only `ref` returns of values.
  - **Copies that point at their owner.** A value class whose fields point at other objects (`BRepGraph_TopoView` holds its graph), returned by value or `const&`, is a copy the proxy owns, and it dangles once the owner is collected: the tests needed `GC.KeepAlive(graph)`. Mark such returns so the copy keeps the member's object (`NETOCC_VIEW`), the moved result of a move-only class too. SWIG looks up a return typemap by type and function name, so `%apply ... { T Member }` around the one declaration scopes it.
  - **A default a macro spells has no tokens where it is.** `DEFINE_STANDARD_EXCEPTION` writes `C1(const char* theMessage = "")`; tokenizing the default's extent gave nothing (the macro in another header) or garbage from the macro body to the expansion (the same header). Spelling and file locations differ there: evaluate the constant instead (`clang_Cursor_Evaluate`), and only for parameters a literal fits, since shims compile the default.
  - **Default arguments of instance members stay uninstantiated.** `DefaultArg` is null, `UninstantiatedDefaultArg` isn't: without it, `IntPolyh_Array(int = 256)` and `IntPolyh_Array(int, int = 256)` looked unambiguous, and the call wasn't.
  - **SWIG writes `enum X` in `%extend` casts.** A template instance over a nested enum (a flat alias) gets `(T< enum X > const *)self` for `%extend` members, and C++ declares a new local enum there; template arguments of `NetOcc_Address< enum X * >` too. Keep `%extend` out of templates that may hold enums, and spell pointers to nested enums by C++'s name.
  - **`SwigValueWrapper` moves only for C++11.** It picks its move operators by `__cplusplus`, which MSVC reports as 199711L without `/Zc:__cplusplus`: a move-only result (a guard) didn't compile. OCCT's headers use `_MSVC_LANG`, so the flag changes only SWIG's code.
  - **Instances of protected templates.** C++ can't name `ShapePersistent_Geom::geometryBase<Geom_Curve>` outside its class, but OCCT's public typedef `ShapePersistent_Geom::Curve` can; alias through it. Its flat name may be a real class's (`ShapePersistent_Geom_Curve`): check file-scope names.
  - **Names past the path limit.** A flattened instance name reached 250 characters (`Extrema_GGExtPC<...>`), and SWIG couldn't write its `.cs` file. Prefer OCCT's alias (`Extrema_ExtPC`), and hash the arguments past a length.

- **Declared, never defined.** OCCT declares a few members `Standard_EXPORT` and never defines them: `XCAFDoc_GeomTolerance(const Handle(XCAFDoc_GeomTolerance)&)` and `BRepGProp_VinertGK::GetAbsolutError`. The headers can't tell; the shim compiles and then fails to link. Check every out-of-line member against the TK DLLs' export tables by its mangled name. libclang's MSVC mangling matches the DLLs.
- **Headers that don't compile alone.** `XCAFDoc_AssemblyIterator.hxx` holds a `TDF_Label` by value without including it. In `_module.hxx`, include the dependency headers before the package's own; parse such a package with the missing header first.
- **`using` re-exposes protected bases.** `BRepAlgoAPI_Algo` inherits `BOPAlgo_Options` protected and republishes `HasErrors` & co. with `using`. A parser that reads only a class's own method declarations loses them; follow the using-shadow declarations (ClangSharp: `UsingShadowDecl.UnderlyingDecl`).
- **Placement-only allocation.** NCollection-allocated nodes (`Poly_CoherentTriPtr`) declare `operator new(size_t, const Handle(NCollection_BaseAllocator)&)` and a matching `operator delete`. Those hide the global forms, so the shim's `new T` and `delete p` don't compile. Detect classes whose own operators lack the usual forms; give them no constructors, `%nodefaultdtor`, and no by-value copies.
- **Hand-written parts next to generated ones.** SWIG 4.5 applies `%extend` (including `%proxycode`) and `%csmethodmodifiers` written before the class declaration. One companion include placed before the generated classes is enough. `%template` may come before a class of the same module that it holds, since SWIG resolves class names after parsing; typemaps can't, since they apply only to later declarations.
- **ClangSharp traps:**
  - `IsImplicit` is libclang's cursor flag, not "declared by the compiler"; use `IsDefaulted && !IsExplicitlyDefaulted`.
  - `CXXRecordDecl.Methods` leaves out the constructors; read `Ctors`.
  - `SpecializedTemplate` throws on partial specializations (std internals); take the template name from `QualifiedName`.
  - The MSVC STL's `std::array<T, N>` is a struct around `_Elems`; flattened member by member it gives `Roots__Elems_0`. Treat it as the `T[N]` it holds.
  - The NuGet libclang (`libclang.runtime.linux-x64`) comes without clang's own headers, and on Linux the C library has no `stddef.h`: every `<cstddef>` fails there. On Windows the SDK has one. A fatal error in a system header is the toolchain's, not the header's that reached it: blamed on OCCT's headers one by one, it left out nearly every header, and the docs run documented 281 of 6,946 types in 22 minutes (on Windows 6,220, in about a minute).

## SWIG imports

- **Warning 401 from import order.** SWIG drops a base class it meets after the derived class. With nested `%import`s (each imported module importing its own dependencies) and package uses that form cycles, classes of imported modules keep meeting their bases late: 9622 warnings. Harmless for imported classes (no code), but a module class deriving from such a class loses the chain: missing `new` modifiers. Fix: one flat list per module (`#ifndef SWIGIMPORTED` around the imports), bases' packages first. Left over: imported packages deriving from the module's own classes, which come after the imports.
- **Checking SWIG output changes.** SWIG rewrites every file; compare with a copy before regenerating, and give identical `.cxx` files their old timestamps back, or ninja rebuilds all natives for nothing.

## OCCT 8 API changes

- **`Standard_Failure` derives from `std::exception`**, no longer from `Standard_Transient`. The type name comes from `ExceptionType()`; there is no `DynamicType()` on it anymore.
- **Collection aliases are deprecated.** `TopTools_ListOfShape`, `TDF_LabelSequence` & co. live in `Deprecated/NCollectionAliases`, and signatures use `NCollection_List<TopoDS_Shape>` / `NCollection_Sequence<TDF_Label>` directly. Wrap the template; keeping the old name for the C# class is fine. Only one instantiation has two aliases, so the alias's prefix is a stable home for the C# class.
- **Signatures use `occ::handle<T>`, `double`, `bool`** instead of `Handle(T)`, `Standard_Real`, `Standard_Boolean`. `occ::handle` is an alias of `opencascade::handle`, so either spelling works in `.i` files.
- **Namespace functions.** `TopoDS::Face()` & co. are namespace functions now; wrap them through a static shim class.

## Documentation (DocFX)

- **Examples as test regions.** `[!code-csharp[](../samples/X.cs#name)]` takes a `#region name` and dedents it; a file outside the docfx content globs is fine. Tests keep the examples honest, and asserts after the region stay out of the page.
- **Nested toc, flat files.** Sections in `articles/toc.yml` (`items:`) without moving files keep every URL and relative link.
- **Mermaid.** The modern template renders ```` ```mermaid ```` blocks. `mmdc` (`@mermaid-js/mermaid-cli`) validates them; with `PUPPETEER_SKIP_DOWNLOAD=1` and a puppeteer config whose `executablePath` names a local Chromium (EdgeCore's `msedge.exe` works) nothing is downloaded. Rendered PNGs show what parsing doesn't: dagre routes a tree's edges through sibling boxes when the children's order is unlucky; reorder them. Class diagrams draw empty compartments: trees read better as `flowchart LR` with `---` edges.

## Code style

- **clang-format's C# mode can't take modern C#.** With OCCT's `.clang-format` it split raw string literals (`"""`), changing the program, and mangled switch expressions and property patterns. The C# style comes from `.editorconfig` instead, applied by ReSharper's formatter (`python format.py`).
- **CSharpier re-sorts usings.** It merges the commented using groups into one sorted list, own namespaces before third-party ones, with no option to keep them.
- **ReSharper's cleanupcode formats only solution files.** Given a project file, it formats the project file itself. Given a solution, it skips files outside the solution's folder and projects that don't load: the samples and package tests need the packed NetOcc. A temporary project over every file, in a solution at the root, needs no restore of the real ones. It needs the libraries' targets too: a branch no target defines (`#if NETFRAMEWORK` under net10.0 alone) stays unformatted.
- **OCCT's wrapping reads badly in C#.** One argument per line (`BinPackArguments: false`), and names aligned in columns across `var`, `using var` and modifiers, put wide gaps and tall lists into ordinary code; packed lines and single spaces read better. ReSharper also adds blank lines by default after a file header comment (splitting a using group from its comment), between using groups, around fields and around regions, which the documentation's snippets show.

## Licensing

- **OCCT.** LGPL-2.1 with the Open CASCADE exception. Object code that incorporates OCCT header material may be distributed "under terms of your choice", given a prominent notice that it uses OCCT. The TK libs themselves stay LGPL: keep them separate and replaceable, ship the license texts, and publish the exact source, including vcpkg's patches.
- **Doc comments.** Doc text copied from OCCT headers is OCCT's text. Keep it out of MIT sources and ship it only in the XML docs, with attribution.
- **Other OCCT bindings.** Their generators and interface files are GPL-3.0 or LGPL-3.0. Use them as reference for rules at most; never copy their code or exclusion lists, or the generator becomes GPL.
