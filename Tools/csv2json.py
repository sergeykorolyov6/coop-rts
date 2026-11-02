#!/usr/bin/env python3
"""Docs/Design/*.csv (human units) -> Assets/Data/*.json (integers for Game.Sim).

The brother writes seconds, cells and units/s; the simulation only takes integers:
ticks (20 per second) and milli-cells (1000 per cell). A value that does not
convert to a whole number is an error, not a silent rounding: rounding would
quietly change the balance.

    python3 Tools/csv2json.py           regenerate Assets/Data/*.json (and maps/*.json from Docs/Design/maps/*.txt)
    python3 Tools/csv2json.py --check   exit 1 if the json is stale (for hooks/CI)

Standard library only, so it runs anywhere python3 does.
"""
from __future__ import annotations

import csv
import io
import json
import sys
from decimal import Decimal, InvalidOperation
from pathlib import Path

TICKS_PER_SECOND = 20
MILLI_PER_CELL = 1000

ROOT = Path(__file__).resolve().parent.parent
DESIGN_DIR = ROOT / "Docs" / "Design"
DATA_DIR = ROOT / "Assets" / "Data"


class ConvertError(Exception):
    pass


# Column kinds: (output key suffix, converter). Each converter gets the cell text
# and returns a json value, raising ValueError with a human message.

def _number(text: str) -> Decimal:
    try:
        return Decimal(text.strip().replace(",", "."))
    except InvalidOperation:
        raise ValueError(f"«{text}» — не число")


def _whole(value: Decimal, what: str) -> int:
    if value != value.to_integral_value():
        raise ValueError(f"получается {value} {what} — нужно целое; поправьте число")
    return int(value)


def int_(text: str) -> int:
    return _whole(_number(text), "")


def seconds_to_ticks(text: str) -> int:
    return _whole(_number(text) * TICKS_PER_SECOND, "тиков")


def cells_to_milli(text: str) -> int:
    return _whole(_number(text) * MILLI_PER_CELL, "милли-клеток")


def cells_per_second_to_milli_per_tick(text: str) -> int:
    return _whole(_number(text) * MILLI_PER_CELL / TICKS_PER_SECOND, "милли-клеток за тик")


def rate_to_ticks_per_unit(text: str) -> int:
    # 0,125 ед./с → 1 ед. каждые 160 тиков: an integer accumulator threshold, exact.
    rate = _number(text)
    if rate <= 0:
        raise ValueError(f"скорость {rate} должна быть больше нуля")
    return _whole(Decimal(TICKS_PER_SECOND) / rate, "тиков на единицу")


# rules.csv: one value per row, converted by its unit column.
RULE_UNITS = {
    "int": ("", int_),
    "pct": ("_pct", int_),
    "s": ("_ticks", seconds_to_ticks),
    "cells": ("_mc", cells_to_milli),
    "cells_per_s": ("_mc_per_tick", cells_per_second_to_milli_per_tick),
    "rate": ("_ticks_per_unit", rate_to_ticks_per_unit),
}

RULES_COLUMNS = ["id", "value", "unit", "name_ru", "source", "notes"]


def convert_rules(csv_text: str, file_name: str) -> dict:
    reader = csv.DictReader(io.StringIO(csv_text))
    if (reader.fieldnames or []) != RULES_COLUMNS:
        raise ConvertError(f"{file_name}: колонки должны быть {RULES_COLUMNS}")
    out = {}
    for line, record in enumerate(reader, start=2):
        rule_id = (record["id"] or "").strip()
        if not rule_id:
            continue
        unit = (record["unit"] or "").strip()
        if unit not in RULE_UNITS:
            raise ConvertError(f"{file_name}, строка {line}: единица «{unit}» неизвестна, есть {sorted(RULE_UNITS)}")
        suffix, convert = RULE_UNITS[unit]
        # pct ids already end in _pct; don't double the suffix.
        key = rule_id if rule_id.endswith(suffix) else rule_id + suffix
        if key in out:
            raise ConvertError(f"{file_name}, строка {line}: «{rule_id}» повторяется")
        try:
            out[key] = convert(record["value"] or "")
        except ValueError as e:
            raise ConvertError(f"{file_name}, строка {line}, {rule_id}: {e}") from None
    return dict(sorted(out.items()))


def string(text: str) -> str:
    return text.strip()


def id_list(text: str) -> list[str]:
    return [part.strip() for part in text.split(";") if part.strip()]


# column -> (json key, converter); None drops the column (design-only text).
UNITS = {
    "id": ("id", string),
    "name_ru": ("name_ru", string),
    "faction": ("faction", string),
    "hp": ("hp", int_),
    "armor": ("armor", int_),
    "damage": ("damage", int_),
    "damage_vs_buildings": ("damage_vs_buildings", int_),
    "attack_range": ("attack_range_mc", cells_to_milli),
    "attack_cooldown_s": ("attack_cooldown_ticks", seconds_to_ticks),
    "move_speed": ("move_speed_mc_per_tick", cells_per_second_to_milli_per_tick),
    "vision": ("vision_mc", cells_to_milli),
    "cost_primary": ("cost_primary", int_),
    "cost_secondary": ("cost_secondary", int_),
    "build_time_s": ("build_time_ticks", seconds_to_ticks),
    "produced_at": ("produced_at", string),
    "size": ("size_mc", cells_to_milli),
    "tags": ("tags", id_list),
    "reference": None,
    "notes": None,
}

BUILDINGS = {
    "id": ("id", string),
    "name_ru": ("name_ru", string),
    "faction": ("faction", string),
    "hp": ("hp", int_),
    "armor": ("armor", int_),
    # Footprints stay in whole cells: buildings snap to the grid.
    "size_w": ("size_w", int_),
    "size_h": ("size_h", int_),
    "cost_primary": ("cost_primary", int_),
    "cost_secondary": ("cost_secondary", int_),
    # Build work: each builder adds 1 per tick (drill.md, appendix).
    "build_time_s": ("build_time_ticks", seconds_to_ticks),
    "max_builders": ("max_builders", int_),
    "vision": ("vision_mc", cells_to_milli),
    "damage": ("damage", int_),
    "attack_range": ("attack_range_mc", cells_to_milli),
    "attack_cooldown_s": ("attack_cooldown_ticks", seconds_to_ticks),
    "produces": ("produces", id_list),
    "reference": None,
    "notes": None,
}

CREEP_CAMPS = {
    "id": ("id", string),
    "name_ru": ("name_ru", string),
    "creeps": ("creeps", id_list),
    "aggro_radius": ("aggro_radius_mc", cells_to_milli),
    "leash_radius": ("leash_radius_mc", cells_to_milli),
    # Kept as a percentage: the sim turns it into an exact accumulator (creep-camp.md).
    "regen_pct_per_s": ("regen_pct_per_s", int_),
    "calm_delay_s": ("calm_delay_ticks", seconds_to_ticks),
    "trophy_primary": ("trophy_primary", int_),
    "trophy_secondary": ("trophy_secondary", int_),
    "trophy_per_trip": ("trophy_per_trip", int_),
    "reference": None,
    "notes": None,
}

TABLES = [
    ("units.csv", "units.json", UNITS),
    ("buildings.csv", "buildings.json", BUILDINGS),
    ("creep_camps.csv", "creep_camps.json", CREEP_CAMPS),
]


def convert_table(csv_text: str, schema: dict, file_name: str) -> list[dict]:
    reader = csv.DictReader(io.StringIO(csv_text))
    header = reader.fieldnames or []
    unknown = [c for c in header if c not in schema]
    missing = [c for c in schema if c not in header]
    if unknown:
        raise ConvertError(f"{file_name}: неизвестные колонки {unknown} — добавьте их в схему Tools/csv2json.py")
    if missing:
        raise ConvertError(f"{file_name}: нет колонок {missing}")

    rows = []
    seen = set()
    # Line 1 is the header.
    for line, record in enumerate(reader, start=2):
        if not any((v or "").strip() for v in record.values()):
            continue
        row = {}
        for column, rule in schema.items():
            if rule is None:
                continue
            key, convert = rule
            try:
                row[key] = convert(record[column] or "")
            except ValueError as e:
                raise ConvertError(f"{file_name}, строка {line}, колонка {column}: {e}") from None
        if not row["id"]:
            raise ConvertError(f"{file_name}, строка {line}: пустой id")
        if row["id"] in seen:
            raise ConvertError(f"{file_name}, строка {line}: id «{row['id']}» повторяется")
        seen.add(row["id"])
        rows.append(row)

    # Sorted by id: diffs stay small and order never depends on how the csv was edited.
    rows.sort(key=lambda r: r["id"])
    return rows


def render(rows) -> str:
    return json.dumps(rows, ensure_ascii=False, indent=2) + "\n"


MAP_CHARS = {".": None, "#": "blocked", "o": 0, "s": 1}
MAPS_DIR = DESIGN_DIR / "maps"


def convert_map(text: str, file_name: str, camp_types: dict, hq_size: tuple) -> dict:
    """Docs/Design/maps/*.txt → map json. Rows are stored bottom-up: rows[y] is row y."""
    lines = text.splitlines()
    grid, camps, inside, done = [], [], False, False
    for number, raw in enumerate(lines, start=1):
        line = raw.rstrip("\r")
        if inside:
            if line.strip() == "end":
                inside, done = False, True
            else:
                grid.append((number, line))
            continue
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        if stripped == "map":
            if done:
                raise ConvertError(f"{file_name}, строка {number}: второй блок map")
            inside = True
            continue
        parts = stripped.split()
        if parts[0] == "camp":
            camps.append((number, parts))
            continue
        raise ConvertError(f"{file_name}, строка {number}: непонятная строка «{stripped}»")
    if inside or not grid:
        raise ConvertError(f"{file_name}: нет блока map … end")

    height = len(grid)
    width = len(grid[0][1])
    starts = {}
    for row_index, (number, row) in enumerate(grid):
        if len(row) != width:
            raise ConvertError(f"{file_name}, строка {number}: длина {len(row)}, а у первой строки {width}")
        y = height - 1 - row_index
        for x, ch in enumerate(row):
            if ch.isdigit() and ch != "0":
                if ch in starts:
                    raise ConvertError(f"{file_name}, строка {number}: старт {ch} второй раз")
                starts[ch] = (x, y)
            elif ch not in MAP_CHARS:
                raise ConvertError(f"{file_name}, строка {number}, x = {x}: символ «{ch}» не из легенды")

    rows = [row for _, row in reversed(grid)]

    def cell(x, y):
        return rows[y][x] if 0 <= x < width and 0 <= y < height else None

    w, h = hq_size
    for slot, (sx, sy) in sorted(starts.items()):
        for yy in range(sy, sy + h):
            for xx in range(sx, sx + w):
                c = cell(xx, yy)
                if c is None or (c != "." and (xx, yy) != (sx, sy)):
                    raise ConvertError(f"{file_name}: штаб игрока {slot} ({w}×{h} от {sx},{sy}) не помещается на свободную землю")

    out_camps = []
    for number, parts in camps:
        try:
            if len(parts) < 5 or parts[4] != "creeps":
                raise ValueError
            camp_type = parts[1]
            cx, cy = int(parts[2]), int(parts[3])
            coords = [int(v) for v in parts[5:]]
        except ValueError:
            raise ConvertError(f"{file_name}, строка {number}: ожидается camp <тип> <x> <y> creeps <x> <y> …") from None
        if camp_type not in camp_types:
            raise ConvertError(f"{file_name}, строка {number}: тип лагеря «{camp_type}» нет в creep_camps.csv")
        if len(coords) % 2 or len(coords) // 2 != camp_types[camp_type]:
            raise ConvertError(f"{file_name}, строка {number}: у «{camp_type}» {camp_types[camp_type]} крипов — нужно столько пар x y")
        creeps = [[coords[i], coords[i + 1]] for i in range(0, len(coords), 2)]
        for x, y in [[cx, cy]] + creeps:
            if cell(x, y) in (None, "#"):
                raise ConvertError(f"{file_name}, строка {number}: клетка ({x}, {y}) вне карты или в скале")
        out_camps.append({"type": camp_type, "x": cx, "y": cy, "creeps": creeps})

    return {
        "width": width,
        "height": height,
        "rows_bottom_up": rows,
        "starts": [{"slot": int(k), "x": v[0], "y": v[1]} for k, v in sorted(starts.items())],
        "camps": out_camps,
    }


def build_all() -> dict[Path, str]:
    out = {}
    for csv_name, json_name, schema in TABLES:
        text = (DESIGN_DIR / csv_name).read_text(encoding="utf-8-sig")
        out[DATA_DIR / json_name] = render(convert_table(text, schema, csv_name))
    rules = (DESIGN_DIR / "rules.csv").read_text(encoding="utf-8-sig")
    out[DATA_DIR / "rules.json"] = render(convert_rules(rules, "rules.csv"))

    camps = convert_table((DESIGN_DIR / "creep_camps.csv").read_text(encoding="utf-8-sig"), CREEP_CAMPS, "creep_camps.csv")
    camp_types = {c["id"]: len(c["creeps"]) for c in camps}
    buildings = convert_table((DESIGN_DIR / "buildings.csv").read_text(encoding="utf-8-sig"), BUILDINGS, "buildings.csv")
    hq = next(b for b in buildings if b["id"] == "headquarters")
    for path in sorted(MAPS_DIR.glob("*.txt")):
        data = convert_map(path.read_text(encoding="utf-8-sig"), "maps/" + path.name, camp_types, (hq["size_w"], hq["size_h"]))
        out[DATA_DIR / "maps" / (path.stem + ".json")] = render_map(data)
    return out


def render_map(data: dict) -> str:
    # One row per line keeps the json diffable and readable as a picture.
    rows = ",\n".join("    " + json.dumps(r) for r in data["rows_bottom_up"])
    head = {k: data[k] for k in ("width", "height")}
    tail = {k: data[k] for k in ("starts", "camps")}
    return ("{\n" + ",\n".join(f'  "{k}": {json.dumps(v)}' for k, v in head.items())
            + ',\n  "rows_bottom_up": [\n' + rows + "\n  ],\n"
            + ",\n".join(f'  "{k}": {json.dumps(v, ensure_ascii=False)}' for k, v in tail.items()) + "\n}\n")


def main(argv: list[str]) -> int:
    check = "--check" in argv
    try:
        outputs = build_all()
    except ConvertError as e:
        print(f"csv2json: {e}", file=sys.stderr)
        return 1

    stale = []
    for path, text in outputs.items():
        current = path.read_text(encoding="utf-8") if path.exists() else None
        if current == text:
            continue
        if check:
            stale.append(path.relative_to(ROOT).as_posix())
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            # newline="\n": the same bytes on every OS, so --check agrees everywhere.
            path.write_text(text, encoding="utf-8", newline="\n")
            print(f"csv2json: {path.relative_to(ROOT).as_posix()}")

    if stale:
        print("csv2json: устарели " + ", ".join(stale) + " — запустите python3 Tools/csv2json.py", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
