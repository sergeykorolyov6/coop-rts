#!/usr/bin/env python3
"""Карты в редакторе Tiled (mapeditor.org) — plan-terrain-and-factions.md, этап T2.

Tiled — второй способ записать ту же карту, что и Docs/Design/maps/*.txt: файл *.tmj
превращается в тот же текст (to_text), дальше его разбирает и проверяет csv2json.convert_map.
Одна карта — один файл: либо <имя>.txt, либо <имя>.tmj.

    python3 Tools/tiled.py tileset         пересоздать maps/cooprts.tsj и cooprts.png из terrain.csv
    python3 Tools/tiled.py export <имя>    maps/<имя>.txt → maps/<имя>.tmj (после этого .txt удалить)

Слои карты в Tiled:
    terrain  — тайлы земли, руды и породы; пустая клетка — земля;
    levels   — тайлы уровней 0–7 и рамп; пустая клетка — уровень 0; слоя может не быть;
    objects  — объекты, смысл в имени (Name), место — клетка левого нижнего угла:
        start 1                    штаб игрока 1
        camp guard_camp            лагерь; его крипы — объекты creep, каждый уходит ближайшему лагерю
        creep
        legion legion_hq [up]      слот базы Легиона, порядок слотов — порядок создания объектов
        legion_line left           лента Легиона во всю длину прямоугольника, смотрит в указанную сторону
        neutral cave [guard]       нейтральное здание; guard — охраняет ближайший лагерь
        rally                      точка сбора Легиона; на карте с несколькими базами (legion-ai.md, T5)
                                   точек несколько, и каждый слот и лента уходят ближайшей

Только стандартная библиотека.
"""
from __future__ import annotations

import base64
import gzip
import json
import struct
import sys
import zlib
from pathlib import Path

TILE = 32
COLUMNS = 8
TILESET_NAME = "cooprts"
FLIP_MASK = 0x1FFFFFFF
MAX_LEVEL = 7

# 3×5 digits for the level tiles.
GLYPHS = {
    "0": "111101101101111", "1": "010110010010111", "2": "111001111100111", "3": "111001111001111",
    "4": "101101111001001", "5": "111100111001111", "6": "111100111101111", "7": "111001001001001",
}
COLORS = {
    ".": (96, 108, 88), "o": (214, 148, 66), "s": (88, 180, 216), "#": (40, 38, 36), "%": (128, 104, 80),
    "@": (86, 92, 110), "R": (150, 96, 40), "C": (50, 120, 150),
}


class TiledError(Exception):
    pass


# ---------- tileset ----------

def tiles(terrain_symbols: list[str]) -> list[dict]:
    """Every tile of the set, in order: {"symbol": c} for the terrain layer, {"level": c} for levels."""
    out = [{"symbol": "."}, {"symbol": "o"}, {"symbol": "s"}]
    out += [{"symbol": s} for s in terrain_symbols if s != "."]
    while len(out) % COLUMNS:
        out.append({})
    out += [{"level": str(i)} for i in range(MAX_LEVEL + 1)]
    out += [{"level": chr(ord("a") + i)} for i in range(MAX_LEVEL)]
    return out


def tileset_json(terrain_symbols: list[str]) -> dict:
    all_tiles = tiles(terrain_symbols)
    rows = -(-len(all_tiles) // COLUMNS)
    return {
        "columns": COLUMNS, "image": TILESET_NAME + ".png", "imageheight": rows * TILE, "imagewidth": COLUMNS * TILE,
        "margin": 0, "name": TILESET_NAME, "spacing": 0, "tilecount": rows * COLUMNS, "tiledversion": "1.10.2",
        "tileheight": TILE, "tilewidth": TILE, "type": "tileset", "version": "1.10",
        "tiles": [{"id": i, "properties": [{"name": k, "type": "string", "value": v}]}
                  for i, t in enumerate(all_tiles) for k, v in t.items()],
    }


def tileset_png(terrain_symbols: list[str]) -> bytes:
    all_tiles = tiles(terrain_symbols)
    rows = -(-len(all_tiles) // COLUMNS)
    width, height = COLUMNS * TILE, rows * TILE
    pixels = bytearray(width * height * 4)

    def put(x, y, rgba):
        i = (y * width + x) * 4
        pixels[i:i + 4] = bytes(rgba)

    for index, tile in enumerate(all_tiles):
        ox, oy = index % COLUMNS * TILE, index // COLUMNS * TILE
        if "symbol" in tile:
            s = tile["symbol"]
            r, g, b = COLORS.get(s, (zlib.crc32(s.encode()) & 255, zlib.crc32(s.encode()) >> 8 & 255, 120))
            for y in range(TILE):
                for x in range(TILE):
                    edge = x in (0, TILE - 1) or y in (0, TILE - 1)
                    put(ox + x, oy + y, (r * 3 // 4, g * 3 // 4, b * 3 // 4, 255) if edge else (r, g, b, 255))
        elif "level" in tile:
            c = tile["level"]
            ramp = c >= "a"
            level = ord(c) - ord("a") if ramp else int(c)
            # Transparent, so the terrain shows through: higher is lighter, a ramp is striped.
            for y in range(TILE):
                for x in range(TILE):
                    alpha = 20 + level * 22
                    if ramp and (x + y) // 4 % 2 == 0:
                        alpha = 0
                    put(ox + x, oy + y, (255, 255, 255, alpha))
            glyph = GLYPHS[str(level)]
            for gy in range(5):
                for gx in range(3):
                    if glyph[gy * 3 + gx] == "1":
                        for y in range(4):
                            for x in range(4):
                                put(ox + 10 + gx * 4 + x, oy + 6 + gy * 4 + y, (255, 230, 60, 255) if ramp else (20, 20, 20, 255))

    def chunk(kind: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    raw = b"".join(b"\x00" + bytes(pixels[y * width * 4:(y + 1) * width * 4]) for y in range(height))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


# ---------- text map ⇄ parts ----------

def split_text(text: str):
    """A maps/*.txt as (comment lines, grid rows top first, level rows top first or None, other lines)."""
    comments, grid, levels, lines, inside = [], [], [], [], None
    for raw in text.splitlines():
        line = raw.rstrip("\r")
        if inside:
            if line.strip() == "end":
                inside = None
            else:
                (grid if inside == "map" else levels).append(line)
        elif line.strip() in ("map", "levels"):
            inside = line.strip()
        elif line.strip().startswith("#"):
            comments.append(line)
        elif line.strip():
            lines.append(line.split())
    return comments, grid, (levels or None), lines


def join_text(comments, grid, levels, lines) -> str:
    out = list(comments) + ["map"] + grid + ["end"]
    if levels:
        out += ["levels"] + levels + ["end"]
    return "\n".join(out + lines) + "\n"


# ---------- export: text → tmj ----------

def to_tiled(text: str, terrain_symbols: list[str], hq_size: tuple, building_sizes: dict) -> dict:
    comments, grid, levels, lines = split_text(text)
    height, width = len(grid), len(grid[0])
    all_tiles = tiles(terrain_symbols)
    gid_of_symbol = {t["symbol"]: i + 1 for i, t in enumerate(all_tiles) if "symbol" in t}
    gid_of_level = {t["level"]: i + 1 for i, t in enumerate(all_tiles) if "level" in t}

    objects = []

    def add(name, x, y, w=1, h=1):
        # (x, y) is the bottom-left cell, y up; Tiled wants the top-left corner in pixels, y down.
        objects.append({"height": h * TILE, "id": len(objects) + 1, "name": name, "rotation": 0, "type": "",
                        "visible": True, "width": w * TILE, "x": x * TILE, "y": (height - y - h) * TILE})

    terrain_data = []
    for row_index, row in enumerate(grid):
        for x, ch in enumerate(row):
            if ch.isdigit():
                add(f"start {ch}", x, height - 1 - row_index, *hq_size)
                ch = "."
            terrain_data.append(0 if ch == "." else gid_of_symbol[ch])
    for parts in lines:
        kind = parts[0]
        if kind == "camp":
            add(f"camp {parts[1]}", int(parts[2]), int(parts[3]))
            coords = [int(v) for v in parts[5:]]
            for i in range(0, len(coords), 2):
                add("creep", coords[i], coords[i + 1])
        elif kind == "legion":
            direction = f" {parts[4]}" if len(parts) == 5 and parts[4] != "right" else ""
            add(f"legion {parts[1]}{direction}", int(parts[2]), int(parts[3]), *building_sizes[parts[1]])
        elif kind == "legion_line":
            x1, y1, x2, y2 = (int(v) for v in parts[1:5])
            direction = "right" if x2 > x1 else "left" if x2 < x1 else "up" if y2 > y1 else "down"
            add(f"legion_line {direction}", min(x1, x2), min(y1, y2), abs(x2 - x1) + 1, abs(y2 - y1) + 1)
        elif kind == "neutral":
            add(f"neutral {parts[1]}" + (" guard" if len(parts) == 6 else ""), int(parts[2]), int(parts[3]), *building_sizes[parts[1]])
        elif kind == "rally":
            add("rally", int(parts[1]), int(parts[2]))
        else:
            raise TiledError(f"непонятная строка «{' '.join(parts)}»")

    def layer(layer_id, name, data):
        return {"data": data, "height": height, "id": layer_id, "name": name, "opacity": 1, "type": "tilelayer",
                "visible": True, "width": width, "x": 0, "y": 0}

    layers = [layer(1, "terrain", terrain_data)]
    level_rows = levels or ["0" * width] * height
    layers.append(layer(2, "levels", [0 if ch == "0" else gid_of_level[ch] for row in level_rows for ch in row]))
    layers.append({"draworder": "topdown", "id": 3, "name": "objects", "objects": objects, "opacity": 1,
                   "type": "objectgroup", "visible": True, "x": 0, "y": 0})
    out = {
        "compressionlevel": -1, "height": height, "infinite": False, "layers": layers, "nextlayerid": 4,
        "nextobjectid": len(objects) + 1, "orientation": "orthogonal", "renderorder": "right-down",
        "tiledversion": "1.10.2", "tileheight": TILE, "tilewidth": TILE,
        "tilesets": [{"firstgid": 1, "source": TILESET_NAME + ".tsj"}], "type": "map", "version": "1.10", "width": width,
    }
    if comments:
        out["properties"] = [{"name": "comment", "type": "string", "value": "\n".join(comments)}]
    return out


# ---------- import: tmj → text ----------

def _layer_data(layer: dict, count: int, name: str) -> list[int]:
    data = layer.get("data")
    if isinstance(data, str):
        raw = base64.b64decode(data)
        compression = layer.get("compression", "")
        if compression == "zlib":
            raw = zlib.decompress(raw)
        elif compression == "gzip":
            raw = gzip.decompress(raw)
        elif compression:
            raise TiledError(f"{name}: сжатие «{compression}» не поддерживается — в свойствах карты выберите CSV или zlib")
        data = list(struct.unpack(f"<{len(raw) // 4}I", raw))
    if not isinstance(data, list) or len(data) != count:
        raise TiledError(f"{name}: слой «{layer.get('name')}» не того размера (бесконечные карты не поддерживаются)")
    return [g & FLIP_MASK for g in data]


def to_text(tmj: dict, load_tileset, name: str) -> str:
    """load_tileset(source) → the json of an external tileset the map refers to."""
    if tmj.get("infinite"):
        raise TiledError(f"{name}: бесконечная карта — в свойствах карты снимите Infinite")
    if tmj.get("orientation") != "orthogonal":
        raise TiledError(f"{name}: нужна ортогональная карта")
    width, height, tw, th = tmj["width"], tmj["height"], tmj["tilewidth"], tmj["tileheight"]

    meaning = {}
    for ts in tmj.get("tilesets", []):
        first = ts["firstgid"]
        body = load_tileset(ts["source"]) if "source" in ts else ts
        for tile in body.get("tiles", []):
            for prop in tile.get("properties", []):
                if prop["name"] in ("symbol", "level"):
                    meaning[first + tile["id"]] = (prop["name"], prop["value"])

    grid = [["."] * width for _ in range(height)]
    levels = [["0"] * width for _ in range(height)]
    objects = []
    seen = set()
    for layer in tmj["layers"]:
        kind, lname = layer.get("type"), layer.get("name")
        if kind == "objectgroup":
            objects += layer.get("objects", [])
            continue
        if kind != "tilelayer":
            raise TiledError(f"{name}: слой «{lname}» — не тайлы и не объекты")
        if lname not in ("terrain", "levels"):
            raise TiledError(f"{name}: слой тайлов «{lname}» — нужны имена terrain и levels")
        if lname in seen:
            raise TiledError(f"{name}: два слоя «{lname}»")
        seen.add(lname)
        want = "symbol" if lname == "terrain" else "level"
        target = grid if lname == "terrain" else levels
        for i, gid in enumerate(_layer_data(layer, width * height, name)):
            if gid == 0:
                continue
            x, row = i % width, i // width
            if gid not in meaning or meaning[gid][0] != want:
                raise TiledError(f"{name}: слой «{lname}», клетка ({x}, {height - 1 - row}) — тайл не из набора {TILESET_NAME} или не для этого слоя")
            target[row][x] = meaning[gid][1]
    if "terrain" not in seen:
        raise TiledError(f"{name}: нет слоя тайлов terrain")

    def cells(obj):
        """Bottom-left cell (y up) and size in cells."""
        if obj.get("point") or not obj.get("width") or not obj.get("height"):
            cx, top, w, h = int(obj["x"] // tw), int(obj["y"] // th), 1, 1
        else:
            cx, top = round(obj["x"] / tw), round(obj["y"] / th)
            w, h = max(1, round(obj["width"] / tw)), max(1, round(obj["height"] / th))
        return cx, height - top - h, w, h

    objects.sort(key=lambda o: o["id"])
    camps, creeps, lines, tail, at = [], [], [], [], {}
    for obj in objects:
        words = (obj.get("name") or "").split()
        x, y, w, h = cells(obj)
        where = f"{name}: объект «{obj.get('name')}» в клетке ({x}, {y})"
        if not words:
            raise TiledError(f"{name}: объект без имени в клетке ({x}, {y})")
        if not (0 <= x < width and 0 <= y < height):
            raise TiledError(f"{where} — вне карты")
        kind = words[0]
        if kind == "start" and len(words) == 2 and words[1].isdigit() and words[1] != "0" and len(words[1]) == 1:
            if grid[height - 1 - y][x] != ".":
                raise TiledError(f"{where} — штаб стоит левым нижним углом не на земле")
            grid[height - 1 - y][x] = words[1]
        elif kind == "camp" and len(words) == 2:
            camps.append({"type": words[1], "x": x, "y": y, "creeps": []})
        elif kind == "creep" and len(words) == 1:
            creeps.append((x, y, where))
        elif kind == "legion" and len(words) in (2, 3):
            lines.append(["legion", words[1], str(x), str(y)] + words[2:])
            at[len(lines) - 1] = (x, y)
        elif kind == "legion_line" and len(words) == 2 and words[1] in ("right", "left", "up", "down"):
            if (w > 1) == (h > 1):
                raise TiledError(f"{where} — лента идёт по прямой и длиннее одной клетки")
            ends = {"right": (x, y, x + w - 1, y), "left": (x + w - 1, y, x, y),
                    "up": (x, y, x, y + h - 1), "down": (x, y + h - 1, x, y)}[words[1]]
            if (words[1] in ("right", "left")) != (w > 1):
                raise TiledError(f"{where} — направление поперёк прямоугольника")
            lines.append(["legion_line"] + [str(v) for v in ends])
            at[len(lines) - 1] = (ends[0], ends[1])
        elif kind == "neutral" and len(words) in (2, 3) and words[2:] in ([], ["guard"]):
            tail.append((["neutral", words[1], str(x), str(y)], len(words) == 3, x, y, where))
        elif kind == "rally" and len(words) == 1:
            lines.append(["rally", str(x), str(y)])
        else:
            raise TiledError(f"{where} — непонятное имя; см. список в начале Tools/tiled.py")

    def nearest_camp(x, y, where):
        if not camps:
            raise TiledError(f"{where} — на карте нет ни одного лагеря (camp)")
        return min(range(len(camps)), key=lambda i: ((camps[i]["x"] - x) ** 2 + (camps[i]["y"] - y) ** 2, i))

    for x, y, where in creeps:
        camps[nearest_camp(x, y, where)]["creeps"] += [str(x), str(y)]
    camp_lines = [["camp", c["type"], str(c["x"]), str(c["y"]), "creeps"] + c["creeps"] for c in camps]
    neutral_lines = [parts + (["guard", str(nearest_camp(x, y, where) + 1)] if guard else []) for parts, guard, x, y, where in tail]

    comments = []
    for prop in tmj.get("properties", []):
        if prop.get("name") == "comment":
            comments = [line if line.startswith("#") else "# " + line for line in str(prop["value"]).splitlines()]
    level_rows = ["".join(r) for r in levels]
    if all(set(r) <= {"0"} for r in level_rows):
        level_rows = None
    rally = [l for l in lines if l[0] == "rally"]
    rest = [l for l in lines if l[0] != "rally"]
    if len(rally) > 1:
        # Several bases: every slot goes to the nearest rally point; a base is its slots, then its rally.
        def base_of(i):
            x, y = at[i]
            return min(range(len(rally)), key=lambda b: ((int(rally[b][1]) - x) ** 2 + (int(rally[b][2]) - y) ** 2, b))
        grouped = [[] for _ in rally]
        for i, line in enumerate(lines):
            if line[0] != "rally":
                grouped[base_of(i)].append(line)
        rest, rally = [l for b, group in enumerate(grouped) for l in group + [rally[b]]], []
    return join_text(comments, ["".join(r) for r in grid], level_rows,
                     [" ".join(l) for l in camp_lines + neutral_lines + rest + rally])


# ---------- command line ----------

def _tables():
    import csv2json as c
    read = lambda n: (c.DESIGN_DIR / n).read_text(encoding="utf-8-sig")
    # terrain.csv order, not the converter's sort by id: a new row goes to the end of the set.
    import csv, io
    symbols = [row["symbol"] for row in csv.DictReader(io.StringIO(read("terrain.csv")))]
    buildings = c.convert_table(read("buildings.csv"), c.BUILDINGS, "buildings.csv")
    sizes = {b["id"]: (b["size_w"], b["size_h"]) for b in buildings}
    return c, symbols, sizes


def tileset_files() -> dict[Path, bytes]:
    c, symbols, _ = _tables()
    return {
        c.MAPS_DIR / (TILESET_NAME + ".tsj"): (json.dumps(tileset_json(symbols), ensure_ascii=False, indent=1) + "\n").encode("utf-8"),
        c.MAPS_DIR / (TILESET_NAME + ".png"): tileset_png(symbols),
    }


def main(argv: list[str]) -> int:
    c, symbols, sizes = _tables()
    if argv[:1] == ["tileset"] and len(argv) == 1:
        for path, data in tileset_files().items():
            path.write_bytes(data)
            print(f"tiled: {path.relative_to(c.ROOT).as_posix()}")
        return 0
    if argv[:1] == ["export"] and len(argv) == 2:
        source = c.MAPS_DIR / (argv[1] + ".txt")
        if not source.exists():
            print(f"tiled: нет {source.relative_to(c.ROOT).as_posix()}", file=sys.stderr)
            return 1
        target = c.MAPS_DIR / (argv[1] + ".tmj")
        tmj = to_tiled(source.read_text(encoding="utf-8-sig"), symbols, sizes["headquarters"], sizes)
        target.write_text(json.dumps(tmj, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8", newline="\n")
        print(f"tiled: {target.relative_to(c.ROOT).as_posix()} — теперь удалите {source.name}: у карты должен быть один файл")
        return 0
    print(__doc__, file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    sys.exit(main(sys.argv[1:]))
