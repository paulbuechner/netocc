# netocc

Monorepo of NetOcc, the SWIG-generated C# bindings for OCCT 8.0.1. netocc-core and netocc-generator have their own `CLAUDE.md`; read it before working there. The documentation and the demos are below.

| Folder | Content |
|---|---|
| `netocc-core/` | typemap library, generated `.i` files, native shims, the `NetOcc` assembly, tests, NuGet packs (`build.py`) |
| `netocc-generator/` | `netocc-gen`: reads OCCT's headers, writes `netocc-core/src/SWIG_files` and the value-type structs |
| `netocc-documentation/` | DocFX site for GitHub Pages |
| `netocc-demos/` | WPF and Avalonia 12 viewers on the packed NetOcc |

- **Folders refer to each other by relative paths** (`--core ../netocc-core`), so each builds on its own; netocc-core builds without the generator.
- **Workflows** (`.github/workflows/`): every push to main or next runs all of them but `release.yml` (entries only) and `changelog.yml` (pull requests only); pull requests are filtered to each folder's paths. No path filters on pushes while `main` is still amended into its initial commit: each force-push is a new root commit, which gives GitHub no diff, and it silently skips path-filtered workflows then (the first two monorepo pushes started nothing). With normal commits after that, pushes can take path filters again.
  - `build.yml`: netocc-core on four RIDs, the package's documentation (netocc-gen docs, a Windows job of its own), pack, package tests, the demos and the documentation's samples on the packages (Windows). The jobs share `.github/actions/` (`occt`: vcpkg and OCCT from the binary cache; `swig`). A push to main whose `CHANGELOG.md` has a release without its tag publishes it; on next, a prerelease so. Keep the file name: nuget.org's Trusted Publishing policy names it.
  - `release.yml`: the release PR (like changesets' "Version Packages"): changelog entries on main become the next release in `CHANGELOG.md`, in the pull request "Release NetOcc <version>"; merging it publishes. On `next` (changesets' pre mode) they become the next prerelease, numbered (`8.0.1.2-next.1`, `-next.2`), in a pull request into next. After next is merged into main, the release (run by hand without new entries) lists the prereleases' notes; merge main back into next after each release, so next numbers the following revision.
  - `changelog.yml`: a pull request that changes what the packages hold needs an entry in `netocc-core/changes/`, or the label `no changelog`; a label change reruns it.
  - `generator.yml`: netocc-gen's unit and golden tests on three OSes.
  - `format.yml`: `python format.py --check` over the hand-written C#, pinned clang-format over `netocc-core/src/Native` (`.clang-format`, OCCT's style).
  - Third-party actions with write or OIDC permissions are pinned by commit SHA; `.github/dependabot.yml` updates all actions monthly.
  - `docs.yml`: builds the documentation site (warnings as errors); pushes to main deploy it to GitHub Pages (Settings > Pages > Source: GitHub Actions, which the user sets).
- **License:** MIT. The root `LICENSE` has copies in `netocc-core/` (`build.py` writes it into the package notices) and `netocc-generator/`.
- **Project metadata** lives per folder, no root `Directory.Build.props`: netocc-core's Docker build copies only that folder. Packages: `NetOcc.csproj` and `pack/NetOcc.runtime.csproj` (authors, copyright, description, tags, license, the documentation site as project URL); netocc-gen and the demos carry authors, copyright and descriptions for their file properties and aren't packable. No OCCT doc text in committed files; never copy code or lists from other OCCT bindings (GPL, LGPL).
- **Versions** are `<OCCT version>.<revision>` (`8.0.1.2`): the OCCT version netocc-gen stamped into the modules (`vcpkg.json` must build the same) and NetOcc's revision, from 1 per OCCT version (NuGet shows `8.0.1.0` as `8.0.1`). `build.py` computes and checks them: CI packs `<OCCT>.<next revision>-ci.<run>`, local packs `-local.<time>`. netocc-gen and the demos aren't published: a release's tag marks their state too.
- **Changelog:** entries wait in `netocc-core/changes/`, a file each (`python build.py change fixed "..."`, like a changeset); `build.py release` writes them into `CHANGELOG.md` for the release PR. `/release` (`.claude/skills/release/SKILL.md`) walks through a release.
- **Skills** (`.claude/skills/`): `/release`; `/occt-upgrade <version>` moves NetOcc to a new OCCT version (vcpkg pins, bootstrap, generate, what broke before, the version everywhere); `regenerate` is the loop after any change to netocc-gen, its config or the typemap library (baseline, diff and skip review, SWIG's guards, timestamps, tests).
- **Logs** go into each folder's gitignored `log/`.
- **Code style:** OCCT's `.clang-format` for the hand-written C++, and as far as C# has it for all hand-written C# (`.editorconfig`): 2-space indents, Allman braces, 100 columns, wrapped lines aligned after the parenthesis with the operators first. Unlike OCCT's, arguments and parameters that don't fit fill their lines rather than take one each, and nothing is aligned in columns. `python format.py` applies it: ReSharper's formatter (the dotnet tool pinned in `.config/dotnet-tools.json`) through a temporary project over every file, then comments wrapped at 100 columns. Generated code keeps the generator's layout, which follows the same rules (netocc-gen's `CsLayout`).

## netocc-documentation

```bash
cd netocc-documentation
dotnet tool restore                 # docfx, pinned in dotnet-tools.json
python classes.py                   # classes/*.md from netocc-core's modules.json and classes.json (gitignored)
python skips.py                     # articles/skipped.md from netocc-gen's skip logs (committed; after a generate)
dotnet test samples -c Release      # the examples, on netocc-core's packed NetOcc (build.py pack first)
dotnet docfx docfx.json --serve     # _site/, http://localhost:8080
```

- **Content:** `index.md` and `articles/`, sectioned by `articles/toc.yml` (the files stay flat, so URLs don't move): Guide (introduction, getting started, geometry, topology, modeling, analysis and repair, meshing, files, assemblies, documents, 3D views), Concepts (C++ to C#, lifetimes, errors, how NetOcc works), Reference (platforms, building, troubleshooting), Development (design decisions, pitfalls, licensing, skipped members: the maintainers' knowledge base, which a note outside the repository held before; add to it as you learn). The class index links every type to OCCT's 8.0.1 reference manual. Pages there are Doxygen's (`classgp___pnt.html`: a capital as `_` and its lower letter, `_` as `__`, `::` as `_1_1`); `classes.py` builds them from what netocc-gen records.
- **Examples are tests:** every C# example is a `#region` of an NUnit test in `samples/` (on the packed NetOcc like the demos: `nuget.config`, `NetOcc.version.props`), which articles include with `[!code-csharp[](../samples/<File>.cs#<region>)]` (docfx dedents it). A new example goes into a test, asserts after the region; `build.yml`'s demos job runs them. Visualization's compile only (no GPU in CI): methods outside `[Test]`. Say only what a sample or a check showed. The READMEs repeat samples, which they can't include: the repository's `README.md` shows `Readme.cs`, a whole `Program.cs` with its `Main`, which a test runs, the package's (`netocc-core/src/NetOcc/README.md`) `GettingStarted.cs` (`usings`, `first-part`); `Readmes` fails when they drift.
- **Diagrams:** mermaid code blocks, which the modern template renders. Render new ones before committing (`mmdc` from `@mermaid-js/mermaid-cli`, puppeteer's `executablePath` on a local Chromium) and look at them: dagre crosses edges when a tree's children come in an unlucky order.
- **Skipped members** (`articles/skipped.md`) is written by `skips.py` from the skip logs (`netocc-generator/log/skips`, not committed): the accepted cases are curated in the script, each with the members it covers, the rest grouped by reason. It fails, without writing the page, on an entry nothing accounts for and on an accepted case that covers none; rerun it after every generate and commit the page.
- **No generated API reference:** OCCT's doc text is in the package's IntelliSense (LGPL, never committed) but not on the site, so the manual is the reference and the site explains the mapping.

## netocc-demos

```bash
(cd netocc-core && python build.py pack --rids win-x64,win-x86)   # the packages, and NetOcc.version.props the demos import
dotnet build netocc-demos/NetOcc.Demos.slnx -c Release            # both viewers and the tests
dotnet test netocc-demos/NetOcc.Demos.slnx -c Release --no-build  # the viewer library's tests (dotnet test alone skips the viewers)
netocc-demos/src/NetOcc.Viewer.Wpf/bin/Release/net10.0-windows/NetOcc.Viewer.Wpf.exe --sample   # or a .step path
```

- **Layout:** `src/NetOcc.Viewer` (net10.0) holds what both share: `OcctViewer` (driver, viewer, context, `AIS_ViewController`), `StepDocument` (XCAF, STEPCAFControl), `SampleModel` (a bolted plate written as STEP), `FileOpener` (one open at a time, read off the UI thread; a file that doesn't read is a status line, not an exception), `ViewerWindow`. `NetOcc.Viewer.Wpf` hosts the window in an `HwndHost`, `NetOcc.Viewer.Avalonia` (12.1.3) in a `NativeControlHost`. `test/NetOcc.Viewer.Tests` covers the parts without a window.
- **Packages:** `Directory.Build.props` imports netocc-core's `artifacts/packages/NetOcc.version.props`; `nuget.config` maps `NetOcc*` to that folder, the rest to nuget.org. Without a pack, the build stops with a message.
- **The view's window** (`ViewerWindow`) is a Win32 child of our own class: `CS_OWNDC` (OpenGL needs its DC), and its procedure feeds size, paint and mouse messages to the viewer (capture while a button is down, a lost capture ends the gesture, wheel positions from screen coordinates). No exception leaves the procedure: it can't unwind through user32. The host disposes the viewer before destroying the window.
- **Mouse masks:** OCCT's `Aspect_VKeyMouse`/`Aspect_VKeyFlags` are anonymous enums, which C# doesn't get: `ViewerButtons` and `ViewerModifiers` repeat their values.
- **Avalonia** creates the native control after the window shows, and again after the control moves: `ViewerHost.ViewerCreated` fires after that layout pass, and the command line's file opens then. Windows only: Linux and macOS would need an X11 window or NSView child.
- **Never drive the viewers** (no clicks, keystrokes or screenshots): launch them with `--sample` or a file and ask the user to look.
