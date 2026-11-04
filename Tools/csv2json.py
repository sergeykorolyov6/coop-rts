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


def id_count_list(text: str) -> list[dict]:
    # "legion_raider:2;legion_gunner:3" -> [{"id": "legion_raider", "count": 2}, ...]
    out = []
    for part in id_list(text):
        name, sep, count = part.partition(":")
        if not sep or not name.strip():
            raise ValueError(f"«{part}» — нужно «id:число»")
        n = int_(count)
        if n <= 0:
            raise ValueError(f"«{part}» — число должно быть больше нуля")
        out.append({"id": name.strip(), "count": n})
    return out


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
    # factions.md: ability ids from abilities.csv.
    "abilities": ("abilities", id_list),
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
    # What the sim does with it beyond the common columns: headquarters, drill, conveyor, bridge,
    # storage, lighthouse; empty — nothing special (factory, turret and the rest follow from other columns).
    "behavior": ("behavior", string),
    "produces": ("produces", id_list),
    # factory.md M2: "conveyor" — pays from its own stock that belts fill; empty — from the owner's wallet.
    "supply": ("supply", string),
    # neutral-buildings.md: points of interest nobody can damage (1) or ordinary buildings (0).
    "invulnerable": ("invulnerable", int_),
    # UI only: "line" — placed by dragging a line, like a conveyor.
    "placement": ("placement", string),
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

# legion-ai.md: one row per number of players.
LEGION = {
    "players": ("players", int_),
    "start_primary": ("start_primary", int_),
    "start_secondary": ("start_secondary", int_),
    "start_workers": ("start_workers", int_),
    "worker_limit": ("worker_limit", int_),
    "army_limit": ("army_limit", int_),
    "income_pct": ("income_pct", int_),
    "wave_size_pct": ("wave_size_pct", int_),
    "wave_time_pct": ("wave_time_pct", int_),
    "rebuild_delay_s": ("rebuild_delay_ticks", seconds_to_ticks),
    "notes": None,
}

# legion-ai.md: one row per wave, for one player.
WAVES = {
    "wave": ("wave", int_),
    "time_s": ("time_ticks", seconds_to_ticks),
    "units": ("units", id_count_list),
    "target": ("target", string),
    "notes": None,
}

# Factions: who builds what, which builder and headquarters a player of the faction starts with.
FACTIONS = {
    "id": ("id", string),
    "name_ru": ("name_ru", string),
    "role": ("role", string),
    "builder": ("builder", string),
    "headquarters": ("headquarters", string),
    "buildings": ("buildings", id_list),
    "single_headquarters": ("single_headquarters", int_),
    "builder_respawn_s": ("builder_respawn_ticks", seconds_to_ticks),
    # Percent of max HP per second, kept as a percentage: the sim runs an exact accumulator.
    "unit_regen_pct_per_s": ("unit_regen_pct_per_s", int_),
    "building_regen_pct_per_s": ("building_regen_pct_per_s", int_),
    "regen_delay_s": ("regen_delay_ticks", seconds_to_ticks),
    "first_strike_pct": ("first_strike_pct", int_),
    "first_strike_delay_s": ("first_strike_delay_ticks", seconds_to_ticks),
    "on_kill_ability": ("on_kill_ability", string),
    "bounty_pct": ("bounty_pct", int_),
    "notes": None,
}

# factions.md: active abilities (blink — jump to a point, boost — a timed buff).
ABILITIES = {
    "id": ("id", string),
    "name_ru": ("name_ru", string),
    "kind": ("kind", string),
    "cooldown_s": ("cooldown_ticks", seconds_to_ticks),
    "range": ("range_mc", cells_to_milli),
    "duration_s": ("duration_ticks", seconds_to_ticks),
    "move_speed_pct": ("move_speed_pct", int_),
    "attack_speed_pct": ("attack_speed_pct", int_),
    "hp_cost_pct": ("hp_cost_pct", int_),
    "notes": None,
}

# mission.md: one row per mission; the menu lists them.
MISSIONS = {
    "id": ("id", string),
    "name_ru": ("name_ru", string),
    "map": ("map", string),
    "players": ("players", int_),
    "legion": ("legion", int_),
    "goal": ("goal", string),
    # Faction of each start slot in order; a slot past the list gets the default player faction.
    "factions": ("factions", id_list),
    # UI only: colour of each start slot (blue, red, yellow, green); a slot past the list keeps its own.
    "colors": ("colors", id_list),
    "notes": None,
}

# (csv, json, schema, key column)
TABLES = [
    ("units.csv", "units.json", UNITS, "id"),
    ("buildings.csv", "buildings.json", BUILDINGS, "id"),
    ("creep_camps.csv", "creep_camps.json", CREEP_CAMPS, "id"),
    ("legion.csv", "legion.json", LEGION, "players"),
    ("waves.csv", "waves.json", WAVES, "wave"),
    ("missions.csv", "missions.json", MISSIONS, "id"),
    ("factions.csv", "factions.json", FACTIONS, "id"),
    ("abilities.csv", "abilities.json", ABILITIES, "id"),
]


def convert_table(csv_text: str, schema: dict, file_name: str, key_column: str = "id") -> list[dict]:
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
        if row[key_column] == "":
            raise ConvertError(f"{file_name}, строка {line}: пустой {key_column}")
        if row[key_column] in seen:
            raise ConvertError(f"{file_name}, строка {line}: {key_column} «{row[key_column]}» повторяется")
        seen.add(row[key_column])
        rows.append(row)

    # Sorted by key: diffs stay small and order never depends on how the csv was edited.
    rows.sort(key=lambda r: r[key_column])
    return rows


def render(rows) -> str:
    return json.dumps(rows, ensure_ascii=False, indent=2) + "\n"


MAP_CHARS = {".": None, "#": "blocked", "o": 0, "s": 1}
MAPS_DIR = DESIGN_DIR / "maps"


DIRECTIONS = {"right": (1, 0), "up": (0, 1), "left": (-1, 0), "down": (0, -1)}


def convert_map(text: str, file_name: str, camp_types: dict, hq_size: tuple, building_sizes: dict | None = None) -> dict:
    """Docs/Design/maps/*.txt → map json. Rows are stored bottom-up: rows[y] is row y."""
    building_sizes = building_sizes or {}
    lines = text.splitlines()
    grid, camps, legion, neutrals, rally, inside, done = [], [], [], [], None, False, False
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
        if parts[0] in ("legion", "legion_line"):
            legion.append((number, parts))
            continue
        if parts[0] == "neutral":
            neutrals.append((number, parts))
            continue
        if parts[0] == "rally":
            try:
                if len(parts) != 3 or rally is not None:
                    raise ValueError
                rally = (number, int(parts[1]), int(parts[2]))
            except ValueError:
                raise ConvertError(f"{file_name}, строка {number}: ожидается одна строка rally <x> <y>") from None
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

    # legion-ai.md: base slots in file order (the rebuild order).
    slots, taken = [], {}
    for number, parts in legion:
        try:
            if parts[0] == "legion":
                if len(parts) not in (4, 5):
                    raise ValueError
                direction = parts[4] if len(parts) == 5 else "right"
                items = [(parts[1], int(parts[2]), int(parts[3]), direction)]
            else:
                if len(parts) != 5:
                    raise ValueError
                x1, y1, x2, y2 = (int(v) for v in parts[1:5])
                if (x1 != x2) == (y1 != y2):
                    raise ConvertError(f"{file_name}, строка {number}: legion_line — по прямой и не из одной клетки")
                dx, dy = (x2 > x1) - (x2 < x1), (y2 > y1) - (y2 < y1)
                direction = next(k for k, v in DIRECTIONS.items() if v == (dx, dy))
                n = max(abs(x2 - x1), abs(y2 - y1)) + 1
                items = [("conveyor", x1 + dx * i, y1 + dy * i, direction) for i in range(n)]
        except ValueError:
            raise ConvertError(f"{file_name}, строка {number}: ожидается legion <здание> <x> <y> [направление] или legion_line <x1> <y1> <x2> <y2>") from None
        for building, x, y, direction in items:
            if building not in building_sizes:
                raise ConvertError(f"{file_name}, строка {number}: здания «{building}» нет в buildings.csv")
            if direction not in DIRECTIONS:
                raise ConvertError(f"{file_name}, строка {number}: направление «{direction}», есть {list(DIRECTIONS)}")
            w, h = building_sizes[building]
            for yy in range(y, y + h):
                for xx in range(x, x + w):
                    c = cell(xx, yy)
                    if c in (None, "#") or c.isdigit():
                        raise ConvertError(f"{file_name}, строка {number}: «{building}» в ({x}, {y}) задевает клетку ({xx}, {yy}) вне карты, в скале или на старте")
                    if (xx, yy) in taken:
                        raise ConvertError(f"{file_name}, строка {number}: клетка ({xx}, {yy}) уже занята слотом из строки {taken[(xx, yy)]}")
                    taken[(xx, yy)] = number
            if building == "drill" and not any(cell(xx, yy) in ("o", "s") for yy in range(y, y + h) for xx in range(x, x + w)):
                raise ConvertError(f"{file_name}, строка {number}: бур в ({x}, {y}) не стоит на руде")
            slots.append({"building": building, "x": x, "y": y, "dir": direction})
    if slots and rally is None:
        raise ConvertError(f"{file_name}: у базы Легиона нет точки сбора (rally <x> <y>)")
    if rally is not None and cell(rally[1], rally[2]) in (None, "#"):
        raise ConvertError(f"{file_name}, строка {rally[0]}: точка сбора вне карты или в скале")

    # neutral-buildings.md: neutral <здание> <x> <y> [guard <номер лагеря с 1>].
    out_neutral = []
    for number, parts in neutrals:
        try:
            if len(parts) not in (4, 6) or (len(parts) == 6 and parts[4] != "guard"):
                raise ValueError
            building, x, y = parts[1], int(parts[2]), int(parts[3])
            guard = int(parts[5]) if len(parts) == 6 else 0
        except ValueError:
            raise ConvertError(f"{file_name}, строка {number}: ожидается neutral <здание> <x> <y> [guard <номер лагеря>]") from None
        if building not in building_sizes:
            raise ConvertError(f"{file_name}, строка {number}: здания «{building}» нет в buildings.csv")
        if guard < 0 or guard > len(out_camps):
            raise ConvertError(f"{file_name}, строка {number}: лагеря номер {guard} нет (их {len(out_camps)}, нумерация с 1)")
        w, h = building_sizes[building]
        for yy in range(y, y + h):
            for xx in range(x, x + w):
                c = cell(xx, yy)
                if c in (None, "#") or c.isdigit():
                    raise ConvertError(f"{file_name}, строка {number}: «{building}» в ({x}, {y}) задевает клетку ({xx}, {yy}) вне карты, в скале или на старте")
                if (xx, yy) in taken:
                    raise ConvertError(f"{file_name}, строка {number}: клетка ({xx}, {yy}) уже занята строкой {taken[(xx, yy)]}")
                taken[(xx, yy)] = number
        if building == "drill" and not any(cell(xx, yy) in ("o", "s") for yy in range(y, y + h) for xx in range(x, x + w)):
            raise ConvertError(f"{file_name}, строка {number}: бур в ({x}, {y}) не стоит на руде")
        item = {"building": building, "x": x, "y": y}
        if guard:
            item["guard"] = guard - 1
        out_neutral.append(item)

    out = {
        "width": width,
        "height": height,
        "rows_bottom_up": rows,
        "starts": [{"slot": int(k), "x": v[0], "y": v[1]} for k, v in sorted(starts.items())],
        "camps": out_camps,
    }
    if slots:
        out["legion"] = {"slots": slots, "rally": [rally[1], rally[2]]}
    if out_neutral:
        out["neutral"] = out_neutral
    return out


def build_all() -> dict[Path, str]:
    out = {}
    for csv_name, json_name, schema, key in TABLES:
        text = (DESIGN_DIR / csv_name).read_text(encoding="utf-8-sig")
        out[DATA_DIR / json_name] = render(convert_table(text, schema, csv_name, key))
    rules = (DESIGN_DIR / "rules.csv").read_text(encoding="utf-8-sig")
    out[DATA_DIR / "rules.json"] = render(convert_rules(rules, "rules.csv"))

    camps = convert_table((DESIGN_DIR / "creep_camps.csv").read_text(encoding="utf-8-sig"), CREEP_CAMPS, "creep_camps.csv")
    camp_types = {c["id"]: len(c["creeps"]) for c in camps}
    buildings = convert_table((DESIGN_DIR / "buildings.csv").read_text(encoding="utf-8-sig"), BUILDINGS, "buildings.csv")
    hq = next(b for b in buildings if b["id"] == "headquarters")
    sizes = {b["id"]: (b["size_w"], b["size_h"]) for b in buildings}
    for path in sorted(MAPS_DIR.glob("*.txt")):
        data = convert_map(path.read_text(encoding="utf-8-sig"), "maps/" + path.name, camp_types, (hq["size_w"], hq["size_h"]), sizes)
        out[DATA_DIR / "maps" / (path.stem + ".json")] = render_map(data)
    return out


def render_map(data: dict) -> str:
    # One row per line keeps the json diffable and readable as a picture.
    rows = ",\n".join("    " + json.dumps(r) for r in data["rows_bottom_up"])
    head = {k: data[k] for k in ("width", "height")}
    tail = {k: data[k] for k in ("starts", "camps", "legion", "neutral") if k in data}
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
