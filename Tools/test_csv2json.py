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


class RepoDataTest(unittest.TestCase):
    def test_repo_csv_converts(self):
        outputs = c.build_all()
        self.assertEqual(len(outputs), 3)


if __name__ == "__main__":
    unittest.main()
