"""Pins the dev-loop lint's four mechanical rules and the false-positive guards for real past regressions.

Every case writes its own throwaway `.cs` file under a tempdir so no test touches the real repo tree.
"""
from __future__ import annotations

import os
import sys
import tempfile
import unittest

_TESTS_DIRECTORY: str = os.path.dirname(os.path.abspath(__file__))
_DEVLOOP_TOP_LEVEL_DIRECTORY: str = os.path.dirname(_TESTS_DIRECTORY)
if _DEVLOOP_TOP_LEVEL_DIRECTORY not in sys.path:
    sys.path.insert(0, _DEVLOOP_TOP_LEVEL_DIRECTORY)

from devloop_tools import lint


def _write_temporary_csharp_file(directory_path: str, relative_path: str, source_text: str) -> str:
    absolute_file_path: str = os.path.join(directory_path, relative_path)
    os.makedirs(os.path.dirname(absolute_file_path), exist_ok=True)
    with open(absolute_file_path, "w", encoding="utf-8") as csharp_file:
        csharp_file.write(source_text)
    return absolute_file_path


class LintRulesTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory: str = tempfile.mkdtemp(prefix="devloop_lint_test_")

    def tearDown(self) -> None:
        for root_directory, _subdirectory_names, file_names in os.walk(self.temporary_directory, topdown=False):
            for file_name in file_names:
                os.remove(os.path.join(root_directory, file_name))
            os.rmdir(root_directory)

    def test_no_var_rule_flags_keyword_but_not_identifier_or_string_literal(self) -> None:
        var_keyword_path: str = _write_temporary_csharp_file(
            self.temporary_directory,
            "VarKeyword.cs",
            "public class VarKeyword\n{\n    void Method()\n    {\n        var someValue = 1;\n    }\n}\n",
        )
        variable_identifier_path: str = _write_temporary_csharp_file(
            self.temporary_directory,
            "VariableIdentifier.cs",
            "public class VariableIdentifier\n{\n    void Method()\n    {\n        int variable = 1;\n    }\n}\n",
        )
        var_inside_string_literal_path: str = _write_temporary_csharp_file(
            self.temporary_directory,
            "VarInsideString.cs",
            "public class VarInsideString\n{\n    void Method()\n    {\n        string message = \"contains a var keyword\";\n    }\n}\n",
        )

        report: lint.LintReport = lint.lint_paths(
            self.temporary_directory,
            [var_keyword_path, variable_identifier_path, var_inside_string_literal_path],
        )

        self.assertEqual(1, len(report.findings))
        self.assertEqual("no-var", report.findings[0].rule_id)
        self.assertEqual("VarKeyword.cs", report.findings[0].file_path)

    def test_job_run_call_is_flagged_once(self) -> None:
        job_run_call_path: str = _write_temporary_csharp_file(
            self.temporary_directory,
            "JobRunCall.cs",
            "public class JobRunCall\n{\n    void Method(SomeJob job)\n    {\n        job.Run();\n    }\n}\n",
        )

        report: lint.LintReport = lint.lint_paths(self.temporary_directory, [job_run_call_path])

        self.assertEqual(1, len(report.findings))
        self.assertEqual("no-run-on-jobs", report.findings[0].rule_id)

    def test_enabled_ref_naming_rule_flags_mismatch_but_accepts_correct_name(self) -> None:
        mismatched_name_path: str = _write_temporary_csharp_file(
            self.temporary_directory,
            "MismatchedEnabledRefName.cs",
            "public class MismatchedEnabledRefName\n{\n"
            "    void Execute(EnabledRefRW<Health> healthFlag)\n    {\n    }\n}\n",
        )
        correct_name_path: str = _write_temporary_csharp_file(
            self.temporary_directory,
            "CorrectEnabledRefName.cs",
            "public class CorrectEnabledRefName\n{\n"
            "    void Execute(EnabledRefRW<Health> healthEnabled)\n    {\n    }\n}\n",
        )

        report: lint.LintReport = lint.lint_paths(
            self.temporary_directory, [mismatched_name_path, correct_name_path]
        )

        self.assertEqual(1, len(report.findings))
        self.assertEqual("enabled-ref-naming", report.findings[0].rule_id)
        self.assertEqual("MismatchedEnabledRefName.cs", report.findings[0].file_path)

    def test_handles_banned_only_under_package_editor_folder(self) -> None:
        package_editor_path: str = _write_temporary_csharp_file(
            self.temporary_directory,
            os.path.join("Packages", "FakePackage", "Editor", "SceneGuiDrawer.cs"),
            "public class SceneGuiDrawer\n{\n"
            "    void OnSceneGUI()\n    {\n        Handles.DrawLine(default, default);\n    }\n}\n",
        )
        assets_path: str = _write_temporary_csharp_file(
            self.temporary_directory,
            os.path.join("Assets", "SceneGuiDrawer.cs"),
            "public class SceneGuiDrawer\n{\n"
            "    void OnSceneGUI()\n    {\n        Handles.DrawLine(default, default);\n    }\n}\n",
        )

        report: lint.LintReport = lint.lint_paths(self.temporary_directory, [package_editor_path, assets_path])

        self.assertEqual(1, len(report.findings))
        self.assertEqual("no-handles-in-package-editor", report.findings[0].rule_id)
        self.assertTrue(report.findings[0].file_path.startswith("Packages/"))

    def test_single_letter_declaration_rule_ignores_for_loop_counter_and_member_access(self) -> None:
        """Regression guard: the broad rule once produced 141 false positives on conforming code."""
        conforming_path: str = _write_temporary_csharp_file(
            self.temporary_directory,
            "ConformingLoopAndMemberAccess.cs",
            "public class ConformingLoopAndMemberAccess\n{\n"
            "    void Method(int count, Quaternion rotation)\n    {\n"
            "        for (int i = 0; i < count; i++)\n        {\n        }\n"
            "        float x = rotation.x;\n    }\n}\n",
        )

        report: lint.LintReport = lint.lint_paths(self.temporary_directory, [conforming_path])

        single_letter_findings: list[lint.LintFinding] = [
            finding for finding in report.findings if finding.rule_id == "no-single-letter-names"
        ]
        self.assertEqual([], single_letter_findings)


if __name__ == "__main__":
    unittest.main()
