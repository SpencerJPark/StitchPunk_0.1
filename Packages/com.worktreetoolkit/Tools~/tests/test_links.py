from __future__ import annotations

import os
import tempfile
import unittest

from tests.repo_fixture import TemporaryRepository
from worktree_toolkit import links
from worktree_toolkit.errors import RefusedError


class LinksTests(unittest.TestCase):
    def setUp(self):
        self.repository = TemporaryRepository()
        self.target_path = os.path.join(self.repository.root_directory, "target")
        os.makedirs(self.target_path)
        with open(os.path.join(self.target_path, "precious.txt"), "w", encoding="utf-8") as precious_file:
            precious_file.write("keep")

    def tearDown(self):
        self.repository.cleanup()

    def test_remove_directory_link_refuses_a_real_directory(self):
        with self.assertRaises(RefusedError):
            links.remove_directory_link(self.target_path)
        self.assertTrue(os.path.exists(os.path.join(self.target_path, "precious.txt")))

    def test_find_skips_links_inside_link_targets_and_removal_keeps_target(self):
        elsewhere_path = os.path.join(self.repository.root_directory, "elsewhere")
        os.makedirs(elsewhere_path)
        links.create_directory_link(os.path.join(self.target_path, "InnerLink"), elsewhere_path)
        worktree_path = self.repository.add_raw_worktree("alpha")
        outer_link_path = os.path.join(worktree_path, "Nested", "Deep")
        links.create_directory_link(outer_link_path, self.target_path)

        found_links = links.find_directory_links(worktree_path)

        self.assertEqual([os.path.normcase(os.path.abspath(outer_link_path))], [os.path.normcase(path) for path in found_links])
        links.remove_directory_link(outer_link_path)
        self.assertFalse(os.path.lexists(outer_link_path))
        self.assertTrue(os.path.exists(os.path.join(self.target_path, "precious.txt")))


if __name__ == "__main__":
    unittest.main()
