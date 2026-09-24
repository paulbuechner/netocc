# SPDX-FileCopyrightText: 2026 Paul Büchner
# SPDX-License-Identifier: MIT

"""Tests of build.py's versions, changelog and SWIG message rules: python test/test_build.py (stdlib unittest)."""
from __future__ import annotations

import importlib.util
import tempfile
import unittest
from pathlib import Path
from unittest import mock

CORE = Path(__file__).resolve().parents[1]
_spec = importlib.util.spec_from_file_location("build", CORE / "build.py")
build = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(build)

CHANGELOG = """# Changelog

## [Unreleased]

### Fixed

- A fix nobody released yet.

## [8.0.1.2-preview.1] - 2026-09-20

### Added

- Views keep their owner.

## [8.0.1.1] - 2026-09-01

### Added

- The first release.

[8.0.1.2-preview.1]: https://github.com/paulbuechner/netocc/releases/tag/v8.0.1.2-preview.1
[8.0.1.1]: https://github.com/paulbuechner/netocc/releases/tag/v8.0.1.1
[Unreleased]: https://github.com/paulbuechner/netocc/compare/v8.0.1.1...HEAD
"""


class VersionTests(unittest.TestCase):
    def test_released_versions_are_newest_first(self):
        # Act
        versions = build.released_versions(CHANGELOG)

        # Assert
        self.assertEqual(versions, ["8.0.1.2-preview.1", "8.0.1.1"])

    def test_next_revision_follows_the_last_release_not_a_prerelease(self):
        # Arrange
        released = build.released_versions(CHANGELOG)

        # Act
        revisions = (build.next_revision("8.0.1", released), build.next_revision("8.0.2", released))

        # Assert (a new OCCT version starts at 1)
        self.assertEqual(revisions, (2, 1))

    def test_prerelease_label_numbers_a_bare_label_after_its_revisions_last(self):
        # Arrange
        released = ["8.0.1.2-next.2", "8.0.1.2-next.1", "8.0.1.1-next.4", "8.0.1.1"]

        # Act
        labels = (build.prerelease_label("next", "8.0.1", 2, released), build.prerelease_label("next", "8.0.1", 3, released),
                  build.prerelease_label("preview.1", "8.0.1", 2, released))

        # Assert (a numbered label stays as given)
        self.assertEqual(labels, ("next.3", "next.1", "preview.1"))

    def test_version_problem_takes_occts_version_and_a_revision_from_1(self):
        # Act
        problems = {v: build.version_problem(v, "8.0.1") for v in ("8.0.1.1", "8.0.1.2-preview.1", "8.0.1.0", "1.2.3", "8.0.2.1")}

        # Assert
        self.assertEqual([v for v, problem in problems.items() if problem is None], ["8.0.1.1", "8.0.1.2-preview.1"])


class ChangelogTests(unittest.TestCase):
    def test_changelog_section_is_the_body_of_a_heading(self):
        # Act
        sections = (build.changelog_section(CHANGELOG, "8.0.1.1"), build.changelog_section(CHANGELOG, None),
                    build.changelog_section(CHANGELOG, "8.0.1.9"))

        # Assert
        self.assertEqual(sections, ("### Added\n\n- The first release.", "### Fixed\n\n- A fix nobody released yet.", None))

    def test_as_bullet_indents_the_lines_after_the_first(self):
        # Act
        bullet = build.as_bullet("Streams seek.\nIn both directions.\n")

        # Assert
        self.assertEqual(bullet, ["- Streams seek.", "  In both directions."])

    def test_pending_notes_add_the_entries_to_unreleased_in_keep_a_changelogs_order(self):
        # Arrange
        entries = [("fixed", "Another fix."), ("added", "- A feature.")]

        # Act
        notes = build.pending_notes(CHANGELOG, entries)

        # Assert
        self.assertEqual(notes, "### Added\n\n- A feature.\n\n### Fixed\n\n- A fix nobody released yet.\n- Another fix.")

    def test_pending_notes_are_none_without_entries_or_unreleased(self):
        # Arrange
        changelog = CHANGELOG.replace("## [Unreleased]\n\n### Fixed\n\n- A fix nobody released yet.\n\n", "")

        # Act
        notes = build.pending_notes(changelog, [])

        # Assert
        self.assertIsNone(notes)

    def test_release_notes_of_a_release_list_its_prereleases_changes_first(self):
        # Arrange (the prerelease deleted its entries when it was written)
        pending = "### Fixed\n\n- A late fix."

        # Act
        notes = build.release_notes(CHANGELOG, "8.0.1.2", pending)

        # Assert
        self.assertEqual(notes, "### Added\n\n- Views keep their owner.\n\n### Fixed\n\n- A late fix.")

    def test_release_notes_of_a_prerelease_are_whats_pending(self):
        # Act
        notes = build.release_notes(CHANGELOG, "8.0.1.2-preview.2", "### Fixed\n\n- A late fix.")

        # Assert
        self.assertEqual(notes, "### Fixed\n\n- A late fix.")

    def test_with_release_replaces_unreleased_and_links_the_tag_first(self):
        # Act
        text = build.with_release(CHANGELOG, "8.0.1.2", "### Fixed\n\n- A fix.", "2026-09-27")

        # Assert
        self.assertIn("# Changelog\n\n## [8.0.1.2] - 2026-09-27\n\n### Fixed\n\n- A fix.\n\n## [8.0.1.2-preview.1]", text)
        self.assertNotIn("[Unreleased]", text)
        self.assertIn("\n[8.0.1.2]: https://github.com/paulbuechner/netocc/releases/tag/v8.0.1.2\n[8.0.1.2-preview.1]: ", text)

    def test_fragments_refuse_an_empty_entry(self):
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, "streams-seek.fixed.md").write_text("\n", encoding="utf-8")

            # Act
            with mock.patch.object(build, "CHANGES", Path(directory)), self.assertRaises(SystemExit) as failure:
                build.fragments()

        # Assert
        self.assertEqual(str(failure.exception.code), "changes/streams-seek.fixed.md is empty")


class ReleasePullRequestTests(unittest.TestCase):
    def test_release_pr_body_on_next_is_changesets_text_with_its_pre_mode_note(self):
        # Act
        body = build.release_pr_body("8.0.1.2-next.1", "### Fixed\n\n- A fix.", "next")

        # Assert (changesets' header, its pre mode note between the warning signs, then the release)
        self.assertTrue(body.startswith("This PR was opened by the [NetOcc release]("))
        self.assertIn("the packages will be published to nuget.org automatically. If you're not ready to do a release yet, "
                      "that's fine, whenever you add more changelog entries to next, this PR will be updated.", body)
        self.assertIn(f"{build.PRE_MODE_SIGNS}\n\n`next` is currently in **pre mode** so this branch has prereleases rather "
                      "than normal releases. If you want to exit prereleases, merge `next` into `main` and run "
                      f"`gh workflow run release.yml` on `main`.\n\n{build.PRE_MODE_SIGNS}\n\n# Releases", body)
        self.assertTrue(body.endswith("# Releases\n\n## NetOcc@8.0.1.2-next.1\n\n### Fixed\n\n- A fix."))

    def test_release_pr_body_on_main_has_no_pre_mode_note(self):
        # Act
        body = build.release_pr_body("8.0.1.2", "### Fixed\n\n- A fix.", "main")

        # Assert
        self.assertIn("whenever you add more changelog entries to main, this PR will be updated.\n\n# Releases", body)
        self.assertNotIn("pre mode", body)


class SwigMessageTests(unittest.TestCase):
    def test_sift_swig_messages_leaves_out_the_harmless_warning_401_pairs(self):
        # Arrange (an imported class before its base in this module, then a class of this module without its base)
        messages = "\n".join([
            "Geom.i(10) : Warning 401: Base class 'Demo_Base' undefined.",
            "Demo.i(20) : Warning 401: 'Demo_Base' must be defined before it is used as a base class.",
            "Demo.i(30) : Warning 401: Base class 'Other_Base' undefined.",
            "Demo.i(40) : Warning 503: Can't wrap 'operator ()' unless renamed to a valid identifier.",
        ])

        # Act
        kept, harmless, lost = build.sift_swig_messages("Demo", messages)

        # Assert
        self.assertEqual((harmless, lost), (1, ["Demo.i(30) : Warning 401: Base class 'Other_Base' undefined."]))
        self.assertEqual(kept, messages.splitlines()[2:])


class LibraryTests(unittest.TestCase):
    def test_library_stem_matches_a_staged_copy_with_its_installed_file(self):
        # Act
        stems = [build.library_stem(n) for n in ("libz.so.1", "libz.so.1.3.2", "libz.1.dylib", "z.dll", "z.lib", "gperf")]

        # Assert
        self.assertEqual(stems, ["libz", "libz", "libz", "z", None, None])


if __name__ == "__main__":
    unittest.main()
