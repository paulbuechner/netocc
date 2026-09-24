# SPDX-FileCopyrightText: 2026 Paul Büchner
# SPDX-License-Identifier: MIT
"""The class index: every type NetOcc gives C#, per OCCT module and package, linked to OCCT's reference manual.

Reads netocc-core's src/SWIG_files/modules.json and classes.json (netocc-gen writes both) and writes classes/*.md with
their toc.yml. Names and links only: no OCCT doc text.

    python classes.py            # before docfx
"""

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SWIG_FILES = ROOT.parent / "netocc-core" / "src" / "SWIG_files"
OUT = ROOT / "classes"
REFMAN = "https://dev.opencascade.org/doc/occt-8.0.1/refman/html/"

# how Doxygen spells names in file names: capitals as _ and the lower letter, other characters as codes
ESCAPES = {"_": "__", ":": "_1", "/": "_2", "<": "_3", ">": "_4", "*": "_5", "&": "_6", ".": "_8", ",": "_00", " ": "_01"}


def doxygen(name: str) -> str:
    return "".join(ESCAPES.get(c, f"_{c.lower()}" if c.isupper() else c) for c in name)


# NCollection_UBTree<int, Bnd_Box>::TreeNode as NCollection_UBTree::TreeNode: the manual documents templates
def without_arguments(cpp: str) -> str:
    text, depth = [], 0
    for c in cpp:
        depth += {"<": 1, ">": -1}.get(c, 0)
        if depth == 0 and c != ">":
            text.append(c)
    return "".join(text)


def scope_kind(scope: str, kinds: dict[str, str]) -> str:
    """class, struct or namespace. A namespace holding only enums has no entry of its own; OCCT names namespaces like
    packages, without a "_"."""
    return kinds.get(scope) or ("class" if "_" in scope else "namespace")


def link(entry: dict, kinds: dict[str, str]) -> str | None:
    """The reference manual's page of a type, or None where it has none (standard library templates)."""
    kind, cpp = entry["kind"], without_arguments(entry["cpp"])
    if cpp.startswith("std::"):
        return None
    if kind == "enum":
        # a nested enum is on its scope's page, a file-scope one on its header's
        if "::" not in cpp:
            return REFMAN + f"{doxygen(entry['header'])}.html"
        scope = cpp.rsplit("::", 1)[0]
        return REFMAN + f"{scope_kind(scope, kinds)}{doxygen(scope)}.html"
    return REFMAN + f"{kind}{doxygen(cpp)}.html"


# what the entry is, next to its name: a collection's or instance's template, an enum, a namespace
def note(entry: dict) -> str:
    if entry.get("instance"):
        return f" (instance of `{entry['cpp']}`)"
    return f" ({entry['kind']})" if entry["kind"] in ("enum", "namespace") else ""


def page(module: str, packages: list[str], types: dict[str, list[dict]], kinds: dict[str, str]) -> str:
    lines = [f"# {module}", "",
             f"The types of OCCT's {module} module in C#, each linked to its page in the OCCT 8.0.1 reference manual. "
             "C# names are OCCT's; a nested type's scopes are joined by `_`.", ""]
    for package in packages:
        entries = types.get(package, [])
        if not entries:
            continue
        lines += [f"## {package}", "", f"`OCC.Core.{package}`", ""]
        for entry in entries:
            url = link(entry, kinds)
            name = f"[{entry['name']}]({url})" if url else entry["name"]
            lines.append(f"- {name}{note(entry)}")
        lines.append("")
    return "\n".join(lines)


def main() -> None:
    modules: dict[str, list[str]] = json.loads((SWIG_FILES / "modules.json").read_text(encoding="utf-8"))
    types: dict[str, list[dict]] = json.loads((SWIG_FILES / "classes.json").read_text(encoding="utf-8"))
    # each C++ type's kind, templates by their name: a nested enum's page is its scope's
    kinds = {without_arguments(entry["cpp"]): entry["kind"] for entries in types.values() for entry in entries}
    OUT.mkdir(exist_ok=True)
    for stale in OUT.glob("*.md"):
        stale.unlink()
    toc = ["- name: Overview", "  href: index.md"]
    for module, packages in modules.items():
        (OUT / f"{module}.md").write_text(page(module, packages, types, kinds), encoding="utf-8", newline="\n")
        toc += [f"- name: {module}", f"  href: {module}.md"]
    count = sum(len(v) for v in types.values())
    (OUT / "index.md").write_text("\n".join([
        "# Class index", "",
        f"All {count} types NetOcc gives C#, by OCCT module and package. Each links to its page in the "
        f"[OCCT 8.0.1 reference manual]({REFMAN}); C# keeps OCCT's names, so the manual's pages apply as they are.", "",
        *[f"- [{module}]({module}.md): {sum(len(types.get(p, [])) for p in packages)} types" for module, packages in modules.items()], "",
    ]), encoding="utf-8", newline="\n")
    (OUT / "toc.yml").write_text("\n".join(toc) + "\n", encoding="utf-8", newline="\n")
    print(f"class index: {count} types in {len(modules)} modules -> {OUT}")


if __name__ == "__main__":
    main()
