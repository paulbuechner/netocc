---
name: regenerate
description: Change netocc-gen, its config or netocc-core's typemap library and regenerate the bindings. Generator tests, generate with a reviewed diff and skip logs, SWIG with its guards, native builds only when the wrappers changed, tests on every framework, generate --check. Use for any change to netocc-generator, config/modules.yaml, src/SWIG_files/common or extras, and within an OCCT upgrade.
---

# Regenerate the bindings

Generated: `netocc-core/src/SWIG_files/{wrapper,headers}`, `modules.json`, `classes.json` and `netocc-core/src/NetOcc/<Pkg>/*.g.cs`. Never edit them. Change netocc-gen (`netocc-generator/src`), `config/modules.yaml`, the typemap library (`netocc-core/src/SWIG_files/common/*.i`), a companion (`extras/<Pkg>.i`) or a value type's partial (`netocc-core/src/NetOcc/<Pkg>/<Type>.cs`). The contract and the rules: netocc-core's CLAUDE.md (Wrapper contract, Gotchas) and netocc-generator's (Mapping rules).

## 1. The change, with a test

- netocc-gen: a unit test on a synthetic model (`InterfaceWriterTests`, `SignatureMapperTests`; `PackageParserTests` run libclang on a temporary header), then `dotnet test NetOcc.Generator.slnx -c Release` in netocc-generator.
- Golden output changed on purpose: `NETOCC_UPDATE_GOLDEN=1 dotnet test NetOcc.Generator.slnx -c Release`, then review `git diff -- test/NetOcc.Generator.Tests/Golden`.
- The typemap library: a test in `netocc-core/test/NetOcc.Tests` for what C# sees.

## 2. Baseline

Copies to compare with (`log/` and `build/` are gitignored):

```bash
cp -r netocc-generator/log/skips netocc-generator/log/skips.before
cp -a netocc-core/build/generated netocc-core/build/generated.before   # -a keeps the timestamps
```

## 3. Generate

On Windows (the export check reads OCCT's DLLs), with the paths from netocc-generator/CLAUDE.md (Commands):

```bash
cd netocc-generator
dotnet run --project src/NetOcc.Generator -c Release -- generate --occt-src <sources> --occt-include <headers> --core ../netocc-core > log/generate.log 2>&1
```

- `git diff --stat -- ../netocc-core/src/SWIG_files` touches only what the change should. For a wide change, prove it with a script. The import-order change touched 333 modules, each only in its guard, the order of its `%import` lines and of its `using`s.
- `diff -r log/skips.before log/skips`: understand every new skip. A skip that's gone is a member newly wrapped, which may deserve a test.
- `python skips.py` in netocc-documentation rewrites `articles/skipped.md` and fails on a skip nothing accounts for: accept it in the script's `ACCEPTED` (members, reason) or fix the generator.

## 4. SWIG

```bash
cd ../netocc-core
python build.py generate > log/generate.log 2>&1
```

It fails on Warning 516 (SWIG dropped an overload), on a Warning 401 that costs a class its base, and on `SWIGTYPE_*` types in the C# (a typemap missed). Each is a bug in the `.i` or the typemaps, not noise to silence.

## 5. What SWIG's output changed

SWIG rewrites every file, and ninja rebuilds each wrapper whose timestamp moved: an hour or more for both triplets. Compare with the copy, and give unchanged files their old timestamps (a scratch script, from `netocc-core/`):

```python
import filecmp, os
from pathlib import Path
now, before = Path("build/generated"), Path("build/generated.before")
for path in now.rglob("*"):
    old = before / path.relative_to(now)
    if path.is_file() and old.exists() and filecmp.cmp(path, old, shallow=False):
        stat = old.stat()
        os.utime(path, ns=(stat.st_atime_ns, stat.st_mtime_ns))
```

- `diff -r build/generated.before build/generated`: is the change the one you meant?
- No `.cxx` changed: skip the natives. Otherwise `python build.py native --triplet x64-windows` and `x86-windows`.
- Then delete `build/generated.before`.

## 6. Tests and notes

- `python build.py test --arch x64` and `--arch x86`: every framework, net35 on CLR 2.
- `generate --check` in netocc-generator reports up to date.
- When the public API changed: `python build.py pack --rids win-x64,win-x86`, `test-package --arch x64`, the documentation's samples (`dotnet test netocc-documentation/samples -c Release`) and the demos (`dotnet build`, then `dotnet test --no-build` on `netocc-demos/NetOcc.Demos.slnx`; `dotnet test` alone skips the viewers).
- Notes: the rule in its CLAUDE.md (netocc-generator: Mapping rules; netocc-core: the contract or Gotchas); a changelog entry when package users see it (`python build.py change <type> "..."` in netocc-core); `netocc-documentation/articles/mapping.md` when a mapping changed.
- Commit only when the user asks.

## Traps

- **Line endings:** on Windows, Python's `Path.write_text` writes CRLF, and the repository is LF. Write bytes, or open with `newline="\n"`.
- **Python:** Git Bash's `python` is MSYS2's and runs `build.py`; it has neither PyYAML nor Pillow (`yq` reads YAML, Anaconda's Python has Pillow).
- **Bash heredocs** with quotes or backslashes break in the Bash tool: put edit scripts into `netocc-core/log/scratch/` and run them.
- **Stale test binaries:** `dotnet test --arch` builds into RID folders; `build.py test` always builds.
- **The real C# signatures** are in `netocc-core/build/generated/cs/<Pkg>/<Class>.cs`: check an API there, or in a scratch console project on the packed NetOcc, before writing about it.
