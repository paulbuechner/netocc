# SPDX-FileCopyrightText: 2026 Paul Büchner
# SPDX-License-Identifier: MIT

"""Writes articles/skipped.md, the members NetOcc leaves out: the accepted cases, curated below (each with the member labels
it covers), and the groups the generator's skip logs sort by reason. netocc-gen generate writes the logs
(netocc-generator/log/skips, not committed), so run this after a generate and commit the page. Fails without writing it,
listing them, when a skip entry is neither accepted nor in a group (say why it's left out, or wrap it), or when an accepted
case covers no entry anymore (drop it)."""
import re
import sys
from collections import defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
SKIPS = HERE.parent / "netocc-generator" / "log" / "skips"
PAGE = HERE / "articles" / "skipped.md"
HEADERS = "Headers left out"

# category -> [(members as shown, reason, regex of the member labels it covers)]
ACCEPTED = {
    "Raw pointers": [
        ("`gp_XYZ::GetData`, `gp_XYZ::ChangeData`",
         "A pointer into the struct. A C# struct is pinned for the call only, and the GC may move it after; its fields hold the data.",
         r"gp_XYZ::(GetData|ChangeData)"),
        ("`BinObjMgt_Persistent::GetOStream`, `GetIStream`",
         "The persistent's stream exists only while a storage driver pastes. `SetOStream` keeps a stream, which C# lends for a call only, "
         "and without one OCCT dereferences a null stream.",
         r"BinObjMgt_Persistent::(GetOStream|GetIStream)"),
        ("`Image_AlienPixMap::GetPalette` (excluded in the config)",
         "Declared only on Windows without FreeImage: a WIC palette (`IWICPalette*`). Generated on Windows, it breaks the other "
         "platforms' builds.",
         r"Image_AlienPixMap::GetPalette"),
        ("`Standard_CLocaleSentry::GetCLocale`",
         "The C runtime's locale handle: `_locale_t` on Windows, `locale_t` elsewhere. Generated on Windows, the type doesn't exist on "
         "Linux.",
         r"Standard_CLocaleSentry::GetCLocale"),
        ("`Standard_Type::Register`", "Takes a `std::type_info&`: C++ RTTI, which C# can't produce.", r"Standard_Type::Register"),
    ],
    "Class template instances": [
        ("`RWGltf_GltfOStreamWriter::RWGltf_GltfOStreamWriter`",
         "Takes rapidjson's `BasicOStreamWrapper<std::ostream>`, a C++ template of a third-party library that C# can't create.",
         r"RWGltf_GltfOStreamWriter::RWGltf_GltfOStreamWriter"),
        ("`IntPolyh_ArrayOfPointNormal::Dump`, `IntPolyh_ArrayOfEdges::Dump`, `IntPolyh_ArrayOfTriangles::Dump`",
         "`IntPolyh_Array<T>::Dump` calls `T::Dump()`, which these `T` lack or take arguments for: the member doesn't compile in C++ "
         "either.",
         r"IntPolyh_ArrayOf(PointNormal|Edges|Triangles)::Dump"),
        ("`BRepGraph_LayerTopoSupplement::AttachedTo`; `Image_PixMap`'s 3D sizes (`SizeXYZ`, `InitWrapper3D`, `InitTrash3D`, "
         "`InitZero3D`, `Image_PixMapData::Init`); `Aspect_WindowInputListener::TouchPoints`",
         "A class template instance of `size_t` (`NCollection_LinearVector<size_t>`, `NCollection_Vec3<size_t>`, a map keyed by "
         "`size_t`). NetOcc names instances by their canonical types, which spell `size_t` per platform (`unsigned long long` on "
         "Windows, `unsigned long` elsewhere); keeping the sugar would take a parser change.",
         r"BRepGraph_LayerTopoSupplement::AttachedTo|Image_PixMap(Data)?::(Init|SizeXYZ|InitWrapper3D|InitTrash3D|InitZero3D)"
         r"|Aspect_WindowInputListener::TouchPoints|NCollection_Vec3<unsigned long long>"),
        ("`NCollection_UtfString<wchar_t>` and the `ToUtfWide` members returning it",
         "`wchar_t` is 16 bits on Windows and 32 elsewhere: no C# type has both widths. `NCollection_UtfString<char16_t>` and "
         "`<char32_t>` hold UTF-16 and UTF-32.",
         r"NCollection_UtfString<wchar_t>|\w+::ToUtfWide"),
        ("`Graphic3d_Layer::Structures`, `NonCullableStructures`, `ArrayOfStructures`; `Graphic3d_BvhCStructureSet::Structures`, "
         "`Graphic3d_BvhCStructureSetTrsfPers::Structures`; `Graphic3d_Structure::Network`; `Graphic3d_StructureManager::DefinedViews`, "
         "`RecomputeStructures`",
         "Collections of raw pointers to the structure manager's structures and views: no C# collection holds raw pointers. C# reaches "
         "structures through their presentations (`PrsMgr`, `AIS`).",
         r"Graphic3d_(Layer|BvhCStructureSet|BvhCStructureSetTrsfPers|Structure|StructureManager)::"
         r"(Structures|NonCullableStructures|ArrayOfStructures|Network|DefinedViews|RecomputeStructures)"),
        ("`BRepMesh_Classifier::RegisterWire`",
         "Takes `NCollection_Sequence<const gp_Pnt2d*>`: raw pointers to the caller's points, which no C# collection holds (a C# "
         "struct has no fixed address).",
         r"BRepMesh_Classifier::RegisterWire"),
        ("`TNaming_UsedShapes::Map`",
         "The attribute's map to the raw `TNaming_RefShape` pointers it owns: no C# collection holds raw pointers.",
         r"TNaming_UsedShapes::Map"),
        ("`MoniTool_Timer::Dictionary`",
         "A map keyed by C strings (`const char*`) whose storage OCCT owns: no C# collection holds them.",
         r"MoniTool_Timer::Dictionary"),
        ("`BVH::RadixSorter`", "Its flat name, `BVH_RadixSorter`, is the file-scope template's too.", r"BVH::RadixSorter"),
    ],
    "Standard library types": [
        ("`OSD_FileSystem`, `OSD_CachedFileSystem`, `OSD_FileSystemSelector`, `OSD_LocalFileSystem`: `OpenIStream`, `OpenOStream`, "
         "`OpenStreamBuffer`, `IsOpenIStream`, `IsOpenOStream`; the `OSD_IStreamBuffer`, `OSD_OStreamBuffer`, `OSD_IOStreamBuffer` "
         "constructors; `RWGltf_GltfJsonParser::SetStream`; `RWPly_PlyWriterContext::Open`'s stream (a call opens the file)",
         "A `std::shared_ptr` to a native stream or stream buffer: OCCT shares it and keeps it past the call, while NetOcc lends a C# "
         "stream for one call (Streams.i) and gives C# no view of OCCT's streams.",
         r"OSD_(FileSystem|CachedFileSystem|FileSystemSelector|LocalFileSystem)::(OpenIStream|OpenOStream|OpenStreamBuffer|IsOpenIStream"
         r"|IsOpenOStream)"
         r"|OSD_(I|O|IO)StreamBuffer::OSD_(I|O|IO)StreamBuffer|RWGltf_GltfJsonParser::SetStream|RWPly_PlyWriterContext::Open"),
        ("`BinTools_IStream::Stream`, `Message_PrinterOStream::GetStream`",
         "A mutable reference to a native stream: C# has no view of OCCT's streams (Streams.i).",
         r"BinTools_IStream::Stream|Message_PrinterOStream::GetStream"),
        ("`Message_Messenger_StreamBuffer::Stream`",
         "The `std::stringstream` C++'s `<<` writes into, sent when the buffer goes; C# sends text with `Message_Messenger.Send`.",
         r"Message_Messenger_StreamBuffer::Stream"),
        ("`BRepGraphInc_Storage::CurrentShapesMutex`, `DE_Wrapper::GlobalLoadMutex`, `Aspect_VKeySet::Mutex`, "
         "`SelectMgr_BVHThreadPool_BVHThread::BVHMutex`, `StdPrs_BRepFont::Mutex`",
         "A `std::mutex`/`std::shared_mutex` for C++ code to lock around its own work: C# can't hold a C++ lock, which lives within one "
         "call.",
         r"BRepGraphInc_Storage::CurrentShapesMutex|DE_Wrapper::GlobalLoadMutex|Aspect_VKeySet::Mutex"
         r"|SelectMgr_BVHThreadPool_BVHThread::BVHMutex|StdPrs_BRepFont::Mutex"),
        ("`Select3D_SensitivePrimitiveArray::GetVertex`",
         "Returns `std::array<NCollection_Vec3<float>, 3>`: Std.i's arrays hold numbers, not structs.",
         r"Select3D_SensitivePrimitiveArray::GetVertex"),
        ("`MathUtils_Polynomial(std::initializer_list<double>)`, `MathUtils_Rational(std::initializer_list<double>, ...)`",
         "Only the compiler builds a `std::initializer_list`, from a braced list; the `math_Vector` constructors take the same "
         "coefficients.",
         r"MathUtils_Polynomial::MathUtils_Polynomial|MathUtils_Rational::MathUtils_Rational"),
        ("`Standard_ErrorHandler::Error`, `Standard_ErrorHandler::Label`",
         "`Standard_ErrorHandler` is the C++ side of OCCT's signal handling (`OCC_CATCH_SIGNALS`): a `std::variant` of signal exceptions "
         "and "
         "a setjmp buffer. C# gets OCCT's errors as `OcctException`.",
         r"Standard_ErrorHandler::(Error|Label)"),
    ],
    "Wrapped by hand": [
        ("`ShapeProcess::ToOperationFlag`",
         "Returns `std::pair<ShapeProcess::Operation, bool>`. A pair's accessors are `%extend`, which SWIG casts with "
         "`enum ShapeProcess_Operation`, and a nested enum's flat alias can't follow that. The companion `extras/ShapeProcess.i` "
         "wraps it: `ShapeProcess.ToOperationFlag(name, ref known)`.",
         r"ShapeProcess::ToOperationFlag"),
    ],
    "References and overloads": [
        ("`Font_Rect::TopLeft`, `TopRight`, `BottomLeft`, `BottomRight` (the overloads taking a vector)",
         "They fill the vector they're given and return it by reference, into a C# struct; C# gets the overloads without it, which "
         "return the value.",
         r"Font_Rect::(TopLeft|TopRight|BottomLeft|BottomRight)"),
        ("`NCollection_Mat3<T>::Map`, `NCollection_Mat4<T>::Map` (`BVH_Mat4d`, `BVH_Mat4f`)",
         "A static function viewing a raw array as a matrix, by reference: memory C# doesn't own. The matrices' members and "
         "constructors take the values.",
         r"(BVH_Mat4[df]|NCollection_Mat3_(double|float))::Map"),
        ("`NCollection_Mat3<T>::Multiply(m)`, `NCollection_Mat4<T>::Multiply(m)` (`BVH_Mat4d`, `BVH_Mat4f`)",
         "Next to the static `Multiply(m1, m2)`: SWIG's wrappers take the object as the first argument, so the two collide, and SWIG "
         "keeps the static one; `a = Multiply(a, m)` does the same.",
         r"(BVH_Mat4[df]|NCollection_Mat3_(double|float))::Multiply"),
        ("`Font_FontMgr::ToUseUnicodeSubsetFallback`",
         "A static function returning a reference to a process-wide flag: C# refs come from objects (References.i), and the "
         "flag has no setter.",
         r"Font_FontMgr::ToUseUnicodeSubsetFallback"),
        ("`gp_XY::ChangeCoord`, `gp_XYZ::ChangeCoord`, `gp_Pnt::ChangeCoord`, `gp_Pnt2d::ChangeCoord`, `gp_Mat::ChangeValue`, "
         "`gp_Mat2d::ChangeValue`, `Poly_Triangle::ChangeValue`, `Quantity_ColorRGBA::ChangeRGB`",
         "A reference into a C# struct, which the GC may move: C# can't return a ref to a struct's own field on .NET Framework; "
         "`SetCoord`, `SetValue` and `SetRGB` write the same.",
         r"gp_XY::ChangeCoord|gp_XYZ::ChangeCoord|gp_Pnt::ChangeCoord|gp_Pnt2d::ChangeCoord|gp_Mat::ChangeValue|gp_Mat2d::ChangeValue"
         r"|Poly_Triangle::ChangeValue|Quantity_ColorRGBA::ChangeRGB"),
        ("`BOPAlgo_ParallelAlgo::Perform`",
         "C++ can't call it either: the private `Perform(const Message_ProgressRange& = {})` makes `Perform()` ambiguous. Subclasses "
         "implement it.",
         r"BOPAlgo_ParallelAlgo::Perform"),
        ("`Standard_Transient::IncrementRefCounter`, `DecrementRefCounter`, `Delete`",
         "Excluded in the config: the proxy owns one reference (SWIG's `ref`/`unref`), and calling these from C# would free the object "
         "under a live proxy.",
         r"Standard_Transient::(IncrementRefCounter|DecrementRefCounter|Delete)"),
    ],
}

# the groups the skip logs sort by reason: (title, intro, reason regex; its group, when it matched, is shown with the
# member). The headers the parse left out are a group of their own.
GROUPS = {
    "Outside the wrapped modules": [
        ("Renderer internals, one platform's input, FFmpeg",
         "Visualization is wrapped without most of OpenGl's renderer (windows, shaders, resources, clipping, raytracing: they "
         "declare per platform; the driver, the context, frame buffers and textures are wrapped), WNT (Windows' window and input "
         "package: a native window comes from `Aspect_Window.FromNativeHandle`), and FFmpeg's types (`AVStream`, `AVRational`: "
         "NetOcc's OCCT build has no FFmpeg). These take or return one (in parentheses).",
         r"\b((?:OpenGl|WNT)_\w+|AV\w+)\b.* (?:of an unwrapped class|is not wrapped)|^returns a mutable reference, (OpenGl_\w+)"),
    ],
    "Headers OCCT ships broken": [
        (HEADERS,
         "OCCT installs these without a header they include, or they don't compile, or they're Windows-only (in parentheses, why): "
         "the parse leaves them out, and their declarations with them.",
         None),
    ],
    "Objects C# can't create or keep": [
        ("Streams a class keeps",
         "A constructor, or a class with a stream member, may keep the stream past the call, while NetOcc lends a C# stream for one call "
         "(Streams.i).", r"may keep the stream"),
        ("Allocated by OCCT only",
         "The class declares only placement forms of `operator new` (NCollection nodes, mesh data): OCCT allocates them from its "
         "allocators, and C# gets them from OCCT.", r"declares only placement forms of operator new"),
        ("Destructors C# can't call",
         "The destructor isn't public or isn't exported: a proxy that owned one couldn't release it (its finalizer would throw).",
         r"the destructor isn't public or doesn't link"),
    ],
    "Operators": [
        ("Operators of proxies",
         "C# proxies have no operators. OCCT's mostly repeat a named member (`operator*` is `Multiplied`, `operator()` is "
         "`Value`), which C# has; shapes compare with `Equals`.",
         r"^C# proxies have no operators"),
        ("Operators of structs",
         "C# structs get OCCT's const `+`, `-`, `*`, `/` and `^` (and unary `-`); comparisons, calls, increments and the rest "
         "stay out, and so do operators that change the struct.",
         r"^C# structs get the const operators"),
    ],
    "Overrides": [
        ("Without a hook",
         "A C# subclass of a director class can't override these: SWIG runs no code after a callback without parameters or result, "
         "where the C++ side must rethrow a C# exception (Directors.i). C# calls them; C++ runs their own implementation.",
         r"^C# can't override it: SWIG runs no code after a callback"),
        ("Types a C# override can't receive or return",
         "A parameter (a stream, a collection, a raw pointer, `size_t`) or result (a reference) the callback doesn't convert (in "
         "parentheses). C# calls them; a subclass can't override them.",
         r"^C# can't override it: (?:parameter \w+: |its result: )(.+?) (?:doesn't reach a C# override|can't come back from a C# override)"),
    ],
    "Unlinkable": [
        ("Declared, never defined", "OCCT declares these (most `Standard_EXPORT`) but defines them nowhere.",
         r"^declared, but neither defined"),
        ("Inline bodies calling unexported functions",
         "Defined in the headers, but calling a function the libraries don't export (in parentheses).",
         r"^its inline definition calls (\S+), which"),
        ("Vtables naming unexported functions",
         "An inline constructor or copy of a class the libraries don't export makes MSVC emit its vtable in the shim, and the vtable "
         "names a virtual function that isn't exported (in parentheses). An inline delegating constructor references the vtable "
         "without emitting it, and the libraries export the class's virtual functions but not its vtable.",
         r"^its inline definition (?:sets the vtable, which names (\S+);|delegates to another constructor and sets the vtable)"),
    ],
}


def entries():
    """(package, member or header, reason, is a header) for every line of the skip logs."""
    for log in sorted(SKIPS.glob("*.txt")):
        for line in log.read_text(encoding="utf-8").splitlines():
            if line.startswith("header "):
                header, _, reason = line[len("header "):].partition(": ")
                yield log.stem, header, reason, True
            elif ": " in line:
                member, reason = line.split(": ", 1)
                yield log.stem, member, reason, False


def main():
    if not SKIPS.is_dir():
        sys.exit(f"no skip logs at {SKIPS}: run netocc-gen generate first")
    grouped = {title: defaultdict(list) for sections in GROUPS.values() for title, _, _ in sections}
    accepted = [(re.compile(rf"^(?:{pattern})$"), members) for cases in ACCEPTED.values() for members, _, pattern in cases]
    groups = [(title, re.compile(pattern)) for sections in GROUPS.values() for title, _, pattern in sections if pattern]
    covered = set()
    unaccounted = []
    total = 0
    for package, member, reason, is_header in entries():
        total += 1
        if is_header:
            grouped[HEADERS][package].append(f"{member} ({reason})")
            continue

        case = next((i for i, (pattern, _) in enumerate(accepted) if pattern.match(member)), None)
        if case is not None:
            covered.add(case)
            continue

        match = next(((title, m) for title, pattern in groups for m in [pattern.search(reason)] if m), None)
        if match:
            title, m = match
            grouped[title][package].append(f"{member} ({m.group(m.lastindex)})" if m.lastindex else member)
        else:
            unaccounted.append(f"{package}: {member}: {reason}")

    counts = ", ".join(f"{title} {sum(len(set(v)) for v in groups.values())}" for title, groups in grouped.items())
    print(f"{total} entries; {counts}")
    stale = [members for i, (_, members) in enumerate(accepted) if i not in covered]
    if unaccounted or stale:
        if unaccounted:
            print(f"{len(unaccounted)} entries neither accepted nor in a group:")
            for line in unaccounted:
                print("  ", line[:200])
        if stale:
            print(f"{len(stale)} accepted cases cover no entry:")
            for members in stale:
                print("  ", members[:200])
        sys.exit(f"{PAGE.name} not written")

    def section(title, intro):
        groups = grouped[title]
        lines = [f"### {title} ({sum(len(set(v)) for v in groups.values())})", "", intro, ""]
        for package in sorted(groups):
            lines.append(f"- **{package}:** " + ", ".join(f"`{m}`" for m in sorted(dict.fromkeys(groups[package]))))
        return lines + [""]

    out = [
        "# Skipped members",
        "",
        f"What NetOcc leaves out of OCCT, {total} entries, each for a reason: members OCCT declares but never defines or exports, "
        "APIs C# can't express, and the parts of Visualization NetOcc doesn't wrap (the renderer's internals, one platform's input). "
        "netocc-gen logs every skip with its reason; members C# reaches another way (overload twins, hand-written members, .NET "
        "types) aren't skips. Written by `skips.py` from the generator's skip logs.",
        "",
        "Many of the reasons are traps described in [Pitfalls](pitfalls.md).",
        "",
        "## Accepted",
        "",
    ]
    for category, cases in ACCEPTED.items():
        out += [f"### {category}", "", "| Member | Reason |", "|---|---|"]
        out += [f"| {members} | {reason} |" for members, reason, _ in cases]
        out.append("")
    for heading, sections in GROUPS.items():
        out += [f"## {heading}", ""]
        for title, intro, _ in sections:
            out += section(title, intro)
    PAGE.write_bytes(("\n".join(out).rstrip("\n") + "\n").encode("utf-8"))
    print(f"{PAGE.name} written")


if __name__ == "__main__":
    main()
