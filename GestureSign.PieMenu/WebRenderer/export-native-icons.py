"""Export the pinned Kando font glyphs for the WinUI icon chooser."""

import json
from pathlib import Path
import re
import sys

from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont


def outlines(font, names):
    units = font["head"].unitsPerEm
    glyphs = font.getGlyphSet()
    paths = {}
    for name, glyph in names.items():
        pen = SVGPathPen(glyphs, ntos=lambda number: f"{number:.3f}".rstrip("0").rstrip("."))
        glyphs[glyph].draw(TransformPen(pen, (1, 0, 0, -1, 0, units)))
        paths[name] = pen.getCommands()
    return units, paths


def material_icons(directory):
    names = re.findall(r'"(.*?)"', (directory / "index.d.ts").read_text(encoding="utf-8"))
    with TTFont(directory / "material-symbols-rounded.woff2") as font:
        letters = {glyph: chr(code) for code, glyph in font.getBestCmap().items()
                   if chr(code) in "0123456789_abcdefghijklmnopqrstuvwxyz"}
        ligatures = {}
        for lookup in font["GSUB"].table.LookupList.Lookup:
            for entry in lookup.SubTable:
                table = getattr(entry, "ExtSubTable", entry)
                for first, substitutions in getattr(table, "ligatures", {}).items():
                    for substitution in substitutions:
                        name = "".join(letters.get(glyph, "?") for glyph in [first, *substitution.Component])
                        ligatures[name] = substitution.LigGlyph
        return outlines(font, {name: ligatures[name] for name in names})


def simple_icons(directory):
    metadata = json.loads((directory / "simple-icons.json").read_text(encoding="utf-8"))
    with TTFont(directory / "SimpleIcons.ttf") as font:
        cmap = font.getBestCmap()
        units, paths = outlines(font, {icon["slug"]: cmap[int(icon["code"], 16)] for icon in metadata})
    return units, {
        icon["slug"]: {
            "title": icon["title"],
            "color": "#" + icon["hex"],
            "path": paths[icon["slug"]],
        }
        for icon in metadata
    }


def main(modules, output):
    material_units, material = material_icons(modules / "material-symbols")
    simple_units, simple = simple_icons(modules / "simple-icons-font" / "font")
    emoji = json.loads((modules / "emojilib/dist/emoji-en-US.json").read_text(encoding="utf-8"))
    files = sorted(path.name for path in (output / "icons").glob("*.svg"))
    data = {"files": files, "materialUnits": material_units, "material": material,
            "simpleUnits": simple_units, "simple": simple, "emoji": emoji}
    (output / "native-icons.json").write_text(
        json.dumps(data, ensure_ascii=False, separators=(",", ":")), encoding="utf-8"
    )
    print(f"Kando icons: {len(files)} SVG, {len(material)} Material, {len(simple)} Simple, {len(emoji)} emoji")


if __name__ == "__main__":
    main(*map(Path, sys.argv[1:]))
