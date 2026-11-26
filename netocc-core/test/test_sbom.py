# SPDX-FileCopyrightText: 2026 Paul Büchner
# SPDX-License-Identifier: MIT

"""Tests of sbom.py, the runtime packages' SBOM: python test/test_sbom.py (stdlib unittest)."""
from __future__ import annotations

import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import sbom  # noqa: E402


class LibraryTests(unittest.TestCase):
    def test_library_stem_matches_a_staged_copy_with_its_installed_file(self):
        # Act
        stems = [sbom.library_stem(n) for n in ("libz.so.1", "libz.so.1.3.2", "libz.1.dylib", "z.dll", "z.lib", "gperf")]

        # Assert
        self.assertEqual(stems, ["libz", "libz", "libz", "z", None, None])


PORT_SPDX = {
    "packages": [
        {"SPDXID": "SPDXRef-port", "name": "zlib", "versionInfo": "1.3.2#2", "licenseConcluded": "Zlib",
         "homepage": "https://www.zlib.net/", "downloadLocation": "git+https://github.com/microsoft/vcpkg@f4f43a5a"},
        {"SPDXID": "SPDXRef-resource-0", "name": "madler/zlib", "downloadLocation": "git+https://github.com/madler/zlib@v1.3.2"},
        {"SPDXID": "SPDXRef-binary", "name": "zlib:x64-windows"}],
    "files": [{"fileName": "./0001-Prevent-invalid-inclusions.patch"}, {"fileName": "./portfile.cmake"},
              {"fileName": "./bin/z.dll"}]}


class SbomTests(unittest.TestCase):
    def test_port_spdx_reads_vcpkgs_file_of_a_port(self):
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            share = Path(directory, "share", "zlib")
            share.mkdir(parents=True)
            (share / "vcpkg.spdx.json").write_text(json.dumps(PORT_SPDX), encoding="utf-8")

            # Act
            spdx = sbom.port_spdx(Path(directory), "zlib")

        # Assert (the version without the port-version; the patches of the port, not its installed files)
        self.assertEqual(spdx, {"version": "1.3.2", "license": "Zlib", "homepage": "https://www.zlib.net/",
                                "recipe": "git+https://github.com/microsoft/vcpkg@f4f43a5a",
                                "source": "git+https://github.com/madler/zlib@v1.3.2",
                                "patches": ["0001-Prevent-invalid-inclusions.patch"]})

    def test_port_patches_point_at_vcpkgs_recipe_commit(self):
        # Arrange
        spdx = {"recipe": "git+https://github.com/microsoft/vcpkg@f4f43a5a", "patches": ["fix.patch"]}

        # Act
        patches = sbom.port_patches("zlib", spdx, "https://github.com/paulbuechner/netocc")

        # Assert
        self.assertEqual(patches, [{"type": "unofficial",
                                    "diff": {"url": "https://github.com/microsoft/vcpkg/blob/f4f43a5a/ports/zlib/fix.patch"}}])

    def test_license_terms_take_the_decision(self):
        # Act
        terms = {port: sbom.license_terms(expression, port)
                 for port, expression in (("freetype", "(FTL OR GPL-2.0-or-later)"), ("opencascade", "LGPL-2.1-only"),
                                          ("libjpeg", "IJG AND BSD-3-Clause"))}

        # Assert
        self.assertEqual(terms, {"freetype": ["FTL"], "opencascade": ["LGPL-2.1-only WITH OCCT-exception-1.0"],
                                 "libjpeg": ["IJG", "BSD-3-Clause"]})

    def test_license_terms_refuse_a_choice_nobody_made_and_no_license(self):
        # Act
        refusals = [lambda: sbom.license_terms("MIT OR Apache-2.0", "someport"),
                    lambda: sbom.license_terms("NOASSERTION", "someport")]

        # Assert
        for refuse in refusals:
            self.assertRaises(SystemExit, refuse)

    def test_licenses_are_one_concluded_expression_their_texts_evidence(self):
        # Act
        licenses = sbom.licenses(["LGPL-2.1-only WITH OCCT-exception-1.0"])
        evidence = sbom.evidence([("LGPL-2.1-only WITH OCCT-exception-1.0", "text")])

        # Assert (an exception's term is no SPDX id: CycloneDX takes it as a name)
        self.assertEqual(licenses, [{"expression": "LGPL-2.1-only WITH OCCT-exception-1.0", "acknowledgement": "concluded"}])
        self.assertEqual(evidence, {"licenses": [{"license": {"name": "LGPL-2.1-only WITH OCCT-exception-1.0",
                                                              "text": {"contentType": "text/plain", "content": "text"}}}]})

    def test_source_references_tell_a_repository_from_an_archive(self):
        # Act
        references = [sbom.source_references(location) for location in
                      ("git+https://github.com/madler/zlib@v1.3.2", "https://sourceware.org/pub/bzip2/bzip2-1.0.8.tar.gz")]

        # Assert (the location itself is the exact source either way)
        self.assertEqual(references, [
            [{"type": "vcs", "url": "https://github.com/madler/zlib", "comment": "v1.3.2"},
             {"type": "source-distribution", "url": "git+https://github.com/madler/zlib@v1.3.2"}],
            [{"type": "source-distribution", "url": "https://sourceware.org/pub/bzip2/bzip2-1.0.8.tar.gz"}]])

    def test_port_dependencies_read_vcpkgs_status_with_the_features(self):
        # Arrange (a feature's needs in a paragraph of its own; a removed port)
        with tempfile.TemporaryDirectory() as directory:
            status = Path(directory, "vcpkg", "status")
            status.parent.mkdir(parents=True)
            status.write_text(VCPKG_STATUS, encoding="utf-8")

            # Act
            needs = sbom.port_dependencies(Path(directory))

        # Assert
        self.assertEqual(needs, {"freetype": {"vcpkg-cmake", "zlib"}, "zlib": set()})

    def test_write_sbom_puts_each_file_under_its_port(self):
        # Arrange (OCCT and zlib as vcpkg installed them, NetOcc's own library beside theirs)
        with tempfile.TemporaryDirectory() as directory:
            installed, stage = Path(directory, "installed"), Path(directory, "win-x64", "native")
            prefix, info = installed / "x64-windows", installed / "vcpkg" / "info"
            info.mkdir(parents=True)
            for port, version, library in (("opencascade", "8.0.1", "TKernel.dll"), ("zlib", "1.3.2", "z.dll")):
                (info / f"{port}_{version}_x64-windows.list").write_text(f"x64-windows/bin/{library}\n", encoding="utf-8")
                share = prefix / "share" / port
                share.mkdir(parents=True)
                spdx = json.loads(json.dumps(PORT_SPDX))
                spdx["packages"][0].update({"name": port, "versionInfo": version})
                (share / "vcpkg.spdx.json").write_text(json.dumps(spdx), encoding="utf-8")
                (share / "copyright").write_text(f"Copyright (c) {port}", encoding="utf-8")
            (installed / "vcpkg" / "status").write_text(
                "Package: opencascade\nVersion: 8.0.1\nDepends: zlib\nStatus: install ok installed\n\n"
                "Package: zlib\nVersion: 1.3.2\nStatus: install ok installed\n", encoding="utf-8")
            stage.mkdir(parents=True)
            for name in ("NetOccRuntime.dll", "TKernel.dll", "z.dll"):
                (stage / name).write_bytes(b"")
            target = stage.parent / sbom.FILE

            # Act
            sbom.write(stage, target, "NetOcc.runtime.win-x64", installed=installed, triplet="x64-windows",
                       repository="https://github.com/paulbuechner/netocc")
            written = json.loads(target.read_text(encoding="utf-8"))

        # Assert (every reference names the package first; what OCCT needs is vcpkg's)
        files = {c["name"]: [f["name"] for f in c.get("components", [])] for c in written["components"]}
        self.assertEqual(files, {"NetOcc": ["NetOccRuntime.dll"], "opencascade": ["TKernel.dll"], "zlib": ["z.dll"]})
        needs = {d["ref"]: d["dependsOn"] for d in written["dependencies"]}
        self.assertEqual(needs["NetOcc.runtime.win-x64/pkg:generic/opencascade@8.0.1"],
                         ["NetOcc.runtime.win-x64/pkg:generic/zlib@1.3.2"])
        self.assertEqual(needs["NetOcc.runtime.win-x64/netocc"], ["NetOcc.runtime.win-x64/pkg:generic/opencascade@8.0.1"])
        self.assertEqual(written["metadata"]["component"]["licenses"][0]["expression"],
                         "MIT AND LGPL-2.1-only WITH OCCT-exception-1.0 AND Zlib")

    def test_stamp_sbom_gives_the_package_its_version_and_each_file_its_hash(self):
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            stage = Path(directory, "native")
            stage.mkdir()
            (stage / "NetOccRuntime.dll").write_bytes(b"code")
            stamp_file = Path(directory, sbom.FILE)
            package, own = "pkg:nuget/NetOcc.runtime.win-x64", "NetOcc.runtime.win-x64/netocc"
            stamp_file.write_text(json.dumps({
                "metadata": {"component": {"bom-ref": package, "name": "NetOcc.runtime.win-x64", "purl": package,
                                           "properties": sbom.bsi_properties(False, True, True)}},
                "components": [{"type": "library", "bom-ref": own, "name": "NetOcc", "version": "", "components": [
                    sbom.file_component("NetOcc.runtime.win-x64", "NetOccRuntime.dll", {
                        "manufacturer": {}, "version": "", "licenses": []})]}],
                "dependencies": [{"ref": package, "dependsOn": [own]}],
                "compositions": [{"aggregate": "complete", "assemblies": [package, own]}]}), encoding="utf-8")

            # Act (twice: pack stamps a stage again)
            sbom.stamp(stamp_file, "8.0.1.1")
            sbom.stamp(stamp_file, "8.0.1.2")
            stamped = json.loads(stamp_file.read_text(encoding="utf-8"))

        # Assert (the reference that names the package follows it; the package's own libraries take its version)
        expected = "pkg:nuget/NetOcc.runtime.win-x64@8.0.1.2"
        root, own_part = stamped["metadata"]["component"], stamped["components"][0]
        file = own_part["components"][0]
        self.assertEqual((root["version"], root["purl"], stamped["dependencies"][0]["ref"],
                          stamped["compositions"][0]["assemblies"][0]), ("8.0.1.2", expected, expected, expected))
        self.assertEqual(root["properties"][0], {"name": "bsi:component:filename",
                                                 "value": "NetOcc.runtime.win-x64.8.0.1.2.nupkg"})
        self.assertEqual((own_part["version"], file["version"]), ("8.0.1.2", "8.0.1.2"))
        self.assertEqual(file["hashes"], [{"alg": "SHA-512", "content": hashlib.sha512(b"code").hexdigest()}])
        self.assertTrue(stamped["serialNumber"].startswith("urn:uuid:"))


VCPKG_STATUS = """Package: zlib
Version: 1.3.2
Architecture: x64-windows
Status: install ok installed

Package: freetype
Version: 2.14.3
Depends: vcpkg-cmake
Description: a font engine
    over two lines
Status: install ok installed

Package: freetype
Feature: zlib
Depends: zlib:x64-windows
Status: install ok installed

Package: brotli
Version: 1.2.0
Status: purge ok not-installed
"""


if __name__ == "__main__":
    unittest.main()
