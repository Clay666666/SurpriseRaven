"""Run public legacy entrypoints in empty fixtures to enforce fail-before-start."""

from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class NoGuiPolicyTests(unittest.TestCase):
    def test_python_legacy_flags_fail_before_configuration_or_output_creation(self):
        for flag in ("--game", "--no-menu-round-trip"):
            with self.subTest(flag=flag), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                output = root / "must-not-be-created"
                result = subprocess.run(
                    [sys.executable, str(ROOT / "tools/mcp_smoke.py"), "configure",
                     "--root", str(root), "--output-dir", str(output), flag],
                    capture_output=True, text=True, timeout=20)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn("prohibited", result.stderr)
                self.assertFalse(output.exists())
                self.assertFalse((root / ".codex").exists())

    @unittest.skipUnless(shutil.which("powershell.exe"), "Windows PowerShell entrypoint")
    def test_powershell_legacy_switches_fail_before_tool_or_game_access(self):
        for flag in ("-Game", "-NoMenuRoundTrip"):
            with self.subTest(flag=flag), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                tools = root / "tools"
                tools.mkdir()
                script = tools / "verify-environment.ps1"
                shutil.copyfile(ROOT / "tools/verify-environment.ps1", script)
                result = subprocess.run(
                    ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(script), flag],
                    capture_output=True, text=True, timeout=20)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn("GUI game testing is prohibited", result.stderr)
                self.assertFalse((root / "artifacts").exists())


if __name__ == "__main__":
    unittest.main()
