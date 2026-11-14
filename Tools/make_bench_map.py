#!/usr/bin/env python3
"""Генерирует Docs/Design/maps/bench256.txt — стенд 256×256 для проверки масштаба
(plan-terrain-and-factions.md, этап T1). Это не игровая карта: порода — целочисленный шум,
проходы прорублены по прямым. Результат зависит только от этого файла.

    python3 Tools/make_bench_map.py && python3 Tools/csv2json.py
"""
from pathlib import Path

SIZE = 256
COARSE = 8          # шаг решётки шума, клеток
ROCK_PERCENT = 42   # порог шума; после прорубки проходов породы остаётся около трети
CORE_PERCENT = 33   # ниже этого порога — вечная скала, между порогами — мягкая порода
STARTS = {"1": (24, 24), "2": (90, 24), "3": (24, 90), "4": (90, 90)}
LEGION = (160, 160)  # сдвиг базы Легиона из m2.txt
RALLY = (218, 218)


def mix(v: int) -> int:
    v &= 0xFFFFFFFF
    v ^= v >> 16
    v = (v * 0x7FEB352D) & 0xFFFFFFFF
    v ^= v >> 15
    v = (v * 0x846CA68B) & 0xFFFFFFFF
    return v ^ (v >> 16)


def lattice(gx: int, gy: int) -> int:
    return mix(gx * 73856093 ^ gy * 19349663) % 1000


def noise(x: int, y: int) -> int:
    """Билинейная интерполяция по решётке, 0..999, только целые."""
    gx, fx = divmod(x, COARSE)
    gy, fy = divmod(y, COARSE)
    a, b = lattice(gx, gy), lattice(gx + 1, gy)
    c, d = lattice(gx, gy + 1), lattice(gx + 1, gy + 1)
    top = a * (COARSE - fx) + b * fx
    bottom = c * (COARSE - fx) + d * fx
    return (top * (COARSE - fy) + bottom * fy) // (COARSE * COARSE)


def main() -> None:
    # Ядро массива — вечная скала, кромка — мягкая порода: карманы за ней достижимы копанием.
    def rock(x, y):
        n = noise(x, y)
        return "#" if n < CORE_PERCENT * 10 else "%" if n < ROCK_PERCENT * 10 else "."

    grid = [[rock(x, y) for x in range(SIZE)] for y in range(SIZE)]

    def clear(cx, cy, r):
        for y in range(max(0, cy - r), min(SIZE, cy + r + 1)):
            for x in range(max(0, cx - r), min(SIZE, cx + r + 1)):
                if (x - cx) ** 2 + (y - cy) ** 2 <= r * r:
                    grid[y][x] = "."

    def corridor(a, b, half=2):
        (x, y), (bx, by) = a, b
        while x != bx:
            clear(x, y, half)
            x += 1 if bx > x else -1
        while y != by:
            clear(x, y, half)
            y += 1 if by > y else -1

    def ore(x, y, ch, w=4, h=4):
        clear(x + w // 2, y + h // 2, 5)
        for yy in range(y, y + h):
            for xx in range(x, x + w):
                grid[yy][xx] = ch

    for sx, sy in STARTS.values():
        clear(sx + 2, sy + 2, 16)
    clear(LEGION[0] + 66, LEGION[1] + 66, 26)
    order = list(STARTS.values())
    for a, b in ((0, 1), (0, 2), (1, 3), (2, 3)):
        corridor(order[a], order[b])
    for s in order:
        corridor(s, RALLY)
        corridor(RALLY, s)

    lines = []
    for slot, (sx, sy) in STARTS.items():
        ore(sx + 8, sy, "o")
        ore(sx - 2, sy + 9, "o")
        ore(sx + 9, sy + 9, "s", 3, 3)
    camps = []
    kinds = (("small_camp", 2), ("guard_camp", 2), ("big_camp", 3))
    k = 0
    for gy in range(1, 8):
        for gx in range(1, 8):
            x, y = gx * 32 + mix(gx * 31 + gy) % 9 - 4, gy * 32 + mix(gy * 17 + gx) % 9 - 4
            near_base = any(abs(x - sx) < 30 and abs(y - sy) < 30 for sx, sy in STARTS.values())
            if near_base or (x > 190 and y > 190):
                continue
            ore(x, y, "s" if (gx + gy) % 3 == 0 else "o")
            if (gx + gy) % 2 == 0:
                kind, count = kinds[k % 3]
                k += 1
                cx, cy = x + 7, y + 1
                clear(cx, cy, 3)
                cells = " ".join(f"{cx + i % 2} {cy + i // 2}" for i in range(count))
                camps.append(f"camp {kind} {cx} {cy} creeps {cells}")
    for slot, (sx, sy) in STARTS.items():
        grid[sy][sx] = slot

    ox, oy = LEGION
    legion = [
        ("legion", "legion_hq", 66, 66), ("legion", "legion_factory", 60, 72), ("legion", "legion_factory", 72, 60),
        ("legion", "drill", 70, 66), ("legion", "drill", 70, 68), ("legion", "drill", 61, 68), ("legion", "drill", 63, 68),
        ("legion_line", 61, 67, 65, 67), ("legion", "drill", 66, 62), ("legion_line", 66, 64, 66, 65),
        ("legion", "legion_turret", 57, 64), ("legion", "legion_turret", 64, 57), ("legion", "legion_turret", 60, 60),
    ]
    # Руда под бурами Легиона, как в m2.txt.
    for item in legion:
        if item[1] == "drill":
            for yy in range(item[3] + oy, item[3] + oy + 2):
                for xx in range(item[2] + ox, item[2] + ox + 2):
                    grid[yy][xx] = "o"
    for item in legion:
        if item[0] == "legion":
            lines.append(f"legion {item[1]} {item[2] + ox} {item[3] + oy}")
        else:
            lines.append(f"legion_line {item[1] + ox} {item[2] + oy} {item[3] + ox} {item[4] + oy}")

    rocks = sum(row.count("#") + row.count("%") for row in grid)
    header = [
        f"# Стенд масштаба {SIZE}×{SIZE} (plan-terrain-and-factions.md, T1): не игровая карта. Порода {rocks * 100 // (SIZE * SIZE)} %.",
        "# Сгенерировано Tools/make_bench_map.py — руками не править, править генератор.",
        "# Легенда — как в m2.txt.",
        "map",
    ]
    body = ["".join(row) for row in reversed(grid)]
    text = "\n".join(header + body + ["end"] + camps + lines + [f"rally {RALLY[0]} {RALLY[1]}"]) + "\n"
    out = Path(__file__).resolve().parent.parent / "Docs" / "Design" / "maps" / "bench256.txt"
    out.write_text(text, encoding="utf-8")
    print(f"{out.name}: порода {rocks * 100 // (SIZE * SIZE)} %, лагерей {len(camps)}")


if __name__ == "__main__":
    main()
