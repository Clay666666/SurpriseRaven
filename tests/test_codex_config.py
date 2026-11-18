"""Regression checks for preserving hand-edited Codex project settings."""

import importlib.util
from pathlib import Path
import tempfile
import tomllib
import unittest


SPEC = importlib.util.spec_from_file_location(
    "scaffold_mcp", Path(__file__).resolve().parents[1] / "tools/mcp_smoke.py"
)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class CodexConfigTests(unittest.TestCase):
    def make_root(self, directory):
        root = Path(directory)
        for item in (
            "tools/dotnet/dotnet.exe",
            "tools/ILSpy-Mcp/bin/Release/net8.0/ILSpy.Mcp.dll",
            "tools/STS2MCP/mcp/.venv/Scripts/python.exe",
            "tools/STS2MCP/mcp/server.py",
        ):
            target = root / item
            target.parent.mkdir(parents=True, exist_ok=True)
            target.touch()
        (root / ".codex").mkdir()
        return root

    def test_new_configuration_is_idempotent(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self.make_root(directory)
            MODULE.configure(root)
            path = root / ".codex/config.toml"
            first = path.read_bytes()
            MODULE.configure(root)
            self.assertEqual(first, path.read_bytes())
            config = tomllib.loads(first.decode())
            self.assertEqual(config["mcp_optional_startup_grace_ms"], 0)
            self.assertTrue(config["mcp_servers"]["sts2mcp"]["enabled"])

    def test_preserves_other_servers_comments_and_unknown_keys(self):
        original = '''# User comment must survive
model = "user-model"
mcp_optional_startup_grace_ms = 1000

[mcp_servers.personal]
command = "personal.exe"
args = [
  "one",
  "two"
]

[mcp_servers."ilspy"]
command = "old.exe"
args = [
  "old.dll",
]
enabled_tools = ["decompile_type"]

[mcp_servers.ilspy.env]
CUSTOM_VALUE = "preserve me"
DOTNET_ROOT = "old-root"
'''
        with tempfile.TemporaryDirectory() as directory:
            root = self.make_root(directory)
            path = root / ".codex/config.toml"
            path.write_text(original)
            MODULE.configure(root)
            text = path.read_text()
            config = tomllib.loads(text)
            self.assertIn("# User comment must survive", text)
            self.assertEqual(config["model"], "user-model")
            self.assertEqual(config["mcp_servers"]["personal"], tomllib.loads(original)["mcp_servers"]["personal"])
            self.assertEqual(config["mcp_servers"]["ilspy"]["enabled_tools"], ["decompile_type"])
            self.assertEqual(config["mcp_servers"]["ilspy"]["env"]["CUSTOM_VALUE"], "preserve me")
            MODULE.configure(root)
            self.assertEqual(text, path.read_text())

    def test_preserves_inline_environment_values(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self.make_root(directory)
            path = root / ".codex/config.toml"
            path.write_text('[mcp_servers.ilspy]\ncommand="old"\nenv={ USER_CUSTOM="keep", DOTNET_ROOT="old" }\n')
            MODULE.configure(root)
            config = tomllib.loads(path.read_text())
            self.assertEqual(config["mcp_servers"]["ilspy"]["env"]["USER_CUSTOM"], "keep")
            MODULE.configure(root)

    def test_invalid_config_is_never_overwritten(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self.make_root(directory)
            path = root / ".codex/config.toml"
            original = "[invalid =\n"
            path.write_text(original)
            with self.assertRaises(tomllib.TOMLDecodeError):
                MODULE.configure(root)
            self.assertEqual(path.read_text(), original)


if __name__ == "__main__":
    unittest.main()
