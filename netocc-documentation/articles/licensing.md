# Licensing

MIT wherever NetOcc can, OCCT's terms where it must. This isn't legal advice. The repository carries `LICENSE` (MIT) and `netocc-core/NOTICE`; the packages carry `NOTICE`, the runtime packages `THIRD-PARTY-NOTICES.txt` (NOTICE and every vcpkg port's license).

| Part | License | Why, and what it takes |
|---|---|---|
| netocc-gen, its config, the typemap library, the runtime, the hand-written C#, tests, build scripts | MIT | NetOcc's own code; the generator reads OCCT's headers and never links OCCT |
| Generated `.i`, `_module.hxx` and `.g.cs` | MIT, committed | declarations only: OCCT's doc text never lands in committed files |
| SWIG's output (`*_wrap.cxx`, `*.cs`) | build output | not committed |
| Native libraries (`NetOcc<Module>`, `NetOccRuntime`) | MIT | the [OCCT exception](https://github.com/Open-Cascade-SAS/OCCT/blob/master/OCCT_LGPL_EXCEPTION.txt) lets object code that incorporates header material use terms of your choice, given a prominent notice that it uses OCCT: `NOTICE` |
| `NetOcc.dll` | MIT | C# calling the native libraries |
| `NetOcc.xml`, the IntelliSense documentation | LGPL-2.1 with the OCCT exception | the doc comments of OCCT's headers: shipped with `OCCT-LICENSE.txt`, never committed. The package's license expression then reads `MIT AND LGPL-2.1-only WITH OCCT-exception-1.0` |
| OCCT's libraries (`TK*`) | LGPL-2.1 with the OCCT exception | shipped as separate, replaceable libraries with their license texts, and the exact source published: OCCT's tag and vcpkg's port patches. One patch changes `Image_AlienPixMap.cxx`, so the shipped OCCT is modified |
| FreeType, RapidJSON, zlib, libpng, brotli, bzip2, oneTBB, hwloc | FTL, MIT, zlib, libpng, MIT, bzip2, Apache-2.0, BSD-3-Clause | attribution in `NOTICE`, license texts in `THIRD-PARTY-NOTICES.txt`; no FreeImage (GPL, FIPL) |
| SWIG | the tool GPL-3.0, its runtime library terms of your choice | the generated output is unrestricted |
| ClangSharp, libclang | MIT, Apache-2.0 WITH LLVM-exception | the generator's dependencies only |
| Other OCCT bindings | GPL-3.0, LGPL-3.0 | reference for rules at most: copying their code or exclusion lists would make NetOcc's GPL |

- **Managed math is written from scratch.** Porting OCCT's inline implementations to C# would make that code an LGPL derivative, so anything beyond trivial math calls OCCT.
- **NetOcc isn't affiliated with Open Cascade SAS.** "Open CASCADE" and "Open CASCADE Technology" are its trademarks.
