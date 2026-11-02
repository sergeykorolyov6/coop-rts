#!/usr/bin/env python3
"""Docs/Design/*.csv (human units) -> Assets/Data/*.json (integers for Game.Sim).

The brother writes seconds, cells and units/s; the simulation only takes integers:
ticks (20 per second) and milli-cells (1000 per cell). A value that does not
convert to a whole number is an error, not a silent rounding: rounding would
quietly change the balance.

    python3 Tools/csv2json.py           regenerate Assets/Data/*.json
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


def build_all() -> dict[Path, str]:
    out = {}
    for csv_name, json_name, schema in TABLES:
        text = (DESIGN_DIR / csv_name).read_text(encoding="utf-8-sig")
        out[DATA_DIR / json_name] = render(convert_table(text, schema, csv_name))
    rules = (DESIGN_DIR / "rules.csv").read_text(encoding="utf-8-sig")
    out[DATA_DIR / "rules.json"] = render(convert_rules(rules, "rules.csv"))
    return out


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
