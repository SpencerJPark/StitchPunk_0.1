"""Pins the cheap, breakable logic around the Roslyn compile gate: the assembly map and the rsp
harvest/staleness helpers. Never runs a real Roslyn compile here (that is gate.py's own ~90s job).
"""
from __future__ import annotations

import os
import re
import sys
import tempfile
import unittest

_TESTS_DIRECTORY: str = os.path.dirname(os.path.abspath(__file__))
_DEVLOOP_TOP_LEVEL_DIRECTORY: str = os.path.dirname(_TESTS_DIRECTORY)
if _DEVLOOP_TOP_LEVEL_DIRECTORY not in sys.path:
    sys.path.insert(0, _DEVLOOP_TOP_LEVEL_DIRECTORY)

from devloop_tools import packages, rsp_harvest

_REPOSITORY_ROOT: str = os.path.abspath(os.path.join(_DEVLOOP_TOP_LEVEL_DIRECTORY, "..", ".."))


class TargetsForTests(unittest.TestCase):
    def test_dotsanimationtoolkit_targets_are_ordered_runtime_first_tests_last_with_real_paths(self) -> None:
        targets: list[packages.AssemblyTarget] = packages.targets_for("com.dotsanimationtoolkit")

        self.assertEqual(6, len(targets))
        self.assertEqual("DotsAnimationToolkit.Runtime", targets[0].assembly_name)
        self.assertIn("Tests", targets[-1].assembly_name)
        self.assertIn("Tests", targets[-2].assembly_name)

        for assembly_target in targets:
            for source_root in assembly_target.source_roots:
                absolute_source_root: str = os.path.join(_REPOSITORY_ROOT, source_root)
                self.assertTrue(
                    os.path.isdir(absolute_source_root),
                    f"{assembly_target.assembly_name}: source root {source_root!r} does not exist",
                )
            absolute_asmdef_path: str = os.path.join(_REPOSITORY_ROOT, assembly_target.asmdef_path)
            self.assertTrue(
                os.path.isfile(absolute_asmdef_path),
                f"{assembly_target.assembly_name}: asmdef {assembly_target.asmdef_path!r} does not exist",
            )

    def test_unknown_package_or_assembly_name_raises_key_error(self) -> None:
        with self.assertRaises(KeyError):
            packages.targets_for("com.this-package-does-not-exist")


class HarvestTests(unittest.TestCase):
    def test_harvest_editor_assembly_strips_library_bee_output_paths_but_keeps_dll_filename(self) -> None:
        editor_target: packages.AssemblyTarget = packages.targets_for("DotsAnimationToolkit.Editor")[0]

        with tempfile.TemporaryDirectory(prefix="devloop_harvest_test_") as output_directory:
            try:
                harvested: rsp_harvest.HarvestedCompile = rsp_harvest.harvest(
                    _REPOSITORY_ROOT, editor_target.assembly_name, editor_target.source_roots, output_directory
                )
            except FileNotFoundError as missing_dag_error:
                raise unittest.SkipTest(
                    f"no harvested Unity compile graph available: {missing_dag_error}"
                )

            self.assertEqual(269, len(harvested.source_files))

            output_flag_lines: list[str] = [
                flag_line
                for flag_line in harvested.flag_lines
                if flag_line.startswith("-out:") or flag_line.startswith("-refout:")
            ]
            self.assertTrue(output_flag_lines, "expected at least one -out:/-refout: flag line")
            for flag_line in output_flag_lines:
                self.assertNotIn("Library/Bee", flag_line)

            out_flag_line: str = next(line for line in output_flag_lines if line.startswith("-out:"))
            quoted_path_match: re.Match[str] | None = re.search(r'-out:"([^"]+)"', out_flag_line)
            self.assertIsNotNone(quoted_path_match)
            self.assertEqual("DotsAnimationToolkit.Editor.dll", os.path.basename(quoted_path_match.group(1)))


class IsStaleTests(unittest.TestCase):
    def setUp(self) -> None:
        self.fake_project_root: str = tempfile.mkdtemp(prefix="devloop_stale_test_")

    def tearDown(self) -> None:
        for root_directory, _subdirectory_names, file_names in os.walk(self.fake_project_root, topdown=False):
            for file_name in file_names:
                os.remove(os.path.join(root_directory, file_name))
            os.rmdir(root_directory)

    def test_is_stale_true_when_asmdef_mtime_is_newer_than_the_harvested_response_file(self) -> None:
        dag_directory: str = os.path.join(
            self.fake_project_root, "Library", "Bee", "artifacts", "fake.dag"
        )
        os.makedirs(dag_directory)
        response_file_path: str = os.path.join(dag_directory, "FakeAssembly.rsp")
        with open(response_file_path, "w", encoding="utf-8") as response_file:
            response_file.write("-nologo\n")

        asmdef_directory: str = os.path.join(self.fake_project_root, "Packages", "FakePackage")
        os.makedirs(asmdef_directory)
        asmdef_path: str = os.path.join(asmdef_directory, "FakePackage.asmdef")
        with open(asmdef_path, "w", encoding="utf-8") as asmdef_file:
            asmdef_file.write("{}")

        earlier_time: float = 1_700_000_000.0
        later_time: float = earlier_time + 10.0
        os.utime(response_file_path, (earlier_time, earlier_time))
        os.utime(asmdef_path, (later_time, later_time))

        verdict: rsp_harvest.StalenessVerdict = rsp_harvest.is_stale(
            self.fake_project_root, "FakeAssembly", ["Packages/FakePackage/FakePackage.asmdef"]
        )

        self.assertTrue(verdict.is_stale)
        self.assertIn("FakePackage.asmdef", verdict.reason)


if __name__ == "__main__":
    unittest.main()
