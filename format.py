#!/usr/bin/env python3
# SPDX-FileCopyrightText: 2026 Paul Büchner
# SPDX-License-Identifier: MIT

"""The monorepo's C# formatting: OCCT's clang-format style as far as C# has it (.editorconfig), applied by ReSharper's
formatter (the dotnet tool pinned in .config/dotnet-tools.json), then comments wrapped at the column limit, which the
formatter leaves alone. Generated code (*.g.cs, SWIG's output) and the generator's golden fixtures keep their layout.

    python format.py            # format the hand-written C#
    python format.py --check    # fail if a file isn't formatted (CI: .github/workflows/format.yml)
"""
from __future__ import annotations

import argparse
import hashlib
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
LIMIT = 100
# one project over every hand-written file, so the formatter needs no restore of the real ones (their packages, the
# packed NetOcc), and a solution around it, which the formatter needs; both at the root, since it formats only the files
# below the solution. Its targets define both sides of the code's #if NETFRAMEWORK and #if NET5_0_OR_GREATER.
SOLUTION = ROOT / "NetOcc.Format.slnx"
WORK = ROOT / ".format"


def sources() -> list[Path]:
    """The hand-written C#, tracked or new: not generated code or the generator's golden fixtures."""
    listed = subprocess.run(["git", "ls-files", "--cached", "--others", "--exclude-standard", "*.cs"], cwd=ROOT, check=True,
                            capture_output=True, text=True).stdout.split()
    return [ROOT / f for f in sorted(set(listed)) if not f.endswith(".g.cs") and "/Golden/" not in f and (ROOT / f).is_file()]


def reformat(files: list[Path]) -> None:
    """ReSharper's Reformat Code over the files, through a temporary project and solution."""
    items = "\n".join(f'    <Compile Include="..\\{f.relative_to(ROOT)}" />' for f in files)
    WORK.mkdir(exist_ok=True)
    project = WORK / "NetOcc.Format.csproj"
    project.write_text(f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net10.0;net35</TargetFrameworks>
    <EnableDefaultItems>false</EnableDefaultItems>
    <LangVersion>latest</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
{items}
  </ItemGroup>
</Project>
""", encoding="utf-8")
    SOLUTION.write_text('<Solution>\n  <Project Path=".format/NetOcc.Format.csproj" />\n</Solution>\n', encoding="utf-8")
    # no MSBuild nodes left behind, whose working directory would keep the folder from being deleted
    env = dict(os.environ, MSBUILDDISABLENODEREUSE="1")
    try:
        subprocess.run(["dotnet", "tool", "restore"], cwd=ROOT, check=True, stdout=subprocess.DEVNULL, env=env)
        subprocess.run(["dotnet", "restore", str(project)], cwd=ROOT, check=True, stdout=subprocess.DEVNULL, env=env)
        with tempfile.TemporaryDirectory() as caches:
            subprocess.run(["dotnet", "jb", "cleanupcode", str(SOLUTION), "--profile=Built-in: Reformat Code", "--no-build",
                            "--no-buildin-settings", f"--caches-home={caches}", "--verbosity=WARN"], cwd=ROOT, check=True, env=env)
    finally:
        SOLUTION.unlink(missing_ok=True)
        shutil.rmtree(WORK, ignore_errors=True)


# --------------------------------------------------------------------------- comments
# a full-line comment: its indent, // or ///, and its text after one space
COMMENT = re.compile(r"^(?P<indent> *)(?P<marker>///|//)(?: (?P<text>.*))?$")
# a word, with the tags in it whole (<see cref="X"/> has a space)
TOKEN = re.compile(r"(?:<[^<>\n]*>|[^\s<])+|\S")
# an item of a list in a comment: "- ", "* ", "1. "
ITEM = re.compile(r"(?:[-*+]|\d+\.) ")
# the block-level tags of documentation comments, which begin and end paragraphs; the inline ones (<see>, <c>) don't
BLOCK = r"(?:summary|remarks|returns|param|typeparam|value|exception|example|para|list|listheader|item|term|description|code|seealso)"
# a line of a documentation comment that is a block tag alone (<summary>, </remarks>): it stays on its line
TAG_LINE = re.compile(rf"</?{BLOCK}(?:\s[^<>]*)?/?>")
# a paragraph that is one element: <summary>text</summary>, <param name="x">text</param>
ELEMENT = re.compile(rf"(?P<open><(?P<tag>{BLOCK})(?:\s[^<>]*)?>)(?P<text>.*)(?P<close></(?P=tag)>)")


def wrap(indent: str, marker: str, texts: list[str]) -> list[str]:
    """A paragraph's lines at the limit, a list item's continuation under its text. A documentation element on one line
    that is too long becomes its tags' lines around the wrapped text, as the longer ones are written."""
    prefix = f"{indent}{marker} "
    element = ELEMENT.fullmatch(texts[0].strip()) if marker == "///" and len(texts) == 1 else None
    if element:
        return [prefix + element["open"], *wrap(indent, marker, [element["text"]]), prefix + element["close"]]
    item = ITEM.match(texts[0])
    hanging = " " * (item.end() if item else 0)
    lines: list[str] = []
    line = ""
    for token in TOKEN.findall(" ".join(t.strip() for t in texts)):
        candidate = f"{line} {token}" if line else token
        if line and len(prefix) + len(candidate) > LIMIT:
            lines.append(line)
            candidate = hanging + token
        line = candidate
    lines.append(line)
    return [prefix + line for line in lines]


def paragraphs(texts: list[str], doc: bool) -> list[tuple[int, int]]:
    """The reflowable runs of a comment's lines, as (start, end) indices: not blank lines, a documentation comment's tag
    lines and code, or the lines of different list items."""
    runs = []
    start = None
    code = False
    for i, text in enumerate(texts):
        stripped = text.strip()
        if doc and stripped.startswith("<code>"):
            code = True
        if code or not stripped or (doc and TAG_LINE.fullmatch(stripped)):
            if start is not None:
                runs.append((start, i))
            start = None
        elif start is None or ITEM.match(text) or (doc and re.match(rf"<{BLOCK}\b", stripped)):
            if start is not None:
                runs.append((start, i))
            start = i
        if doc and stripped.endswith("</code>"):
            code = False
        # a block's closing tag ends the paragraph (</param>, </summary>)
        if start is not None and doc and re.search(rf"</{BLOCK}>$", stripped):
            runs.append((start, i + 1))
            start = None
    if start is not None:
        runs.append((start, len(texts)))
    return runs


def wrap_comments(text: str) -> str:
    """The full-line comments past the limit wrapped at it, paragraph by paragraph; raw string literals (a test's C++
    header) untouched."""
    lines = text.split("\n")
    out: list[str] = []
    raw = False
    i = 0
    while i < len(lines):
        match = None if raw else COMMENT.match(lines[i])
        if match is None or match["text"] is None:
            raw ^= lines[i].count('"""') % 2 == 1
            out.append(lines[i])
            i += 1
            continue
        indent, marker = match["indent"], match["marker"]
        block = []
        while i < len(lines) and (m := COMMENT.match(lines[i])) and m["indent"] == indent and m["marker"] == marker:
            block.append(m["text"] or "")
            i += 1
        kept = 0
        for start, end in paragraphs(block, marker == "///"):
            out += [f"{indent}{marker} {t}".rstrip() for t in block[kept:start]]
            run = block[start:end]
            if any(len(indent) + len(marker) + 1 + len(t) > LIMIT for t in run):
                out += wrap(indent, marker, run)
            else:
                out += [f"{indent}{marker} {t}".rstrip() for t in run]
            kept = end
        out += [f"{indent}{marker} {t}".rstrip() for t in block[kept:]]
    return "\n".join(out)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="fail if a file isn't formatted")
    args = parser.parse_args()
    files = sources()
    before = {f: hashlib.sha256(f.read_bytes()).digest() for f in files}
    try:
        reformat(files)
    except subprocess.CalledProcessError as e:
        sys.exit(f"format.py: {' '.join(str(c) for c in e.cmd[:3])} failed (exit code {e.returncode})")
    for f in files:
        text = f.read_bytes().decode("utf-8")
        wrapped = wrap_comments(text)
        if wrapped != text:
            f.write_bytes(wrapped.encode("utf-8"))
    changed = [f for f in files if hashlib.sha256(f.read_bytes()).digest() != before[f]]
    for f in changed:
        print(f"{'not formatted' if args.check else 'formatted'}: {f.relative_to(ROOT).as_posix()}")
    if args.check and changed:
        sys.exit(f"format.py: {len(changed)} file(s) not formatted; run python format.py")


if __name__ == "__main__":
    main()
