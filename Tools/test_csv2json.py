"""python3 -m unittest discover Tools"""
import unittest

import csv2json as c


class ConvertersTest(unittest.TestCase):
    def test_seconds_to_ticks(self):
        self.assertEqual(c.seconds_to_ticks("10"), 200)
        self.assertEqual(c.seconds_to_ticks("0.8"), 16)
        self.assertEqual(c.seconds_to_ticks("1,5"), 30)  # Russian decimal comma

    def test_cells(self):
        self.assertEqual(c.cells_to_milli("0.5"), 500)
        self.assertEqual(c.cells_per_second_to_milli_per_tick("2.4"), 120)

    def test_fraction_is_an_error_not_rounding(self):
        with self.assertRaises(ValueError):
            c.seconds_to_ticks("0.33")
        with self.assertRaises(ValueError):
            c.cells_per_second_to_milli_per_tick("2.45")
        with self.assertRaises(ValueError):
            c.int_("1.5")

    def test_not_a_number(self):
        with self.assertRaises(ValueError):
            c.int_("много")

    def test_id_list(self):
        self.assertEqual(c.id_list("a; b;;c"), ["a", "b", "c"])
        self.assertEqual(c.id_list(""), [])


SCHEMA = {"id": ("id", c.string), "hp": ("hp", c.int_), "notes": None}


class TableTest(unittest.TestCase):
    def test_sorted_by_id_and_text_dropped(self):
        rows = c.convert_table("id,hp,notes\nb,2,x\na,1,y\n", SCHEMA, "t.csv")
        self.assertEqual(rows, [{"id": "a", "hp": 1}, {"id": "b", "hp": 2}])

    def test_unknown_column(self):
        with self.assertRaisesRegex(c.ConvertError, "неизвестные колонки"):
            c.convert_table("id,hp,notes,speed\na,1,,2\n", SCHEMA, "t.csv")

    def test_missing_column(self):
        with self.assertRaisesRegex(c.ConvertError, "нет колонок"):
            c.convert_table("id,notes\na,\n", SCHEMA, "t.csv")

    def test_duplicate_id(self):
        with self.assertRaisesRegex(c.ConvertError, "повторяется"):
            c.convert_table("id,hp,notes\na,1,\na,2,\n", SCHEMA, "t.csv")

    def test_error_names_line_and_column(self):
        with self.assertRaisesRegex(c.ConvertError, "строка 3, колонка hp"):
            c.convert_table("id,hp,notes\na,1,\nb,1.5,\n", SCHEMA, "t.csv")

    def test_crlf_and_blank_lines(self):
        rows = c.convert_table("id,hp,notes\r\na,1,\r\n,,\r\n", SCHEMA, "t.csv")
        self.assertEqual(rows, [{"id": "a", "hp": 1}])


HEAD = "id,value,unit,name_ru,source,notes\n"


class RulesTest(unittest.TestCase):
    def test_units_and_suffixes(self):
        rules = c.convert_rules(HEAD + "a,3,int,,,\nb,0.125,rate,,,\nc,4,cells_per_s,,,\nd_pct,50,pct,,,\ne,2,s,,,\n", "r.csv")
        self.assertEqual(rules, {"a": 3, "b_ticks_per_unit": 160, "c_mc_per_tick": 200, "d_pct": 50, "e_ticks": 40})

    def test_unknown_unit(self):
        with self.assertRaisesRegex(c.ConvertError, "единица"):
            c.convert_rules(HEAD + "a,3,meters,,,\n", "r.csv")

    def test_inexact_rate(self):
        with self.assertRaisesRegex(c.ConvertError, "строка 2"):
            c.convert_rules(HEAD + "a,0.3,rate,,,\n", "r.csv")

    def test_duplicate(self):
        with self.assertRaisesRegex(c.ConvertError, "повторяется"):
            c.convert_rules(HEAD + "a,1,int,,,\na,2,int,,,\n", "r.csv")


MAP = """# comment
map
#..o
1..s
end
camp guard x 1 1 creeps 1 1 2 1
"""


class MapTest(unittest.TestCase):
    def test_rows_bottom_up_starts_and_camps(self):
        m = c.convert_map(MAP.replace(" x ", " "), "m.txt", {"guard": 2}, (1, 1))
        self.assertEqual(m["rows_bottom_up"], ["1..s", "#..o"])
        self.assertEqual(m["starts"], [{"slot": 1, "x": 0, "y": 0}])
        self.assertEqual(m["camps"], [{"type": "guard", "x": 1, "y": 1, "creeps": [[1, 1], [2, 1]]}])

    def test_unknown_char(self):
        with self.assertRaisesRegex(c.ConvertError, "не из легенды"):
            c.convert_map("map\n.x\nend\n", "m.txt", {}, (1, 1))

    def test_ragged_rows(self):
        with self.assertRaisesRegex(c.ConvertError, "длина"):
            c.convert_map("map\n...\n..\nend\n", "m.txt", {}, (1, 1))

    def test_headquarters_must_fit(self):
        with self.assertRaisesRegex(c.ConvertError, "не помещается"):
            c.convert_map("map\n#.\n1.\nend\n", "m.txt", {}, (2, 2))

    def test_camp_with_wrong_creep_count(self):
        with self.assertRaisesRegex(c.ConvertError, "крипов"):
            c.convert_map("map\n...\nend\ncamp guard 0 0 creeps 0 0\n", "m.txt", {"guard": 2}, (1, 1))

    def test_unknown_camp_type(self):
        with self.assertRaisesRegex(c.ConvertError, "нет в creep_camps"):
            c.convert_map("map\n...\nend\ncamp wolves 0 0 creeps\n", "m.txt", {"guard": 0}, (1, 1))


class RepoDataTest(unittest.TestCase):
    def test_repo_csv_converts(self):
        outputs = c.build_all()
        self.assertEqual(len(outputs), 5)


if __name__ == "__main__":
    unittest.main()
