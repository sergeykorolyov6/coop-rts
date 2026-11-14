#!/usr/bin/env python3
"""Генерирует Docs/Design/maps/canyons.txt — первую большую игровую карту (192×192, 2–4 игрока,
plan-terrain-and-factions.md, этап T5): четыре плато игроков на юге, две базы Чёрного Легиона на
плато в северных углах, общая середина с котловиной, высокий массив на севере, карманы за породой.

Это заготовка, а не окончательный левел-дизайн: расстановка задана числами ниже, порода —
целочисленный шум, проходы прорублены по прямым. Результат зависит только от этого файла.
Когда карту начнут править руками (в тексте или в Tiled: python3 Tools/tiled.py export canyons),
генератор нужно удалить — иначе следующий запуск сотрёт правки.

    python3 Tools/make_canyons_map.py && python3 Tools/csv2json.py
"""
from pathlib import Path

SIZE = 192
COARSE = 7            # шаг решётки шума, клеток
ROCK_PERCENT = 60     # порог шума: ниже — порода
CORE_PERCENT = 47     # ниже — вечная скала; между порогами — кромка, её можно копать

# Плато игроков (уровень 2): левый край каждого; по вертикали у всех одинаково.
# Места 1 и 2 — в середине (вдвоём играют соседи), 3 и 4 — на флангах.
PLATEAU_W = 38
PLATEAU_Y0, PLATEAU_Y1 = 6, 43
PLATEAUS = {"3": 6, "1": 54, "2": 100, "4": 148}

# Плато баз Легиона (уровень 2) и левый нижний угол базы 20×20 на нём.
# flip: база развёрнута фронтом (турели, точка сбора) на юго-восток; иначе — на юго-запад.
LEGION = [
    {"x0": 12, "x1": 59, "y0": 144, "y1": 187, "origin": (28, 156), "flip": True},
    {"x0": 132, "x1": 179, "y0": 144, "y1": 187, "origin": (144, 156), "flip": False},
]
# База в своих координатах 0..19 — та же, что в m2.txt.
BASE = [
    ("legion", "legion_hq", 10, 10, 4), ("legion", "legion_factory", 4, 16, 3), ("legion", "legion_factory", 16, 4, 3),
    ("legion", "drill", 14, 10, 2), ("legion", "drill", 14, 12, 2), ("legion", "drill", 5, 12, 2), ("legion", "drill", 7, 12, 2),
    ("legion_line", 5, 11, 9, 11), ("legion", "drill_secondary", 10, 6, 2), ("legion_line", 10, 8, 10, 9),
    ("legion", "legion_turret", 1, 8, 2), ("legion", "legion_turret", 8, 1, 2), ("legion", "legion_turret", 4, 4, 2),
]
BASE_RALLY = (2, 2)

BASIN = (64, 76, 127, 107)        # котловина, уровень 0
TERRACE = (72, 144, 119, 155)     # северная терраса, уровень 2
MASSIF = (72, 156, 119, 187)      # массив, уровень 3


def mix(v: int) -> int:
    v &= 0xFFFFFFFF
    v ^= v >> 16
    v = (v * 0x7FEB352D) & 0xFFFFFFFF
    v ^= v >> 15
    v = (v * 0x846CA68B) & 0xFFFFFFFF
    return v ^ (v >> 16)


def lattice(gx: int, gy: int) -> int:
    return mix(gx * 73856093 ^ gy * 19349663 ^ 0x5EED) % 1000


def noise(x: int, y: int) -> int:
    """Билинейная интерполяция по решётке, 0..999, только целые."""
    gx, fx = divmod(x, COARSE)
    gy, fy = divmod(y, COARSE)
    a, b = lattice(gx, gy), lattice(gx + 1, gy)
    c, d = lattice(gx, gy + 1), lattice(gx + 1, gy + 1)
    top = a * (COARSE - fx) + b * fx
    bottom = c * (COARSE - fx) + d * fx
    return (top * (COARSE - fy) + bottom * fy) // (COARSE * COARSE)


def build() -> str:
    grid = [["."] * SIZE for _ in range(SIZE)]       # [y][x], y вверх
    level = [[1] * SIZE for _ in range(SIZE)]
    ramp = [[False] * SIZE for _ in range(SIZE)]

    def fill_level(x0, y0, x1, y1, lv):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                level[y][x] = lv

    def fill_ramp(x0, y0, w, h, lv):
        for y in range(y0, y0 + h):
            for x in range(x0, x0 + w):
                level[y][x] = lv
                ramp[y][x] = True

    # ---------- высоты ----------
    fill_level(*BASIN, 0)
    for px in PLATEAUS.values():
        fill_level(px, PLATEAU_Y0, px + PLATEAU_W - 1, PLATEAU_Y1, 2)
    for base in LEGION:
        fill_level(base["x0"], base["y0"], base["x1"], base["y1"], 2)
    fill_level(*TERRACE, 2)
    fill_level(*MASSIF, 3)

    ramp_mouths = []   # клетка у нижнего конца рампы: от неё прорубается подход

    # Плато игроков: две рампы на север и одна вбок, к соседу.
    for slot, px in PLATEAUS.items():
        for rx in (px + 6, px + 28):
            fill_ramp(rx, PLATEAU_Y1 + 1, 4, 2, 1)
            ramp_mouths.append((rx + 2, PLATEAU_Y1 + 5))
        if slot in ("3", "2"):
            fill_ramp(px + PLATEAU_W, 22, 2, 4, 1)
            ramp_mouths.append((px + PLATEAU_W + 3, 24))
        else:
            fill_ramp(px - 2, 22, 2, 4, 1)
            ramp_mouths.append((px - 4, 24))
    # Базы Легиона: две рампы на юг и одна внутрь карты.
    for base in LEGION:
        near, far = (base["x1"] - 15, base["x0"] + 8) if base["flip"] else (base["x0"] + 10, base["x1"] - 13)
        for rx in (near, far):
            fill_ramp(rx, base["y0"] - 2, 6, 2, 1)
            ramp_mouths.append((rx + 3, base["y0"] - 6))
        if base["flip"]:
            fill_ramp(base["x1"] + 1, 160, 2, 6, 1)
            ramp_mouths.append((base["x1"] + 5, 163))
        else:
            fill_ramp(base["x0"] - 2, 160, 2, 6, 1)
            ramp_mouths.append((base["x0"] - 6, 163))
    # Терраса: две рампы с низины; с террасы на массив — одна.
    for rx in (78, 108):
        fill_ramp(rx, TERRACE[1] - 2, 6, 2, 1)
        ramp_mouths.append((rx + 3, TERRACE[1] - 6))
    fill_ramp(93, TERRACE[3] - 1, 6, 2, 2)
    # Котловина: рампы внутри её края, с четырёх сторон.
    fill_ramp(92, BASIN[1], 8, 2, 0)
    fill_ramp(92, BASIN[3] - 1, 8, 2, 0)
    fill_ramp(BASIN[0], 88, 2, 8, 0)
    fill_ramp(BASIN[2] - 1, 88, 2, 8, 0)

    # ---------- порода ----------
    def rock_kind(x, y):
        n = noise(x, y)
        if n >= ROCK_PERCENT * 10:
            return "."
        if n < CORE_PERCENT * 10:
            return "#"
        h = mix(x * 7919 + y * 104729) % 100
        return "R" if h < 5 else "C" if h < 8 else "@" if h < 26 else "%"

    for y in range(SIZE):
        for x in range(SIZE):
            # На плато игроков, баз и на террасе породы нет: там строят.
            if level[y][x] == 2 or ramp[y][x]:
                continue
            grid[y][x] = rock_kind(x, y)

    def clear(cx, cy, r):
        for y in range(max(0, cy - r), min(SIZE, cy + r + 1)):
            for x in range(max(0, cx - r), min(SIZE, cx + r + 1)):
                # Only rock goes: ore laid earlier stays where it is.
                if (x - cx) ** 2 + (y - cy) ** 2 <= r * r and grid[y][x] in "#%@RC":
                    grid[y][x] = "."

    def corridor(a, b, half=3):
        (x, y), (bx, by) = a, b
        while x != bx:
            clear(x, y, half)
            x += 1 if bx > x else -1
        while y != by:
            clear(x, y, half)
            y += 1 if by > y else -1
        clear(x, y, half)

    # Магистрали по низине: три поперёк, три вдоль; средние идут через рампы котловины.
    for y in (52, 91, 134):
        corridor((8, y) if y != 91 else (24, y), (183, y) if y != 91 else (168, y))
    for x in (30, 96, 162):
        corridor((x, 48), (x, 138))
    # Проходы между плато игроков, куда выходят боковые рампы.
    corridor((49, 20), (49, 52))
    corridor((143, 20), (143, 52))
    # По массиву: от рампы вглубь и поперёк.
    corridor((96, 158), (96, 184), 2)
    corridor((76, 172), (116, 172), 2)
    for mx, my in ramp_mouths:
        clear(mx, my, 4)
    # Тупики у боковых рамп баз Легиона выходят на северную магистраль.
    corridor((66, 163), (66, 134), 2)
    corridor((126, 163), (126, 134), 2)

    camps, neutrals = [], []

    def ore(x, y, w, h, ch, link=None):
        clear(x + w // 2, y + h // 2, max(w, h) // 2 + 3)
        for yy in range(y, y + h):
            for xx in range(x, x + w):
                grid[yy][xx] = ch
        if link:
            corridor((x + w // 2, y + h // 2), link, 1)
            for yy in range(y, y + h):
                for xx in range(x, x + w):
                    grid[yy][xx] = ch

    def camp(kind, cells, link=None):
        cx, cy = cells[0]
        clear(cx, cy, 3)
        if link:
            corridor((cx, cy), link, 1)
        camps.append(f"camp {kind} {cx} {cy} creeps " + " ".join(f"{x} {y}" for x, y in cells))
        return len(camps)

    def neutral(building, x, y, size, guard=0, link=None):
        clear(x + size // 2, y + size // 2, size // 2 + 3)
        if link:
            corridor((x + size // 2, y + size // 2), link, 1)
        neutrals.append((building, x, y, size, guard))

    # ---------- плато игроков: штаб, два месторождения основного, одно второго ----------
    for slot, px in PLATEAUS.items():
        ore(px + 5, 30, 4, 4, "o")
        ore(px + 28, 12, 4, 4, "o")
        ore(px + 29, 32, 3, 3, "s")
    # ---------- южная низина: по месторождению на игрока, с охраной ----------
    ore(12, 58, 5, 4, "o", link=(14, 55))
    camp("small_camp", [(14, 65), (15, 65)])
    ore(171, 58, 5, 4, "o", link=(173, 55))
    camp("small_camp", [(176, 65), (177, 65)])
    ore(70, 60, 5, 4, "o", link=(72, 55))
    camp("guard_camp", [(72, 67), (73, 67)])
    neutral("drill", 73, 62, 2)
    ore(116, 60, 5, 4, "o", link=(118, 55))
    camp("guard_camp", [(118, 67), (119, 67)])
    neutral("drill", 116, 62, 2)
    # ---------- середина: второй ресурс на флангах, маяки по краям ----------
    ore(40, 99, 4, 4, "s", link=(41, 94))
    camp("guard_camp", [(42, 106), (43, 106)])
    neutral("drill", 40, 99, 2)
    ore(148, 99, 4, 4, "s", link=(149, 94))
    camp("guard_camp", [(149, 106), (150, 106)])
    neutral("drill", 150, 101, 2)
    neutral("lighthouse", 10, 90, 2, link=(24, 91))
    neutral("lighthouse", 180, 90, 2, link=(168, 91))
    # ---------- котловина: богатые месторождения, пещера, маяк ----------
    ore(70, 97, 8, 5, "o", link=(80, 94))
    camp("big_camp", [(67, 99), (67, 100), (68, 98)])
    ore(112, 80, 6, 5, "s", link=(112, 88))
    camp("big_camp", [(121, 81), (122, 81), (121, 82)])
    cave_guard = camp("cave_guard", [(86, 98), (87, 98), (86, 97), (87, 97)], link=(86, 94))
    neutral("cave", 88, 100, 3, guard=cave_guard, link=(93, 101))
    neutral("lighthouse", 103, 82, 2, link=(99, 83))
    # ---------- северная низина: подступы к базам Легиона ----------
    ore(8, 116, 6, 5, "o", link=(27, 118))
    camp("guard_camp", [(11, 123), (12, 123)])
    ore(178, 116, 6, 5, "o", link=(165, 118))
    camp("guard_camp", [(180, 123), (181, 123)])
    ore(86, 116, 4, 4, "s", link=(93, 118))
    camp("guard_camp", [(83, 121), (84, 121)])
    ore(103, 116, 5, 4, "o", link=(99, 118))
    camp("small_camp", [(110, 121), (111, 121)])
    neutral("drill", 104, 117, 2)
    # ---------- массив: самое богатое, под охраной ----------
    ore(78, 170, 8, 5, "o")
    camp("big_camp", [(88, 176), (89, 176), (88, 177)], link=(88, 172))
    ore(104, 170, 6, 5, "s")
    camp("big_camp", [(101, 176), (102, 176), (101, 177)], link=(101, 172))
    massif_guard = camp("cave_guard", [(94, 177), (95, 177), (96, 177), (97, 177)])
    neutral("cave", 94, 180, 3, guard=massif_guard)
    neutral("lighthouse", 86, 160, 2, link=(94, 161))

    # ---------- карманы за породой: скала кругом, вход — мягкая порода ----------
    def pocket(cx, cy, inside, door, lane_x):
        for y in range(cy - 4, cy + 5):
            for x in range(cx - 4, cx + 5):
                grid[y][x] = "#"
        for y in range(cy - 2, cy + 3):
            for x in range(cx - 2, cx + 3):
                grid[y][x] = "."
        for y in range(cy - 1, cy + 2):
            for x in range(cx - 1, cx + 2):
                grid[y][x] = inside
        step = -1 if door == "west" else 1
        for x in (cx + 3 * step, cx + 4 * step):
            grid[cy][x] = grid[cy + 1][x] = "%"
        # От входа до магистрали — открытый проход.
        x = cx + 5 * step
        while x != lane_x:
            for y in (cy, cy + 1):
                grid[y][x] = "."
            x += step

    pocket(44, 72, "s", "west", 33)
    pocket(148, 72, "s", "east", 159)
    pocket(50, 118, "C", "west", 33)
    pocket(142, 118, "C", "east", 159)

    # ---------- базы Легиона ----------
    legion_lines = []
    for base in LEGION:
        ox, oy = base["origin"]

        def place(x, w):
            return ox + (20 - x - w if base["flip"] else x)

        lines = []
        for item in BASE:
            if item[0] == "legion":
                _, building, x, y, w = item
                bx = place(x, w)
                if building.startswith("drill"):
                    for yy in range(oy + y, oy + y + 2):
                        for xx in range(bx, bx + 2):
                            grid[yy][xx] = "s" if building == "drill_secondary" else "o"
                    building = "drill"
                lines.append(f"legion {building} {bx} {oy + y}")
            else:
                _, x1, y1, x2, y2 = item
                lines.append(f"legion_line {place(x1, 1)} {oy + y1} {place(x2, 1)} {oy + y2}")
        lines.append(f"rally {place(BASE_RALLY[0], 1)} {oy + BASE_RALLY[1]}")
        legion_lines += lines

    # Край карты — вечная скала; старты — поверх всего.
    for i in range(SIZE):
        for j in (0, 1, SIZE - 2, SIZE - 1):
            grid[i][j] = grid[j][i] = "#"
    for slot, px in PLATEAUS.items():
        grid[23][px + 17] = slot

    rocks = sum(1 for row in grid for ch in row if ch in "#%@RC")
    header = [
        f"# «Каньоны» — карта этапа T5 (plan-terrain-and-factions.md): {SIZE}×{SIZE}, 2–4 игрока, две базы Чёрного Легиона. Порода {rocks * 100 // (SIZE * SIZE)} %.",
        "# Плато игроков — на юге (места 1 и 2 в середине, 3 и 4 на флангах), у каждого две рампы на север и одна к соседу.",
        "# Базы Легиона — на плато в северных углах: западная воюет с местами 1 и 3, восточная — с 2 и 4 (legion-ai.md, T5).",
        "# Посередине — котловина (уровень 0) с богатыми месторождениями, пещерой и маяком; на севере — массив (уровень 3) с самым богатым.",
        "# Сгенерировано Tools/make_canyons_map.py: это заготовка. Начнёте править руками — удалите генератор.",
        "# Легенда — как в m2.txt. Строка rally заканчивает базу Легиона: строки legion после неё — следующая база.",
        "map",
    ]
    body = ["".join(row) for row in reversed(grid)]
    levels = ["".join(chr(ord("a") + level[y][x]) if ramp[y][x] else str(level[y][x]) for x in range(SIZE)) for y in reversed(range(SIZE))]
    neutral_lines = [f"neutral {b} {x} {y}" + (f" guard {g}" if g else "") for b, x, y, _, g in neutrals]
    return "\n".join(header + body + ["end", "levels"] + levels + ["end"] + camps + neutral_lines + legion_lines) + "\n"


def main() -> None:
    text = build()
    out = Path(__file__).resolve().parent.parent / "Docs" / "Design" / "maps" / "canyons.txt"
    out.write_text(text, encoding="utf-8", newline="\n")
    print(f"{out.name}: {text.splitlines()[0]}; лагерей {text.count(chr(10) + 'camp ')}, нейтральных {text.count(chr(10) + 'neutral ')}")


if __name__ == "__main__":
    main()
