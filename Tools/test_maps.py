"""Layers, validation and Tiled for maps (plan-terrain-and-factions.md, T2). python3 -m unittest discover Tools"""
import json
import unittest

import csv2json as c
import tiled

TERRAIN = {".": {"passable": 1, "destructible": 0}, "#": {"passable": 0, "destructible": 0},
           "%": {"passable": 0, "destructible": 1}}
SIZES = {"headquarters": (1, 1), "drill": (2, 2), "conveyor": (1, 1), "cave": (1, 1)}


def convert(text, hq=(1, 1), camps=None):
    return c.convert_map(text, "m.txt", camps or {"g": 1}, hq, SIZES, TERRAIN)


class LayersTest(unittest.TestCase):
    def test_rock_kinds_and_levels_go_to_json_bottom_up(self):
        data = convert("map\n.%#.\n1...\nend\nlevels\n1100\n00a1\nend\n".replace("00a1", "0aa1"))
        self.assertEqual(data["rows_bottom_up"], ["1...", ".%#."])
        self.assertEqual(data["levels_bottom_up"], ["0aa1", "1100"])

    def test_flat_levels_block_is_dropped(self):
        self.assertNotIn("levels_bottom_up", convert("map\n1.\nend\nlevels\n00\nend\n"))

    def test_levels_wrong_size_or_symbol(self):
        with self.assertRaisesRegex(c.ConvertError, "levels 1 строк"):
            convert("map\n..\n..\nend\nlevels\n00\nend\n")
        with self.assertRaisesRegex(c.ConvertError, "не уровень"):
            convert("map\n..\nend\nlevels\n08\nend\n")
        with self.assertRaisesRegex(c.ConvertError, "не уровень"):
            convert("map\n..\nend\nlevels\n0h\nend\n")

    def test_unknown_rock_symbol(self):
        with self.assertRaisesRegex(c.ConvertError, "не из легенды"):
            convert("map\n.@\nend\n")

    def test_terrain_symbol_must_not_clash_with_ore(self):
        with self.assertRaisesRegex(c.ConvertError, "terrain.csv"):
            c.convert_map("map\n.\nend\n", "m.txt", {}, (1, 1), SIZES, {"o": {"passable": 0, "destructible": 0}})


class CheckTest(unittest.TestCase):
    def test_starts_must_reach_each_other_without_digging(self):
        with self.assertRaisesRegex(c.ConvertError, "нет пути к штабу игрока 2"):
            convert("map\n1.%.2\nend\n")
        convert("map\n1...2\n..%..\nend\n")

    def test_diagonal_does_not_cut_a_rock_corner(self):
        # (1, 1) → (2, 0) would squeeze between the rocks at (2, 1) and (1, 0).
        with self.assertRaisesRegex(c.ConvertError, "нет пути"):
            convert("map\n1.#.2\n.#...\nend\n")
        convert("map\n1...2\n.#...\nend\n")

    def test_rally_must_be_reachable(self):
        with self.assertRaisesRegex(c.ConvertError, "точке сбора"):
            convert("map\n1.#..\n..#..\nend\nlegion conveyor 4 0\nrally 3 0\n")

    # legion-ai.md, T5: several Legion bases — a rally line ends a base.
    def test_one_base_keeps_the_legion_key_two_bases_become_a_list(self):
        one = convert("map\n1.....\nend\nlegion conveyor 4 0\nrally 3 0\n")
        self.assertEqual(one["legion"], {"slots": [{"building": "conveyor", "x": 4, "y": 0, "dir": "right"}], "rally": [3, 0]})
        self.assertNotIn("legions", one)
        two = convert("map\n1.......\n........\nend\nlegion conveyor 4 0\nlegion_line 5 0 6 0\nrally 3 0\nlegion conveyor 4 1 up\nrally 3 1\n")
        self.assertNotIn("legion", two)
        self.assertEqual([len(b["slots"]) for b in two["legions"]], [3, 1])
        self.assertEqual([b["rally"] for b in two["legions"]], [[3, 0], [3, 1]])
        self.assertEqual(two["legions"][1]["slots"][0]["dir"], "up")

    def test_every_base_needs_its_own_rally_after_its_slots(self):
        with self.assertRaisesRegex(c.ConvertError, "нет точки сбора"):
            convert("map\n1.....\nend\nlegion conveyor 4 0\nrally 3 0\nlegion conveyor 5 0\n")
        with self.assertRaisesRegex(c.ConvertError, "rally"):
            convert("map\n1.....\nend\nrally 3 0\nlegion conveyor 4 0\n")
        with self.assertRaisesRegex(c.ConvertError, "rally"):
            convert("map\n1.....\nend\nlegion conveyor 4 0\nrally 3 0\nrally 2 0\n")

    def test_slots_of_two_bases_may_not_overlap_and_every_rally_must_be_reachable(self):
        with self.assertRaisesRegex(c.ConvertError, "уже занята"):
            convert("map\n1.....\nend\nlegion conveyor 4 0\nrally 3 0\nlegion conveyor 4 0\nrally 2 0\n")
        with self.assertRaisesRegex(c.ConvertError, r"точке сбора Легиона \(5, 0\)"):
            convert("map\n1...#.\n....#.\nend\nlegion conveyor 2 0\nrally 3 0\nlegion conveyor 5 1\nrally 5 0\n")

    def test_ore_behind_soft_rock_is_fine_behind_eternal_rock_is_not(self):
        convert("map\n1.%o\nend\n")
        with self.assertRaisesRegex(c.ConvertError, "руда в \\(3, 0\\)"):
            convert("map\n1.#o\nend\n")

    def test_camp_behind_eternal_rock(self):
        with self.assertRaisesRegex(c.ConvertError, "лагерь"):
            convert("map\n1.#.\nend\ncamp g 3 0 creeps 3 0\n")

    def test_cliff_blocks_ramp_joins(self):
        with self.assertRaisesRegex(c.ConvertError, "нет пути"):
            convert("map\n1..2\n....\nend\nlevels\n0011\n0011\nend\n")
        convert("map\n1...2\n.....\nend\nlevels\n00a11\n00a11\nend\n")

    def test_ramp_joins_its_level_with_the_one_above_only(self):
        # The ramp b lies between levels 1 and 2: from level 0 it is a cliff.
        with self.assertRaisesRegex(c.ConvertError, "нет пути"):
            convert("map\n1...2\n.....\nend\nlevels\n00b22\n00b22\nend\n")
        # Two ramps in a row climb two levels.
        convert("map\n1....2\n......\nend\nlevels\n00ab22\n00ab22\nend\n")

    def test_ramp_must_lead_up_and_be_two_wide(self):
        with self.assertRaisesRegex(c.ConvertError, "никуда не ведёт"):
            convert("map\n1...\n....\nend\nlevels\n0aa0\n0000\nend\n")
        # Two cells deep: the lower half touches the level above only through the upper half.
        convert("map\n1....\n.....\nend\nlevels\n0aa11\n0aa11\nend\n")
        with self.assertRaisesRegex(c.ConvertError, "одну клетку"):
            convert("map\n1...\n....\nend\nlevels\n00a1\n0001\nend\n")

    def test_buildings_stand_on_one_level_off_ramps(self):
        with self.assertRaisesRegex(c.ConvertError, "штаб игрока 1"):
            convert("map\n....\n1...\nend\nlevels\n0aa1\n0aa1\nend\n", hq=(2, 2))
        with self.assertRaisesRegex(c.ConvertError, "слот Легиона"):
            convert("map\n...oo\n1..oo\nend\nlevels\n00a11\n00a11\nend\nlegion drill 2 0\nrally 0 1\n")


class TiledTest(unittest.TestCase):
    SYMBOLS = [".", "#", "%"]

    def round_trip(self, text, hq=(1, 1)):
        tmj = tiled.to_tiled(text, self.SYMBOLS, hq, SIZES)
        tmj = json.loads(json.dumps(tmj))
        ts = tiled.tileset_json(self.SYMBOLS)
        return tiled.to_text(tmj, lambda source: ts, "m.tmj")

    def test_round_trip_keeps_the_map(self):
        text = ("map\n..%#..oo\n1.....oo\n........\nend\nlevels\n000aa111\n000aa111\n00000000\nend\n"
                "camp g 3 0 creeps 4 0\nneutral cave 5 0 guard 1\nlegion drill 6 1 up\nlegion_line 2 0 0 0\nlegion conveyor 7 0\nrally 2 1\n")
        back = self.round_trip(text)
        self.assertEqual(convert(back), convert(text))

    def test_two_bases_round_trip_every_slot_goes_to_the_nearest_rally(self):
        text = ("map\n1...........\n............\n............\nend\n"
                "legion conveyor 2 2\nlegion_line 3 2 4 2\nrally 2 1\n"
                "legion conveyor 9 1 up\nlegion conveyor 11 0\nrally 10 0\n")
        data = convert(self.round_trip(text))
        self.assertEqual(data, convert(text))
        self.assertEqual([len(b["slots"]) for b in data["legions"]], [3, 2])

    def test_every_map_of_the_repo_survives_tiled(self):
        _, symbols, sizes = tiled._tables()
        ts = tiled.tileset_json(symbols)
        camps = c.convert_table((c.DESIGN_DIR / "creep_camps.csv").read_text(encoding="utf-8-sig"), c.CREEP_CAMPS, "creep_camps.csv")
        camp_types = {x["id"]: len(x["creeps"]) for x in camps}
        rows = c.convert_table((c.DESIGN_DIR / "terrain.csv").read_text(encoding="utf-8-sig"), c.TERRAIN, "terrain.csv")
        terrain = {t["symbol"]: t for t in rows}
        for path in sorted(c.MAPS_DIR.glob("*.txt")):
            text = path.read_text(encoding="utf-8-sig")
            tmj = json.loads(json.dumps(tiled.to_tiled(text, symbols, sizes["headquarters"], sizes)))
            back = tiled.to_text(tmj, lambda source: ts, path.name)
            args = (camp_types, sizes["headquarters"], sizes, terrain)
            self.assertEqual(c.convert_map(back, path.name, *args), c.convert_map(text, path.name, *args), path.name)

    def test_base64_zlib_layer(self):
        import base64, struct, zlib
        tmj = tiled.to_tiled("map\n1.#\nend\n", self.SYMBOLS, (1, 1), SIZES)
        layer = tmj["layers"][0]
        layer["data"] = base64.b64encode(zlib.compress(struct.pack("<3I", *layer["data"]))).decode()
        layer["encoding"], layer["compression"] = "base64", "zlib"
        ts = tiled.tileset_json(self.SYMBOLS)
        self.assertIn("\n1.#\n", tiled.to_text(tmj, lambda s: ts, "m.tmj"))

    def test_errors_name_the_cell(self):
        ts = tiled.tileset_json(self.SYMBOLS)
        tmj = tiled.to_tiled("map\n1..\nend\n", self.SYMBOLS, (1, 1), SIZES)
        tmj["layers"][2]["objects"].append({"id": 9, "name": "ralli", "x": 64, "y": 0, "width": 32, "height": 32})
        with self.assertRaisesRegex(tiled.TiledError, "ralli.*\\(2, 0\\)"):
            tiled.to_text(tmj, lambda s: ts, "m.tmj")
        tmj = tiled.to_tiled("map\n1..\nend\n", self.SYMBOLS, (1, 1), SIZES)
        tmj["layers"][0]["data"][2] = 9   # a level tile on the terrain layer
        with self.assertRaisesRegex(tiled.TiledError, "terrain.*\\(2, 0\\)"):
            tiled.to_text(tmj, lambda s: ts, "m.tmj")
        tmj["infinite"] = True
        with self.assertRaisesRegex(tiled.TiledError, "Infinite"):
            tiled.to_text(tmj, lambda s: ts, "m.tmj")

    def test_point_object_takes_the_cell_it_is_in(self):
        ts = tiled.tileset_json(self.SYMBOLS)
        tmj = tiled.to_tiled("map\n...\n1..\nend\n", self.SYMBOLS, (1, 1), SIZES)
        tmj["layers"][2]["objects"].append({"id": 9, "name": "rally", "point": True, "x": 70.5, "y": 10.0, "width": 0, "height": 0})
        self.assertIn("rally 2 1", tiled.to_text(tmj, lambda s: ts, "m.tmj"))

    def test_tileset_in_the_repo_follows_terrain_csv(self):
        for path, data in tiled.tileset_files().items():
            self.assertTrue(path.exists(), f"нет {path.name}: python3 Tools/tiled.py tileset")
            self.assertEqual(path.read_bytes(), data, f"{path.name} устарел: python3 Tools/tiled.py tileset")


if __name__ == "__main__":
    unittest.main()
