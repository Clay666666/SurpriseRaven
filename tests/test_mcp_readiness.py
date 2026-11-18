"""Offline MCP verification rejects legacy game/menu modes before opening clients."""

import argparse
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import AsyncMock, patch

SPEC = importlib.util.spec_from_file_location(
    "scaffold_mcp_offline", Path(__file__).resolve().parents[1] / "tools/mcp_smoke.py")
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class OfflineVerificationTests(unittest.IsolatedAsyncioTestCase):
    async def test_legacy_programmatic_modes_fail_before_client_initialization(self):
        with tempfile.TemporaryDirectory() as directory:
            for options in ({"game": True}, {"round_trip": True}):
                with self.assertRaisesRegex(RuntimeError, "prohibited"):
                    await MODULE.run_server(Path(directory), "sts2mcp", Path(directory), **options)
            self.assertEqual(list(Path(directory).iterdir()), [])

    async def test_default_smoke_only_opens_offline_servers(self):
        calls = []

        async def server(root, name, output):
            calls.append(name)
            return {"server": name}

        args = argparse.Namespace(mode="smoke", game=False, no_menu_round_trip=False)
        with tempfile.TemporaryDirectory() as directory, patch.object(MODULE, "run_server", server):
            output = Path(directory)
            await MODULE.smoke(args, output, output)
            import json
            report = json.loads((output / "mcp-report.json").read_text())
            self.assertEqual(report["runtime_check"], "offline")
            self.assertFalse(report["gui_allowed"])
            self.assertEqual(set(calls), {"ilspy", "sts2mcp"})

    async def test_legacy_smoke_mode_fails_before_server_calls(self):
        args = argparse.Namespace(mode="smoke", game=True, no_menu_round_trip=False)
        with tempfile.TemporaryDirectory() as directory, patch.object(MODULE, "run_server", AsyncMock()) as server:
            with self.assertRaisesRegex(RuntimeError, "prohibited"):
                await MODULE.smoke(args, Path(directory), Path(directory))
            server.assert_not_called()
            self.assertEqual(list(Path(directory).iterdir()), [])

    def test_nested_exception_groups_expose_leaf_causes(self):
        error = ExceptionGroup("wrapper", [ValueError("bad response"),
                                           ExceptionGroup("nested", [RuntimeError("not ready")])])
        self.assertEqual(MODULE.describe_error(error), "ValueError: bad response; RuntimeError: not ready")


if __name__ == "__main__":
    unittest.main()
