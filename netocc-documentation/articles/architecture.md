# How NetOcc works

## From headers to packages

```mermaid
flowchart LR
    headers["OCCT 8.0.1 headers"] --> gen["netocc-gen<br/><i>libclang</i>"]
    gen --> iface["interface files (.i)<br/>one per OCCT package"]
    gen --> structs["value-type structs (.g.cs)"]
    typemaps["typemap library<br/><i>handles, strings, streams,<br/>collections, keep-alive</i>"] --> swig
    iface --> swig["SWIG"]
    swig --> cxx["C++ wrappers"]
    swig --> cs["C# proxies"]
    cxx --> cmake["CMake + Ninja"] --> natives["NetOcc&lt;Module&gt; libraries<br/>per platform"]
    cs --> dotnet["dotnet build"]
    structs --> dotnet
    dotnet --> assembly["NetOcc.dll<br/>8 target frameworks"]
    headers -->|doc comments| docs["NetOcc.xml<br/>IntelliSense"]
    natives --> pack["NuGet packages"]
    assembly --> pack
    docs --> pack
```

- **netocc-gen** reads OCCT's headers with libclang and writes an interface file per package: which classes and members to wrap, how their handles, collections and references map. Members that can't be mapped are skipped with a reason.
- **SWIG** turns the interface files into C++ wrapper functions (`extern "C"`, one per member) and the C# proxies that call them.
- **The natives** are one library per OCCT module (`NetOccFoundationClasses`, `NetOccModelingData`, `NetOccModelingAlgorithms`, `NetOccDataExchange`, `NetOccApplicationFramework`, `NetOccVisualization`) plus `NetOccRuntime`, linked against OCCT's `TK*` libraries.

## Packages

```mermaid
flowchart TD
    app[your app] --> netocc["NetOcc<br/><i>NetOcc.dll for net35 ... net10.0,<br/>NetOcc.xml</i>"]
    netocc --> winx64["NetOcc.runtime.win-x64"]
    netocc --> winx86["NetOcc.runtime.win-x86"]
    netocc --> linux["NetOcc.runtime.linux-x64"]
    netocc --> osx["NetOcc.runtime.osx-arm64"]
    winx64 --> files["each: runtimes/&lt;rid&gt;/native/<br/>NetOcc* and TK* libraries,<br/>OCCT's dependencies"]
    winx86 --> files
    linux --> files
    osx --> files
```

## A call

```mermaid
sequenceDiagram
    participant App as your code
    participant Proxy as BRepPrimAPI_MakeBox (C#)
    participant Lib as NetOccModelingAlgorithms
    participant TK as TKPrim (OCCT)
    App->>Proxy: new BRepPrimAPI_MakeBox(10, 20, 30)
    Proxy->>Lib: P/Invoke new_BRepPrimAPI_MakeBox(10, 20, 30)
    Lib->>TK: new BRepPrimAPI_MakeBox(10, 20, 30)
    TK-->>Lib: pointer
    Lib-->>Proxy: pointer, owned by the proxy
    App->>Proxy: Shape()
    Proxy->>Lib: P/Invoke BRepPrimAPI_MakeBox_Shape(pointer)
    Lib-->>Proxy: a new TopoDS_Shape
    Proxy-->>App: TopoDS_Shape proxy
```

Each call crosses into native code once, which costs around 10 ns (`shape.ShapeType()` on x64): nothing against a modeling algorithm, noticeable in loops over millions of nodes, where the bulk copies (`NodesToArray()`, `ToArray()`) help.

## Loading the natives

A module initializer runs before the first call: on .NET it registers a resolver for NetOcc's libraries, on .NET Framework it loads them up front. Both look next to the app:

```mermaid
flowchart TD
    first["first P/Invoke into NetOcc*"] --> probe{"look in"}
    probe --> arch["&lt;app&gt;/x64 or x86<br/><i>.NET Framework, AnyCPU</i>"]
    probe --> rid["&lt;app&gt;/runtimes/&lt;rid&gt;/native<br/><i>.NET</i>"]
    probe --> base["&lt;app&gt;<br/><i>self-contained, single folder</i>"]
    arch --> load["load; OCCT's TK* libraries<br/>resolve from the same folder"]
    rid --> load
    base --> load
```

On Windows each library loads with its own folder first in the search path, so another OCCT on `PATH` doesn't interfere.

## Documentation

`NetOcc.xml`, which IntelliSense shows, is written at pack time from OCCT's doc comments in its headers (LGPL, see the package's NOTICE). It's matched to NetOcc's members by class, member name and parameter names.
