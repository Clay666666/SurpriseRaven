"""Offline validation regression tests; no game process or installed files touched."""
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile

MODULE_PATH = Path(__file__).resolve().parents[1] / "tools/ritsulib_check.py"
spec = importlib.util.spec_from_file_location("ritsulib_check", MODULE_PATH)
checker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value), encoding="utf-8")


class RuntimeValidationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.game = self.root / "game"
        self.runtime = self.root / "runtime"
        self.payload = self.root / "payload"
        write_json(self.game / "release_info.json", {"version": "v0.111.0"})
        write_json(self.game / "data_sts2_windows_x86_64/sts2.runtimeconfig.json",
                   {"runtimeOptions": {"tfm": "net9.0"}})
        self.game_dll = self.game / "data_sts2_windows_x86_64/sts2.dll"
        self.game_dll.write_bytes(b"MZgame111")
        write_json(self.runtime / "mod_manifest.json",
                   {"id": "STS2-RitsuLib", "version": "0.6.14", "has_dll": True})
        (self.runtime / "STS2-RitsuLib.dll").write_bytes(b"MZloader")
        self.variant_manifest = {"schema": 2, "shared": [], "variants": []}
        for name in sorted(checker.SHARED):
            self.variant_manifest["shared"].append(self.assembly("shared", name))
        for target in sorted(checker.VARIANTS):
            files = [self.assembly(f"compat/{target}", name)
                     for name in sorted(checker.VARIANT_MODULES)]
            self.variant_manifest["variants"].append({"compatTarget": target, "files": files})
            (self.runtime / f"compat/{target}/compat-target.txt").write_text(target)
        self.save_variants()
        with zipfile.ZipFile(self.runtime / "assets.zip", "w", compression=zipfile.ZIP_STORED) as archive:
            archive.writestr("localization/en/text.json", b"knownassetcontents")
        self.payload.mkdir()
        self.manifest = {"id": checker.MOD_ID, "pck_name": checker.MOD_ID,
                         "has_dll": True, "has_pck": True,
                         "dependencies": [{"id": "STS2-RitsuLib"}]}
        self.save_payload()
        (self.payload / f"{checker.MOD_ID}.dll").write_bytes(b"MZmod")
        (self.payload / f"{checker.MOD_ID}.pck").write_bytes(b"GDPCpayload")

    def assembly(self, directory, name):
        path = self.runtime / directory / f"{name}.dll"
        path.parent.mkdir(parents=True, exist_ok=True)
        content = b"MZ" + f"{directory}/{name}".encode()
        path.write_bytes(content)
        return {"assembly": name, "sha256": hashlib.sha256(content).hexdigest()}

    def save_variants(self):
        write_json(self.runtime / "ritsulib-variants.manifest", self.variant_manifest)

    def save_payload(self):
        write_json(self.payload / f"{checker.MOD_ID}.json", self.manifest)

    def test_complete_runtime_and_payload(self):
        result = checker.validate(self.runtime, self.game, self.payload)
        self.assertEqual(result["status"], "passed")
        self.assertEqual(result["runtime_directory"], str(self.runtime))
        self.assertEqual(len(result["checked_assemblies"]), 9)
        self.assertEqual(result["assets_entries"], 1)
        self.assertEqual(result["mod_id"], checker.MOD_ID)
        self.assertEqual(result["main_manifest_sha256"],
                         hashlib.sha256((self.payload / f"{checker.MOD_ID}.json").read_bytes()).hexdigest())
        self.assertEqual(result["game_version"], "0.111.0")
        self.assertEqual(result["compat_target"], "0.111.0")
        self.assertEqual(result["version"], "0.6.14")
        self.assertEqual(result["game_dll_sha256"], hashlib.sha256(b"MZgame111").hexdigest())

    def test_113_patch_version_uses_113_api_with_same_payload(self):
        baseline = checker.validate(self.runtime, self.game, self.payload)
        write_json(self.game / "release_info.json", {"version": "v0.113.1", "commit": "new-game"})
        self.game_dll.write_bytes(b"MZgame113")
        result = checker.validate(self.runtime, self.game, self.payload)
        self.assertEqual(result["game_version"], "0.113.1")
        self.assertEqual(result["game_api"], "0.113.1")
        self.assertEqual(result["compat_target"], "0.113.0")
        self.assertEqual(result["game_release"]["commit"], "new-game")
        self.assertNotEqual(result["game_dll_sha256"], baseline["game_dll_sha256"])
        self.assertEqual(result["main_dll_sha256"], baseline["main_dll_sha256"])
        self.assertEqual(result["main_pck_sha256"], baseline["main_pck_sha256"])

    def test_missing_module_rejected(self):
        (self.runtime / "compat/0.111.0/STS2-RitsuLib.Runtime.dll").unlink()
        with self.assertRaisesRegex(checker.ValidationError, "missing or empty"):
            checker.validate(self.runtime, self.game)

    def test_runtime_version_mismatch(self):
        write_json(self.runtime / "mod_manifest.json", {"id": "STS2-RitsuLib", "version": "0.6.13"})
        with self.assertRaisesRegex(checker.ValidationError, "Expected STS2-RitsuLib"):
            checker.validate(self.runtime, self.game)

    def test_game_version_mismatch(self):
        for unsupported in ("v0.112.0", "v0.113.0", "v0.113.2", "v0.114.0", ""):
            with self.subTest(version=unsupported):
                write_json(self.game / "release_info.json", {"version": unsupported})
                with self.assertRaisesRegex(checker.ValidationError, "targets games"):
                    checker.validate(self.runtime, self.game)

    def test_tampered_module_rejected(self):
        (self.runtime / "shared/STS2-RitsuLib.Shared.dll").write_bytes(b"MZtampered")
        with self.assertRaisesRegex(checker.ValidationError, "SHA256 mismatch"):
            checker.validate(self.runtime, self.game)

    def test_missing_or_duplicate_variant_rejected(self):
        self.variant_manifest["variants"][-1] = self.variant_manifest["variants"][0]
        self.save_variants()
        with self.assertRaisesRegex(checker.ValidationError, "three compatibility variants"):
            checker.validate(self.runtime, self.game)

    def test_113_missing_variant_never_falls_back_to_111(self):
        write_json(self.game / "release_info.json", {"version": "v0.113.1"})
        self.variant_manifest["variants"] = [branch for branch in self.variant_manifest["variants"]
                                             if branch["compatTarget"] != "0.113.0"]
        self.save_variants()
        with self.assertRaisesRegex(checker.ValidationError, "three compatibility variants"):
            checker.validate(self.runtime, self.game)

    def test_113_missing_module_rejected(self):
        write_json(self.game / "release_info.json", {"version": "v0.113.1"})
        (self.runtime / "compat/0.113.0/STS2-RitsuLib.Runtime.dll").unlink()
        with self.assertRaisesRegex(checker.ValidationError, "missing or empty"):
            checker.validate(self.runtime, self.game)

    def test_wrong_compat_marker(self):
        (self.runtime / "compat/0.111.0/compat-target.txt").write_text("0.110.0")
        with self.assertRaisesRegex(checker.ValidationError, "target does not match"):
            checker.validate(self.runtime, self.game)

    def test_corrupt_zip_crc_rejected(self):
        assets = self.runtime / "assets.zip"
        assets.write_bytes(assets.read_bytes().replace(b"knownassetcontents", b"badlyassetcontents"))
        with self.assertRaisesRegex(checker.ValidationError, "CRC mismatch"):
            checker.validate(self.runtime, self.game)

    def test_runtime_link_rejected_before_copy(self):
        link = self.runtime / "viewer"
        outside = self.root / "outside"
        outside.mkdir()
        try:
            link.symlink_to(outside, target_is_directory=True)
        except OSError:
            self.skipTest("Creating symlinks requires Windows developer mode or permission")
        with self.assertRaisesRegex(checker.ValidationError, "Reparse points"):
            checker.validate(self.runtime, self.game)

    def test_zip_path_traversal_rejected(self):
        with zipfile.ZipFile(self.runtime / "assets.zip", "w") as archive:
            archive.writestr("../outside.txt", "bad")
        with self.assertRaisesRegex(checker.ValidationError, "Unsafe assets.zip"):
            checker.validate(self.runtime, self.game)
        self.assertFalse((self.root / "outside.txt").exists())

    def test_missing_payload_dependency_rejected(self):
        self.manifest["dependencies"] = []
        self.save_payload()
        with self.assertRaisesRegex(checker.ValidationError, "missing its STS2-RitsuLib"):
            checker.validate(self.runtime, self.game, self.payload)

    def test_old_string_dependency_format_rejected(self):
        self.manifest["dependencies"] = ["STS2-RitsuLib"]
        self.save_payload()
        with self.assertRaisesRegex(checker.ValidationError, "object format"):
            checker.validate(self.runtime, self.game, self.payload)

    def test_extra_player_dependency_rejected(self):
        self.manifest["dependencies"].append({"id": "STS2_MCP"})
        self.save_payload()
        with self.assertRaisesRegex(checker.ValidationError, "only dependency"):
            checker.validate(self.runtime, self.game, self.payload)

    def test_bundled_dependency_dll_rejected(self):
        for name in ["GodotSharp.dll", "0Harmony.dll", "sts2.dll", "STS2-RitsuLib.Shared.dll",
                     "STS2_MCP.dll", "Sts2ModScaffold.dll", "mod_manifest.json"]:
            with self.subTest(name=name):
                copy = self.payload / name
                copy.write_bytes(b"MZcopy")
                try:
                    with self.assertRaisesRegex(checker.ValidationError, "must not be bundled"):
                        checker.validate(self.runtime, self.game, self.payload)
                finally:
                    copy.unlink()

    def test_cli_failure_writes_failure_report(self):
        (self.runtime / "STS2-RitsuLib.dll").unlink()
        completed, report = self.run_cli()
        self.assertEqual(completed.returncode, 1)
        self.assertEqual(report["status"], "failed")
        self.assertEqual(report["game_version"], "0.111.0")
        self.assertEqual(report["version"], "0.6.14")
        self.assertIn("error", report)

    def test_cli_unknown_game_retains_observed_versions_and_hash(self):
        write_json(self.game / "release_info.json", {"version": "v0.114.0", "commit": "future-game"})
        completed, report = self.run_cli()
        self.assertEqual(completed.returncode, 1)
        self.assertEqual(report["game_version"], "0.114.0")
        self.assertEqual(report["game_api"], "0.114.0")
        self.assertEqual(report["game_release"]["commit"], "future-game")
        self.assertIsNone(report["compat_target"])
        self.assertEqual(report["version"], "0.6.14")
        self.assertEqual(report["game_dll_sha256"], hashlib.sha256(b"MZgame111").hexdigest())

    def test_cli_old_ritsulib_retains_actual_not_expected_version(self):
        write_json(self.game / "release_info.json", {"version": "v0.113.1"})
        write_json(self.runtime / "mod_manifest.json",
                   {"id": "STS2-RitsuLib", "version": "0.6.13", "has_dll": True})
        completed, report = self.run_cli()
        self.assertEqual(completed.returncode, 1)
        self.assertEqual(report["version"], "0.6.13")
        self.assertEqual(report["expected_version"], "0.6.14")
        self.assertEqual(report["game_version"], "0.113.1")
        self.assertEqual(report["compat_target"], "0.113.0")

    def run_cli(self):
        report = self.root / "output/report.json"
        completed = subprocess.run([sys.executable, str(MODULE_PATH), "--runtime", str(self.runtime),
                                    "--game", str(self.game), "--output", str(report)],
                                   capture_output=True, text=True, timeout=20)
        result = json.loads(report.read_text(encoding="utf-8"))
        self.assertEqual(result, json.loads(completed.stdout))
        return completed, result


if __name__ == "__main__":
    unittest.main()
