#!/usr/bin/env python3
# SPDX-FileCopyrightText: 2026 Paul Büchner
# SPDX-License-Identifier: MIT

"""netocc-vtk build driver: NetOcc.Vtk, OCCT's TKIVtk on NetVtk. netocc-core's build.py builds OCCT (with its vtk feature,
on NetVtk's vcpkg registry), runs SWIG and computes the versions; this drives the rest.

    python build.py netvtk                          # NetVtk restored; prints its SWIG files (swig/), for netocc-gen --netvtk
    python build.py generate                        # SWIG over src/SWIG_files/wrapper -> build/generated
    python build.py native --triplet x64-windows    # NetOccVtk + TKIVtk -> artifacts/runtimes/<rid>/native
    python build.py test --arch x64 [--framework net10.0|net8.0|net48] [--no-rendering]
    python build.py pack [--version 8.0.1.2] [--rids win-x64,win-x86]   # NetOcc.Vtk + NetOcc.Vtk.runtime.<rid>
    python build.py test-package [--arch x64]       # the packed NetOcc.Vtk, consumed like an app would

NetVtk: the version in NetVtk.props, from nuget.org; with a local-dev.props (local-dev.props.template), the packs of a
local netvtk checkout its LocalNetVtkPackagesPath names, as every solution takes them (Directory.Build.props). A pack
records that folder (NetOcc.Vtk.version.props), and test-package restores from it.

Versions: NetOcc.Vtk depends on the NetOcc netocc-core's build.py pack made (>= its version). CI and releases pack it at
that version (--version); a local pack gets one of its own, <OCCT version>.<next revision>-local.<utc time>, as NetOcc's
do: packed again on the same NetOcc, a version NuGet has extracted before would keep its first copy.
"""
from __future__ import annotations

import argparse
import functools
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
CORE_ROOT = ROOT.parent / "netocc-core"
sys.path.insert(0, str(CORE_ROOT))
import build as core  # noqa: E402  netocc-core's build.py
import sbom  # noqa: E402  netocc-core's sbom.py

BUILD = ROOT / "build"
GENERATED = BUILD / "generated"
ARTIFACTS = ROOT / "artifacts"
PACKAGES = ARTIFACTS / "packages"
SWIG_FILES = ROOT / "src" / "SWIG_files"
PROJECT = ROOT / "src" / "NetOcc.Vtk" / "NetOcc.Vtk.csproj"
TESTS = ROOT / "test" / "NetOcc.Vtk.Tests"
PACKAGE_TESTS = ROOT / "test" / "NetOcc.Vtk.PackageTests"
FRAMEWORKS = ["net10.0", "net8.0", "net48"]


def natives(rid: str) -> Path:
    return ARTIFACTS / "runtimes" / rid / "native"


# --------------------------------------------------------------------------- NetVtk
@functools.cache
def netvtk_source() -> tuple[str, Path | None]:
    """NetVtk's version and the folder of local packs it comes from (local-dev.props' LocalNetVtkPackagesPath), or None
    for nuget.org: as MSBuild evaluates NetOcc.Vtk, so build.py and the solutions take the same NetVtk."""
    out = subprocess.run(["dotnet", "msbuild", PROJECT, "-getProperty:NetVtkVersion",
                          "-getProperty:LocalNetVtkPackagesPath"], capture_output=True, text=True, check=True).stdout
    properties = json.loads(out)["Properties"]
    folder = Path(properties["LocalNetVtkPackagesPath"]).resolve() if properties["LocalNetVtkPackagesPath"] else None
    if folder and not (folder / "NetVtk.version.props").is_file():
        sys.exit(f"no packed NetVtk in {folder} (local-dev.props): run netvtk-core's build.py pack first")
    return properties["NetVtkVersion"], folder


def nuget_root() -> Path:
    return Path(os.environ.get("NUGET_PACKAGES") or Path.home() / ".nuget" / "packages")


def netvtk_swig() -> Path:
    """NetVtk's SWIG files (its package's swig/), restored at the version NetOcc.Vtk builds on."""
    core.run(["dotnet", "restore", PROJECT])
    version, _ = netvtk_source()
    swig = nuget_root() / "netvtk" / version.lower() / "swig"
    if not (swig / "interop" / "NetVtkInterop.i").is_file():
        sys.exit(f"NetVtk {version} has no swig/interop/NetVtkInterop.i at {swig}")
    return swig


def cmd_netvtk(args: argparse.Namespace) -> None:
    print(netvtk_swig())


# --------------------------------------------------------------------------- generate, native
def libraries() -> dict[str, list[str]]:
    """modules.json, as netocc-gen writes it with config/vtk.yaml: the library and its modules."""
    return json.loads((SWIG_FILES / "modules.json").read_text(encoding="utf-8"))


def cmd_generate(args: argparse.Namespace) -> None:
    swig = netvtk_swig()
    # NetOcc's modules (imported), their companions (extras/), NetVtk's interop typemaps
    core.generate_modules(SWIG_FILES, GENERATED, libraries(),
                          [core.SWIG_FILES / "wrapper", core.SWIG_FILES, swig / "interop"])


def cmd_native(args: argparse.Namespace) -> None:
    rid, vc_arch = core.TRIPLETS[args.triplet]
    prefix = core.vcpkg_prefix(args.triplet)
    if not (prefix / "share" / "opencascade").is_dir():
        sys.exit(f"OCCT missing for {args.triplet}: run netocc-core's build.py occt --triplet {args.triplet}")
    if not list((prefix / "share").glob("vtk*")):
        sys.exit(f"no VTK in {prefix}: netocc-core's vcpkg.json builds opencascade with its vtk feature")
    if not (GENERATED / "cxx").is_dir():
        sys.exit("SWIG output missing: run build.py generate")
    build_dir = BUILD / f"native-{args.triplet}"
    stage = natives(rid)
    env = core.msvc_env(vc_arch) if vc_arch else dict(os.environ)
    configure = ["cmake", "-S", ROOT, "-B", build_dir, "-G", "Ninja",
                 "-DCMAKE_BUILD_TYPE=Release",
                 f"-DCMAKE_PREFIX_PATH={prefix}",
                 f"-DNETOCC_GENERATED_DIR={GENERATED / 'cxx'}",
                 f"-DNETOCC_OCCT_VERSION={core.occt_version()}",
                 f"-DNETOCC_OCCT_RUNTIME_DIR={prefix / ('bin' if core.host_is_windows() else 'lib')}",
                 f"-DCMAKE_INSTALL_PREFIX={stage}"]
    if core.host_is_macos():
        configure.append(f"-DCMAKE_OSX_DEPLOYMENT_TARGET={core.macos_deployment_target()}")
    core.run(configure, env=env)
    core.run(["cmake", "--build", build_dir, "--parallel"], env=env)
    if stage.exists():
        shutil.rmtree(stage)
    core.run(["cmake", "--install", build_dir], env=env)
    if not core.host_is_windows():
        core.flatten_stage(stage)
    notices = stage.parent / "THIRD-PARTY-NOTICES.txt"
    core.write_notices(args.triplet, stage, notices)
    # VTK's headers' inline code is compiled into NetOccVtk and TKIVtk (its libraries are NetVtk.runtime's)
    vtk_license = prefix / "share" / "vtk" / "licenses" / "VTK" / "Copyright.txt"
    rule = "=" * 78
    text = vtk_license.read_text(encoding="utf-8", errors="replace").strip()
    with notices.open("a", encoding="utf-8") as out:
        out.write(f"{rule}\nvtk\n{rule}\n\n{text}\n\n")
    sbom.write(stage, stage.parent / sbom.FILE, f"NetOcc.Vtk.runtime.{rid}",
               installed=core.vcpkg_installed(args.triplet), triplet=args.triplet, repository=core.REPOSITORY,
               compiled_in={"vtk": "headers compiled into NetOccVtk and TKIVtk; its libraries come with NetVtk.runtime"})


# --------------------------------------------------------------------------- test, pack
def cmd_test(args: argparse.Namespace) -> None:
    frameworks = [args.framework] if args.framework else [f for f in FRAMEWORKS if core.host_is_windows() or "." in f]
    # Linux and macOS: no RID, so NetVtk's VTK libraries land in runtimes/<rid>/native, beside the project's NetOccVtk,
    # whose $ORIGIN finds them there (with a RID NuGet flattens them into the output); one architecture each anyway
    arch = ["--arch", args.arch] if core.host_is_windows() else []
    for framework in frameworks:
        cmd = ["dotnet", "test", TESTS / "NetOcc.Vtk.Tests.csproj", "-c", "Release", *arch,
               "--framework", framework]
        if args.no_rendering:
            cmd += ["--filter", "TestCategory!=Rendering"]
        core.run(cmd)


def netocc_version() -> str:
    """The version of NetOcc in netocc-core's pack output (NetOcc.version.props), which NetOcc.Vtk depends on."""
    props = core.PACKAGES / "NetOcc.version.props"
    if not props.is_file():
        sys.exit("no packed NetOcc: run netocc-core's build.py pack first (NetOcc.Vtk depends on it)")
    match = re.search(r"<NetOccPackageVersion>([^<]+)<", props.read_text(encoding="utf-8"))
    if not match:
        sys.exit(f"{props}: no NetOcc version")
    return match.group(1).strip()


def cmd_pack(args: argparse.Namespace) -> None:
    # NetOcc.Vtk depends on the packed NetOcc; its version is the one asked for (CI and releases: NetOcc's), else a local one
    # of its own
    netocc = netocc_version()
    version = args.version or core.local_version(core.occt_version())
    core.check_version(version)
    rids = args.rids.split(",") if args.rids else [rid for rid, _ in core.TRIPLETS.values()]
    missing = [rid for rid in rids if not natives(rid).is_dir()]
    if missing:
        sys.exit(f"natives missing for {', '.join(missing)}: run build.py native there (or pass --rids)")
    if not (GENERATED / "cs").is_dir():
        sys.exit("SWIG output missing: run build.py generate")
    if PACKAGES.exists():
        shutil.rmtree(PACKAGES)
    PACKAGES.mkdir(parents=True)
    ci = ["-p:ContinuousIntegrationBuild=true"] if os.environ.get("GITHUB_ACTIONS") else []
    for rid in rids:
        sbom.stamp(natives(rid).parent / sbom.FILE, version)
        core.run(["dotnet", "pack", ROOT / "pack" / "NetOcc.Vtk.runtime.csproj", "-c", "Release", "-o", PACKAGES,
                  f"-p:NetOccVtkRid={rid}", f"-p:Version={version}", *ci])
    # Version reaches the NetOcc project reference, whose version the dependency takes: NetOcc's. NetOcc.Vtk's own comes as
    # NetOccVtkVersion, which its project puts in Version's place (TreatAsLocalProperty). NetVtk at NetVtk.props's or the
    # local one's; -p: splits values at ';', so the sources' separators are escaped
    netvtk, packages = netvtk_source()
    managed = [f"-p:Version={netocc}", f"-p:NetOccVtkVersion={version}", f"-p:NetVtkVersion={netvtk}", *ci]
    env = dict(os.environ, NetOccVtkRuntimeRids=";".join(rids))
    sources = "%3B".join(str(s) for s in [PACKAGES, core.PACKAGES, *([packages] if packages else [])])
    core.run(["dotnet", "restore", PROJECT, *managed, f"-p:RestoreAdditionalProjectSources={sources}"], env=env)
    core.run(["dotnet", "pack", PROJECT, "-c", "Release", "--no-restore", "-o", PACKAGES, *managed], env=env)
    # the NetOcc it was packed on (>=), exactly its NetVtk, its runtime packages at its own version
    package = next(PACKAGES.glob("NetOcc.Vtk.[0-9]*.nupkg"))
    expected = {"NetOcc": netocc, "NetVtk": f"[{netvtk}]", **{f"NetOcc.Vtk.runtime.{rid}": version for rid in rids}}
    if problems := core.dependency_problems(package, expected):
        sys.exit(f"{package.name} depends on {', '.join(problems)}, not on "
                 f"{', '.join(f'{name} {want}' for name, want in expected.items())}")
    # and where its NetVtk comes from, for the projects that consume the pack: a local one only restores from there
    source = f"    <NetVtkPackageSource>{packages}</NetVtkPackageSource>\n" if packages else ""
    (PACKAGES / "NetOcc.Vtk.version.props").write_text(
        f"<Project>\n  <PropertyGroup>\n    <NetOccVtkPackageVersion>{version}</NetOccVtkPackageVersion>\n{source}"
        f"  </PropertyGroup>\n</Project>\n", encoding="utf-8")
    print(f"NetOcc.Vtk {version} on NetOcc {netocc}: {', '.join(sorted(p.name for p in PACKAGES.glob('*.*nupkg')))}")


def cmd_test_package(args: argparse.Namespace) -> None:
    if not (PACKAGES / "NetOcc.Vtk.version.props").exists():
        sys.exit("no packages: run build.py pack first")
    frameworks = ["net8.0", "net48"] if core.host_is_windows() else ["net8.0"]
    for framework in frameworks:
        core.run(["dotnet", "test", PACKAGE_TESTS / "NetOcc.Vtk.PackageTests.csproj", "-c", "Release", "--arch",
                  args.arch, "--framework", framework])


# --------------------------------------------------------------------------- command line
def parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)
    sub.add_parser("netvtk", help="restore NetVtk; print its SWIG files").set_defaults(fn=cmd_netvtk)
    sub.add_parser("generate", help="SWIG over the modules -> build/generated").set_defaults(fn=cmd_generate)
    n = sub.add_parser("native", help="NetOccVtk and TKIVtk -> artifacts/runtimes/<rid>/native")
    n.add_argument("--triplet", required=True, choices=sorted(core.TRIPLETS))
    n.set_defaults(fn=cmd_native)
    t = sub.add_parser("test", help="the NetOcc.Vtk tests")
    t.add_argument("--arch", default="x64", choices=["x64", "x86", "arm64"])
    t.add_argument("--framework", choices=FRAMEWORKS)
    t.add_argument("--no-rendering", action="store_true", help="leave out the tests that render (no OpenGL 3.2)")
    t.set_defaults(fn=cmd_test)
    pk = sub.add_parser("pack", help="NetOcc.Vtk and NetOcc.Vtk.runtime.<rid> -> artifacts/packages")
    pk.add_argument("--version", help="package version; default <OCCT version>.<next revision>-local.<utc time>")
    pk.add_argument("--rids", help="comma-separated; default all four")
    pk.set_defaults(fn=cmd_pack)
    tp = sub.add_parser("test-package", help="the packed NetOcc.Vtk, consumed like an app")
    tp.add_argument("--arch", default="x64", choices=["x64", "x86", "arm64"])
    tp.set_defaults(fn=cmd_test_package)
    return p


def main() -> None:
    args = parser().parse_args()
    try:
        args.fn(args)
    except subprocess.CalledProcessError as e:
        command = e.cmd if isinstance(e.cmd, str) else Path(str(e.cmd[0])).name
        sys.exit(f"build.py {args.cmd}: {command} failed (exit code {e.returncode})")


if __name__ == "__main__":
    main()
