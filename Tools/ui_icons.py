#!/usr/bin/env python3
"""UI icons: white glyphs on black (generated in fal from Tools/ui_icons.csv) -> white on transparency.

    python3 Tools/ui_icons.py <folder with <id>.png from fal> [Assets/Resources/Icons]

Brightness becomes alpha (black is see-through, white is solid), the glyph is cropped to its
bounds with a margin, centred in a square and scaled to 128 px. The game tints the white
(Assets/UI/Hud.uss, -unity-background-image-tint-color). Prompt: see PROMPT below; one subject
per row of ui_icons.csv, model fal-ai/flux-2/flash, 512x512.
"""
import csv
import os
import sys

from PIL import Image

PROMPT = ("Sci-fi strategy game command icon. One solid flat pure white silhouette glyph on a solid pure black "
          "background, filling most of the square, centered. Bold simple geometric shapes, a few thin black cut lines "
          "inside for detail, readable at very small size. No shading, no gradient, no glow, no outline frame, no circle, "
          "no text, no letters. Subject: {subject}.")

SIZE = 128
MARGIN = 0.06
# Below this brightness a pixel is background; above WHITE it is fully solid.
BLACK, WHITE = 28, 220


def convert(source, target):
    grey = Image.open(source).convert("L")
    alpha = grey.point(lambda v: 0 if v <= BLACK else 255 if v >= WHITE else int((v - BLACK) * 255 / (WHITE - BLACK)))
    box = alpha.point(lambda v: 255 if v > 40 else 0).getbbox()
    if box is None:
        raise ValueError(f"{source}: empty picture")
    left, top, right, bottom = box
    side = max(right - left, bottom - top)
    side = int(side * (1 + 2 * MARGIN))
    square = Image.new("L", (side, side), 0)
    square.paste(alpha.crop(box), (int(side / 2 - (right - left) / 2), int(side / 2 - (bottom - top) / 2)))
    square = square.resize((SIZE, SIZE), Image.LANCZOS)
    icon = Image.new("RGBA", (SIZE, SIZE), (255, 255, 255, 0))
    icon.putalpha(square)
    icon.save(target)


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    source_dir = sys.argv[1]
    target_dir = sys.argv[2] if len(sys.argv) > 2 else "Assets/Resources/Icons"
    os.makedirs(target_dir, exist_ok=True)
    here = os.path.dirname(os.path.abspath(__file__))
    with open(os.path.join(here, "ui_icons.csv"), encoding="utf-8") as f:
        ids = [row["id"] for row in csv.DictReader(f)]
    missing = []
    for icon_id in ids:
        source = os.path.join(source_dir, icon_id + ".png")
        if not os.path.exists(source):
            missing.append(icon_id)
            continue
        convert(source, os.path.join(target_dir, icon_id + ".png"))
    print(f"{len(ids) - len(missing)} icons -> {target_dir}")
    if missing:
        print("no picture yet:", ", ".join(missing))
    return 0


if __name__ == "__main__":
    sys.exit(main())
