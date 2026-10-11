# SPDX-FileCopyrightText: 2026 Paul Büchner
# SPDX-License-Identifier: MIT

"""Writes assets/<name>.svg and assets/<name>-dark.svg from assets/<name>.html, here and in netocc-core and
netocc-generator: the diagrams the articles and READMEs show as images. The HTML is the source (diagram-design's layout,
default skin, presentation attributes only); the SVG is its <svg>, standalone, clipped to rounded corners, with subsets
of the fonts its text uses embedded, so it renders the same as an <img> on the site and on GitHub. The dark one maps the
skin's colors onto neutral blacks (DARK); the site's template/public/main.js shows it in the dark theme, the READMEs
through <picture>. The fonts come from google/fonts at a pinned commit, cached in log/fonts. Commit all three files.

  python diagrams.py              # every diagram
  python diagrams.py --png        # and log/diagrams/<name>[-dark].png through headless Edge or Chrome, to look at
  python diagrams.py --check      # fails when an SVG is missing or stale
"""
import argparse
import base64
import io
import re
import shutil
import subprocess
import sys
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

HERE = Path(__file__).resolve().parent
FOLDERS = [HERE / "assets", HERE.parent / "netocc-core" / "assets", HERE.parent / "netocc-generator" / "assets"]
CACHE = HERE / "log" / "fonts"
RENDERS = HERE / "log" / "diagrams"

FONTS_COMMIT = "bd8f81ddb5c74d5c8897b36ad88b440266245103"
# family -> (file in google/fonts, variable: weights are instanced from it)
FONTS = {
    ("Geist", "normal"): ("ofl/geist/Geist[wght].ttf", True),
    ("Geist Mono", "normal"): ("ofl/geistmono/GeistMono[wght].ttf", True),
    ("Instrument Serif", "normal"): ("ofl/instrumentserif/InstrumentSerif-Regular.ttf", False),
    ("Instrument Serif", "italic"): ("ofl/instrumentserif/InstrumentSerif-Italic.ttf", False),
}
SVG_NS = "http://www.w3.org/2000/svg"
WEIGHTS = {"normal": 400, "bold": 700}
RADIUS = 12

# The light skin's colors -> the dark variant's: neutral blacks and greys rather than the skill's blue-slate dark skin.
# The paper sits a step above the site's dark background (template/public/main.css), nodes a step above the paper.
PAPER, INK, ACCENT = (245, 245, 245), (45, 49, 66), (191, 69, 32)
DARK_PAPER, DARK_INK, DARK_ACCENT = (23, 23, 23), (230, 230, 230), (240, 138, 89)
DARK = {
    "#f5f5f5": "#171717",  # paper
    "#ffffff": "#1f1f1f",  # node fill
    "#2d3142": "#e6e6e6",  # ink
    "#4f5d75": "#a6a6a6",  # muted
    "#5a6580": "#8c8c8c",  # soft
    "#bf4520": "#f08a59",  # accent
    "#2e5aa8": "#739fdf",  # link
}
# rgba() bases -> (dark base, alpha factor): tints of the accent need more alpha on black to read
DARK_RGBA = {INK: (DARK_INK, 1.0), (79, 93, 117): ((166, 166, 166), 1.0), ACCENT: (DARK_ACCENT, 1.6)}


def font_file(path):
    local = CACHE / path.replace("/", "_")
    if not local.exists():
        CACHE.mkdir(parents=True, exist_ok=True)
        url = f"https://raw.githubusercontent.com/google/fonts/{FONTS_COMMIT}/{path.replace('[', '%5B').replace(']', '%5D')}"
        with urllib.request.urlopen(url) as response:
            local.write_bytes(response.read())
    return local


def style_props(element):
    props = {}
    for declaration in element.get("style", "").split(";"):
        if ":" in declaration:
            key, value = declaration.split(":", 1)
            props[key.strip()] = value.strip()
    for key in ("font-family", "font-weight", "font-style", "text-transform"):
        if element.get(key) is not None:
            props.setdefault(key, element.get(key))
    return props


def used_faces(svg):
    """(family, weight, style) -> the characters set in it, through inherited font attributes."""
    faces = {}

    def walk(element, inherited):
        props = {**inherited, **style_props(element)}
        tag = element.tag.split("}")[-1]
        if tag in ("title", "desc", "style", "defs"):
            return
        if tag in ("text", "tspan", "textPath"):
            if props.get("text-transform", "none") != "none":
                sys.exit(f"text-transform on {element.text!r}: write the case out, the subset needs the glyphs")
            family = props.get("font-family", "Geist").split(",")[0].strip().strip("'\"")
            weight = props.get("font-weight", "normal")
            weight = WEIGHTS.get(weight, None) or int(weight)
            face = (family, weight, props.get("font-style", "normal"))
            chars = faces.setdefault(face, set())
            chars.update(element.text or "")
            for child in element:
                walk(child, props)
                chars.update(child.tail or "")
            return
        for child in element:
            walk(child, props)

    walk(svg, {})
    return faces


def font_face(family, weight, style, chars):
    if (family, style) not in FONTS:
        sys.exit(f"no font for {family} {style}: use Geist, Geist Mono or Instrument Serif")
    path, variable = FONTS[(family, style)]
    font = TTFont(font_file(path))
    if variable:
        font = instancer.instantiateVariableFont(font, {"wght": weight}, updateFontNames=False)
    options = subset.Options()
    options.flavor = "woff"
    options.layout_features = ["kern", "liga", "calt"]
    options.name_IDs = []
    options.notdef_outline = True
    options.drop_tables += ["meta"]
    subsetter = subset.Subsetter(options)
    subsetter.populate(text="".join(sorted(chars | {" "})))
    subsetter.subset(font)
    out = io.BytesIO()
    font.flavor = "woff"
    font.recalcTimestamp = False  # the same input writes the same bytes
    font.save(out)
    data = base64.b64encode(out.getvalue()).decode("ascii")
    return (f"@font-face{{font-family:'{family}';font-weight:{weight};font-style:{style};"
            f"src:url(data:font/woff;base64,{data}) format('woff')}}")


def hex_color(rgb):
    return "#" + "".join(f"{round(c):02x}" for c in rgb)


def blend(under, over, alpha):
    return tuple(u + (o - u) * min(alpha, 1.0) for u, o in zip(under, over))


def dark_color(color, html):
    color = color.lower()
    if color in DARK:
        return DARK[color]
    rgb = tuple(int(color[i:i + 2], 16) for i in (1, 3, 5))
    if min(rgb) >= 200:  # a tint composited over the paper, as a label mask on a zone: composite the dark tint
        if rgb[0] - rgb[2] > 6:
            return hex_color(blend(DARK_PAPER, DARK_ACCENT, (PAPER[2] - rgb[2]) / (PAPER[2] - ACCENT[2]) * 1.6))
        return hex_color(blend(DARK_PAPER, DARK_INK, (PAPER[0] - sum(rgb) / 3) / (PAPER[0] - sum(INK) / 3)))
    sys.exit(f"{html}: {color} has no dark counterpart: use the skin's colors or add it to DARK")


def dark_rgba(match, html):
    rgb = tuple(int(match.group(i)) for i in (1, 2, 3))
    alpha = float(match.group(4))
    if rgb not in DARK_RGBA:
        sys.exit(f"{html}: {match.group(0)} has no dark counterpart: use the skin's colors or add it to DARK_RGBA")
    base, factor = DARK_RGBA[rgb]
    return f"rgba({','.join(str(c) for c in base)},{min(alpha * factor, 1.0):.2f})"


def darken(svg, html):
    svg = re.sub(r"(?<=[\"'\s:,])#[0-9a-fA-F]{6}\b(?![\w-])", lambda m: dark_color(m.group(0), html), svg)
    return re.sub(r"rgba\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*([\d.]+)\s*\)", lambda m: dark_rgba(m, html), svg)


def finish(svg, style, width, height):
    """The fonts and the rounded clip into <defs>, everything after it into the clipped group."""
    clip = f'<clipPath id="netocc-card"><rect width="{width}" height="{height}" rx="{RADIUS}"/></clipPath>'
    if "<defs>" in svg:
        svg = svg.replace("<defs>", "<defs>" + style + clip, 1)
    else:
        svg = svg.replace("</desc>", "</desc><defs>" + style + clip + "</defs>", 1)
    head, body = svg.split("</defs>", 1)
    body = body[:body.rindex("</svg>")]
    svg = f'{head}</defs><g clip-path="url(#netocc-card)">{body}</g></svg>'
    return '<?xml version="1.0" encoding="UTF-8"?>\n' + svg + "\n"


def export(html):
    """{"": the light SVG, "-dark": the dark one}"""
    source = html.read_text(encoding="utf-8")
    match = re.search(r"<svg\b.*?</svg>", source, re.S)
    if not match:
        sys.exit(f"{html}: no <svg>")
    svg = match.group(0)
    if "xmlns=" not in svg.split(">", 1)[0]:
        svg = svg.replace("<svg", f'<svg xmlns="{SVG_NS}"', 1)
    if re.search(r"\sclass=", svg):
        sys.exit(f"{html}: class attributes; style with presentation attributes, the SVG carries no page CSS")
    try:
        root = ET.fromstring(svg)
    except ET.ParseError as error:
        sys.exit(f"{html}: {error} (named HTML entities aren't XML: write &#160; for &nbsp;)")
    viewbox = root.get("viewBox", "").split()
    if len(viewbox) != 4:
        sys.exit(f"{html}: the <svg> needs a viewBox")
    width, height = viewbox[2], viewbox[3]
    head = svg.split(">", 1)[0]
    head = re.sub(r'\s(width|height)="[^"]*"', "", head)
    svg = f'{head} width="{width}" height="{height}">' + svg.split(">", 1)[1]
    faces = used_faces(root)
    css = "\n".join(font_face(*face, chars) for face, chars in sorted(faces.items()))
    style = f"<style>{css}</style>"
    return {"": finish(svg, style, width, height), "-dark": finish(darken(svg, html), style, width, height)}


def browser():
    for candidate in (shutil.which("msedge"), shutil.which("chrome"), shutil.which("chromium"),
                      r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                      r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
                      "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"):
        if candidate and Path(candidate).exists():
            return candidate
    sys.exit("--png needs Edge or Chrome")


def render(svg_path, exe):
    viewbox = re.search(r'viewBox="([^"]+)"', svg_path.read_text(encoding="utf-8")).group(1).split()
    RENDERS.mkdir(parents=True, exist_ok=True)
    png = RENDERS / (svg_path.stem + ".png")
    subprocess.run([exe, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--disable-lcd-text",
                    "--force-device-scale-factor=1.5",
                    f"--window-size={int(float(viewbox[2]))},{int(float(viewbox[3]))}",
                    f"--screenshot={png}", svg_path.as_uri()], check=True, capture_output=True)
    return png


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="fail on a missing or stale SVG, write nothing")
    parser.add_argument("--png", action="store_true", help="also render log/diagrams/<name>.png")
    parser.add_argument("names", nargs="*", help="only these diagrams (file stems)")
    args = parser.parse_args()
    stale = []
    exe = browser() if args.png else None
    for folder in FOLDERS:
        for html in sorted(folder.glob("*.html")):
            if args.names and html.stem not in args.names:
                continue
            for suffix, svg in export(html).items():
                target = html.with_name(html.stem + suffix + ".svg")
                current = target.read_text(encoding="utf-8") if target.exists() else None
                if args.check:
                    if current != svg:
                        stale.append(target)
                    continue
                if current != svg:
                    target.write_text(svg, encoding="utf-8", newline="\n")
                    print(f"wrote {target.relative_to(HERE.parent)} ({len(svg) // 1024} KiB)")
                if exe:
                    print(f"  {render(target, exe).relative_to(HERE.parent)}")
    if stale:
        sys.exit("stale, run python diagrams.py: " + ", ".join(str(p.relative_to(HERE.parent)) for p in stale))


if __name__ == "__main__":
    main()
