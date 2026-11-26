# SPDX-FileCopyrightText: 2026 Paul Büchner
# SPDX-License-Identifier: MIT

"""The runtime packages' CycloneDX SBOM, with the data fields of BSI TR-03183-2.

Every file of a runtime package goes under the vcpkg port it comes from or the project's own libraries. Each port, which
no package manager lists, comes with its creator, version, license (the texts as evidence), copyright, source and
patches from vcpkg's SPDX file of the port, and what it needs of the others from vcpkg's status. build.py native writes
the SBOM beside THIRD-PARTY-NOTICES.txt (write), pack stamps the package's version, the files' SHA-512 and the SBOM's
serial number and time in (stamp). The package carries it as sbom/sbom.cdx.json, where a consumer's own SBOM takes it in
under the package. netocc-vtk's build.py writes and stamps its package's SBOM with this module too.
"""
from __future__ import annotations

import hashlib
import json
import re
import subprocess
import sys
import uuid
from collections.abc import Callable, Iterator
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent
FILE = "sbom.cdx.json"
SPEC = "1.6"
# header-only ports compiled into a shipped library, by the library's stem: no file of theirs is staged, but their code
# is (RWGltf's JSON in TKDEGLTF)
COMPILED_IN = {"rapidjson": "tkdegltf"}
# The license NetOcc ships a port's libraries under where the port's declaration falls short: OCCT's LGPL comes with
# Open CASCADE's exception, which vcpkg leaves out; FreeType offers FTL or the GPL, NetOcc takes FTL; hwloc's text has
# three clauses where vcpkg declares two
LICENSE_DECISIONS = {"opencascade": "LGPL-2.1-only WITH OCCT-exception-1.0", "freetype": "FTL", "hwloc": "BSD-3-Clause"}


# --------------------------------------------------------------------------- the vcpkg ports a stage ships
def library_stem(name: str) -> str | None:
    """A shared library's name without its version and extension (libz.so.1.3.1, libz.1.dylib, z.dll: libz, libz, z), so a
    port's installed file and its staged copy (renamed to the SONAME) match; None for other files."""
    match = re.fullmatch(r"(.+?)(?:\.so(?:\.\d+)*|(?:\.\d+)*\.dylib|\.dll)", name, re.I)
    return match.group(1).lower() if match else None


def shipped_ports(installed: Path, triplet: str, stage: Path) -> list[str]:
    """The vcpkg ports whose libraries the stage holds (vcpkg's installed file lists, <installed>/vcpkg/info/<port>_
    <version>_<triplet>.list), and the header-only ones compiled into one of those libraries. Not build tools (gperf) or
    empty ports (pthread), whose licenses would go into the package for nothing."""
    staged = {stem for path in stage.iterdir() if (stem := library_stem(path.name))}
    ports = set()
    for listing in (installed / "vcpkg" / "info").glob(f"*_{triplet}.list"):
        port = listing.name.split("_", 1)[0]
        files = listing.read_text(encoding="utf-8").splitlines()
        if COMPILED_IN.get(port) in staged or any(library_stem(Path(f).name) in staged for f in files):
            ports.add(port)
    return sorted(ports)


def port_spdx(prefix: Path, port: str) -> dict:
    """What vcpkg's SPDX file of a port installed under the prefix says: version (without the port-version), license,
    homepage, where the port and its source come from, the port's patches."""
    document = json.loads((prefix / "share" / port / "vcpkg.spdx.json").read_text(encoding="utf-8"))
    packages = {p["SPDXID"]: p for p in document["packages"]}
    recipe, resource = packages["SPDXRef-port"], packages.get("SPDXRef-resource-0", {})
    files = [f["fileName"].removeprefix("./") for f in document.get("files", [])]
    return {"version": recipe["versionInfo"].split("#")[0], "license": recipe.get("licenseConcluded", "NOASSERTION"),
            "homepage": recipe.get("homepage"), "recipe": recipe.get("downloadLocation", "NOASSERTION"),
            "source": resource.get("downloadLocation", "NOASSERTION"),
            "patches": sorted(f for f in files if "/" not in f and f.endswith(".patch"))}


def port_dependencies(installed: Path) -> dict[str, set[str]]:
    """What each installed port needs, its features' needs included, as vcpkg's status file lists it."""
    needs: dict[str, set[str]] = {}
    status = (installed / "vcpkg" / "status").read_text(encoding="utf-8")
    for paragraph in re.split(r"\n\s*\n", status):
        fields = dict(re.findall(r"^([\w-]+): (.*)$", paragraph, re.M))
        if fields.get("Status") != "install ok installed":
            continue
        depends = [d.strip().split(":")[0] for d in fields.get("Depends", "").split(",") if d.strip()]
        needs.setdefault(fields["Package"], set()).update(depends)
    return needs


# --------------------------------------------------------------------------- CycloneDX and BSI TR-03183-2
# The same in netvtk-core/sbom.py and netocc-core/sbom.py: a change here goes into both.
def licenses(terms: list[str]) -> list[dict]:
    """The license a component ships under (BSI TR-03183-2's distribution license) as one concluded SPDX expression.
    CycloneDX takes nothing beside an expression: the texts go into the evidence."""
    return [{"expression": " AND ".join(terms), "acknowledgement": "concluded"}]


def evidence(texts: list[tuple[str, str]]) -> dict:
    """License texts, (term, text) each, as the evidence of a component's licenses; a reference or a term with an
    exception goes by name, which CycloneDX's ids can't spell."""
    found = []
    for term, text in texts:
        key = "name" if " WITH " in term or term.startswith("LicenseRef-") else "id"
        found.append({"license": {key: term, "text": {"contentType": "text/plain", "content": text}}})
    return {"licenses": found}


def copyright_lines(text: str) -> str:
    """The copyright lines of a license text, each once."""
    lines = []
    for line in text.splitlines():
        line = line.strip()
        if re.match(r"(Copyright\b|\(c\)|©)", line, re.I) and line not in lines:
            lines.append(line)
    return "\n".join(lines)


def source(location: str) -> tuple[str, str | None] | None:
    """The repository (without .git) and the ref of a git+<repository>@<ref>#<path> source; None for an archive."""
    match = re.match(r"git\+(.+?)(?:\.git)?(?:@([^#]+))?(?:#.*)?$", location)
    return (match.group(1), match.group(2)) if match else None


def source_references(location: str) -> list[dict]:
    """Where a library's source comes from: a repository, the ref as a comment, and the location itself, a
    git+<repository>@<ref> or an archive, which names the exact source (BSI TR-03183-2's source code URI)."""
    references = []
    if repository := source(location):
        vcs = {"type": "vcs", "url": repository[0]}
        if repository[1]:
            vcs["comment"] = repository[1]
        references.append(vcs)
    return references + [{"type": "source-distribution", "url": location}]


def bsi_properties(executable: bool, archive: bool, structured: bool, filename: str | None = None) -> list[dict]:
    """BSI TR-03183-2's properties of a component that is a file, by its CycloneDX taxonomy."""
    properties = [{"name": "bsi:component:filename", "value": filename}] if filename else []
    return properties + [
        {"name": "bsi:component:executable", "value": "executable" if executable else "non-executable"},
        {"name": "bsi:component:archive", "value": "archive" if archive else "no archive"},
        {"name": "bsi:component:structured", "value": "structured" if structured else "unstructured"}]


def own_name(package: str) -> str:
    """The project a runtime package belongs to (NetOcc.Vtk.runtime.win-x64: NetOcc.Vtk)."""
    return package.split(".runtime.")[0]


def package_rid(package: str) -> str:
    """The RID a runtime package's name ends in (NetOcc.Vtk.runtime.win-x64: win-x64)."""
    return package.split(".runtime.")[1]


def creator(package: str, repository: str) -> dict:
    """The creator of the SBOM, the package and the project's own libraries: the project (NetOcc of NetOcc.Vtk)."""
    return {"name": own_name(package).split(".")[0], "url": [repository]}


def root_reference(package: str) -> str:
    """The reference of the package itself, the SBOM's root; pack adds the version."""
    return f"pkg:nuget/{package}"


def own_reference(package: str) -> str:
    """The reference of the project's own libraries in a runtime package's SBOM, the package's version theirs."""
    return f"{package}/{own_name(package).lower()}"


def own_component(package: str, repository: str, description: str) -> dict:
    """The project's own libraries, under its LICENSE (MIT); the version is the package's, which pack stamps in."""
    text = (ROOT / "LICENSE").read_text(encoding="utf-8").strip()
    return {"type": "library", "bom-ref": own_reference(package),
            "manufacturer": creator(package, repository), "name": own_name(package), "version": "",
            "description": description, "licenses": licenses(["MIT"]), "copyright": copyright_lines(text),
            "externalReferences": [{"type": "vcs", "url": repository}], "evidence": evidence([("MIT", text)])}


def file_component(package: str, name: str, owner: dict) -> dict:
    """A file of the package as a component of the library it belongs to, with that library's creator, version and
    license: a shared library, executable, its parts no longer told apart (unstructured). Pack adds its hash."""
    where = f"runtimes/{package_rid(package)}/native/{name}"
    return {"type": "file", "bom-ref": f"{package}/{where}", "manufacturer": owner["manufacturer"], "name": name,
            "version": owner["version"], "licenses": owner["licenses"],
            "externalReferences": [{"type": "distribution", "url": where}],
            "properties": bsi_properties(True, False, False, name)}


def add_files(stage: Path, package: str, owner_of: Callable[[str], dict]) -> None:
    """Each file of the stage as a component of the library owner_of names by the file's name."""
    for path in sorted(stage.iterdir(), key=lambda p: p.name.lower()):
        owner = owner_of(path.name)
        owner.setdefault("components", []).append(file_component(package, path.name, owner))


def walk(components: list[dict]) -> Iterator[dict]:
    """Every component, each nested one after the one it belongs to."""
    for component in components:
        yield component
        yield from walk(component.get("components", []))


def timestamp() -> str:
    return f"{datetime.now(timezone.utc):%Y-%m-%dT%H:%M:%SZ}"


def document(package: str, repository: str, parts: list[dict], dependencies: list[dict]) -> dict:
    """A runtime package's SBOM: the package its root, under the licenses of all its parts; the parts its components;
    the dependencies, an empty entry of each file added. Every file of the package is listed (complete); not what the
    libraries load from the platform, the C and C++ runtime, the system's libraries, OpenGL (incomplete)."""
    made_by = creator(package, repository)
    terms = []
    for part in parts:
        terms += [t for t in re.split(r"\s+AND\s+", part["licenses"][0]["expression"]) if t not in terms]
    root = root_reference(package)
    dependencies = dependencies + [{"ref": f["bom-ref"], "dependsOn": []} for p in parts for f in p.get("components", [])]
    return {"bomFormat": "CycloneDX", "specVersion": SPEC, "serialNumber": f"urn:uuid:{uuid.uuid4()}", "version": 1,
            "metadata": {"timestamp": timestamp(), "manufacturer": made_by,
                         "component": {"type": "library", "bom-ref": root, "manufacturer": made_by, "name": package,
                                       "version": "", "licenses": licenses(terms), "purl": root,
                                       "externalReferences": [{"type": "vcs", "url": repository}],
                                       "properties": bsi_properties(False, True, True)}},
            "components": parts,
            "dependencies": dependencies,
            "compositions": [{"aggregate": "complete", "assemblies": [root, *(p["bom-ref"] for p in parts)]},
                             {"aggregate": "incomplete", "dependencies": [d["ref"] for d in dependencies]}]}


def save(sbom: dict, path: Path) -> None:
    path.write_text(json.dumps(sbom, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def stamp(path: Path, version: str) -> None:
    """What pack knows: the package's version (in its purl and reference, and of the project's own libraries), its
    file name, each file's SHA-512 (of the stage beside the SBOM), and the SBOM's serial number and time. Exits when the
    stage's files are not the SBOM's."""
    if not path.is_file():
        sys.exit(f"{path} missing: run build.py native for its RID again")
    stage = path.parent / "native"
    sbom = json.loads(path.read_text(encoding="utf-8"))
    package = sbom["metadata"]["component"]
    old, new = package["bom-ref"], f"{root_reference(package['name'])}@{version}"
    package.update({"version": version, "bom-ref": new, "purl": new})
    package["properties"] = [{"name": "bsi:component:filename", "value": f"{package['name']}.{version}.nupkg"},
                             *(p for p in package["properties"] if p["name"] != "bsi:component:filename")]
    sbom["serialNumber"] = f"urn:uuid:{uuid.uuid4()}"
    sbom["metadata"]["timestamp"] = timestamp()
    rename = lambda ref: new if ref == old else ref
    for dependency in sbom["dependencies"]:
        dependency.update({"ref": rename(dependency["ref"]), "dependsOn": [rename(r) for r in dependency["dependsOn"]]})
    for composition in sbom.get("compositions", []):
        for key in ("assemblies", "dependencies"):
            if key in composition:
                composition[key] = [rename(r) for r in composition[key]]

    for component in sbom["components"]:
        if component["bom-ref"] == own_reference(package["name"]):
            for part in walk([component]):
                part["version"] = version
    listed = set()
    for component in walk(sbom["components"]):
        if component["type"] != "file":
            continue
        listed.add(component["name"])
        file = stage / component["name"]
        if not file.is_file():
            sys.exit(f"{component['name']} of {path} is not in {stage}: run build.py native for its RID again")
        hashes = [{"alg": "SHA-512", "content": hashlib.sha512(file.read_bytes()).hexdigest()}]
        component["hashes"] = hashes
        for reference in component.get("externalReferences", []):
            if reference["type"] == "distribution":
                reference["hashes"] = hashes
    unlisted = sorted(p.name for p in stage.iterdir() if p.name not in listed)
    if unlisted:
        sys.exit(f"{', '.join(unlisted)} of {stage} not in {path}: run build.py native for its RID again")
    save(sbom, path)


# --------------------------------------------------------------------------- the runtime package's SBOM
def license_terms(expression: str, port: str = "") -> list[str]:
    """The AND terms of the license a port ships under: its declaration, or the decision on it (LICENSE_DECISIONS),
    which an OR or a missing declaration needs first."""
    expression = LICENSE_DECISIONS.get(port, expression)
    if re.search(r"\bOR\b", expression) or expression == "NOASSERTION":
        sys.exit(f"{port}: the license {expression!r} is no decision: decide in LICENSE_DECISIONS which one NetOcc ships")
    return [term.strip("() ") for term in re.split(r"\s+AND\s+", expression.strip("() "))]


def head_commit(root: Path) -> str:
    try:
        return subprocess.run(["git", "rev-parse", "HEAD"], cwd=root, check=True, capture_output=True,
                              text=True).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        return "main"


def port_patches(port: str, spdx: dict, repository: str) -> list[dict]:
    """A port's patches of its library, where they are: vcpkg's at the recipe's commit, an overlay port's in this
    repository at the commit that builds them."""
    vcpkg = re.match(r"git\+https://github\.com/microsoft/vcpkg@([0-9a-f]+)$", spdx["recipe"])
    overlay = ROOT / "vcpkg" / "ports" / port
    if vcpkg:
        base = f"https://github.com/microsoft/vcpkg/blob/{vcpkg.group(1)}/ports/{port}"
    elif overlay.is_dir():
        base = f"{repository}/blob/{head_commit(ROOT)}/{overlay.relative_to(ROOT.parent).as_posix()}"
    else:
        return [{"type": "unofficial"} for _ in spdx["patches"]]
    return [{"type": "unofficial", "diff": {"url": f"{base}/{patch}"}} for patch in spdx["patches"]]


def port_component(prefix: Path, port: str, package: str, repository: str, description: str | None = None) -> dict:
    """A shipped port as a CycloneDX component, its reference named by the package first: another package's copy of the
    port (NetOcc.Vtk's VTK headers beside NetVtk's VTK) is another. Its creator is the port's homepage, else its source
    repository."""
    spdx = port_spdx(prefix, port)
    text = (prefix / "share" / port / "copyright").read_text(encoding="utf-8", errors="replace").strip()
    terms = license_terms(spdx["license"], port)
    repository_of = source(spdx["source"])
    creator = spdx["homepage"] or (repository_of[0] if repository_of else None)
    if not creator:
        sys.exit(f"{port}: vcpkg names neither a homepage nor a source repository, nothing that names its creator")
    ref = f"pkg:generic/{port}@{spdx['version']}"
    component = {"type": "library", "bom-ref": f"{package}/{ref}", "manufacturer": {"url": [creator]}, "name": port,
                 "version": spdx["version"]}
    if description:
        component["description"] = description
    component["licenses"] = licenses(terms)
    if copyright_text := copyright_lines(text):
        component["copyright"] = copyright_text
    component["purl"] = ref
    references = [{"type": "website", "url": spdx["homepage"]}] if spdx["homepage"] else []
    if spdx["source"] != "NOASSERTION":
        references += source_references(spdx["source"])
    if references:
        component["externalReferences"] = references
    component["evidence"] = evidence([(term, text) for term in terms])
    if patches := port_patches(port, spdx, repository):
        component["pedigree"] = {"patches": patches}
    return component


def write(stage: Path, target: Path, package: str, *, installed: Path, triplet: str, repository: str,
          compiled_in: dict[str, str] | None = None) -> None:
    """A runtime package's SBOM: the project's own libraries, every port of the vcpkg install whose libraries the stage
    holds and the ports compiled in without a library of theirs (name -> what of them), each with its files of the
    stage; what each port needs of the others as vcpkg installed it. Exits on a stage no port of the install ships a
    library of, and on a file that is no shared library."""
    prefix = installed / triplet
    ports = shipped_ports(installed, triplet, stage)
    if not set(ports) - set(COMPILED_IN):
        sys.exit(f"no port of {prefix} ships a library of {stage}: not the install it was built from")
    compiled_in = compiled_in or {}
    components = {port: port_component(prefix, port, package, repository,
                                       "header-only, compiled in" if port in COMPILED_IN else None) for port in ports}
    components.update({port: port_component(prefix, port, package, repository, what)
                       for port, what in compiled_in.items()})
    own = own_component(package, repository, f"{own_name(package)}'s native libraries, the C side of its bindings")

    # a staged library by the port that installed it (vcpkg's installed file lists), else it is the project's own
    owners = {}
    for listing in (installed / "vcpkg" / "info").glob(f"*_{triplet}.list"):
        port = listing.name.split("_", 1)[0]
        if port in components:
            for line in listing.read_text(encoding="utf-8").splitlines():
                if stem := library_stem(Path(line).name):
                    owners.setdefault(stem, components[port])

    def owner_of(name: str) -> dict:
        stem = library_stem(name)
        if stem is None:
            sys.exit(f"{name} in {stage} is no shared library: no library of the SBOM it belongs to")
        return owners.get(stem, own)

    add_files(stage, package, owner_of)
    needs = port_dependencies(installed)
    ref = lambda port: components[port]["bom-ref"]
    dependencies = [{"ref": root_reference(package),
                     "dependsOn": [own["bom-ref"], *(ref(p) for p in ports if p not in COMPILED_IN)]},
                    {"ref": own["bom-ref"], "dependsOn": [ref(p) for p in ["opencascade", *compiled_in] if p in components]},
                    *({"ref": ref(port), "dependsOn": [ref(p) for p in sorted(needs.get(port, ())) if p in components]}
                      for port in components)]
    save(document(package, repository, [own, *components.values()], dependencies), target)
