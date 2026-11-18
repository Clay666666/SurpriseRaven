"""Configure project MCP servers and verify real stdio requests.

Configuration uses Python's standard library. Smoke tests run with the existing
STS2MCP virtual environment, which supplies the MCP client SDK. No packages or
interpreters are downloaded by this script.
"""

from __future__ import annotations

import argparse
import asyncio
import json
import os
from pathlib import Path
import re
import sys
import tomllib
import traceback
from datetime import datetime, timezone
from typing import Any


def tool_paths(root: Path) -> dict[str, Path]:
    paths = {
        "dotnet": root / "tools/dotnet/dotnet.exe",
        "ilspy": root / "tools/ILSpy-Mcp/bin/Release/net8.0/ILSpy.Mcp.dll",
        "python": root / "tools/STS2MCP/mcp/.venv/Scripts/python.exe",
        "bridge": root / "tools/STS2MCP/mcp/server.py",
    }
    for name, path in paths.items():
        if not path.is_file():
            raise RuntimeError(f"Missing {name}: {path}. Complete tool installation first.")
    return paths


def server_definitions(root: Path) -> dict[str, dict[str, Any]]:
    paths = tool_paths(root)
    defaults = {"enabled": True, "required": False,
                "startup_timeout_sec": 60, "tool_timeout_sec": 120}
    return {
        "ilspy": {
            **defaults,
            "command": str(paths["dotnet"]),
            "args": [str(paths["ilspy"])],
            "cwd": str(paths["ilspy"].parent),
            "env": {"DOTNET_ROOT": str(paths["dotnet"].parent),
                    "DOTNET_NOLOGO": "1", "DOTNET_CLI_TELEMETRY_OPTOUT": "1"},
        },
        "sts2mcp": {
            **defaults,
            "command": str(paths["python"]),
            "args": ["-u", str(paths["bridge"]), "--no-trust-env"],
            "cwd": str(paths["bridge"].parent),
            "env": {"PYTHONUTF8": "1", "PYTHONUNBUFFERED": "1"},
        },
    }


def table_path(header: str) -> tuple[str, ...] | None:
    """Parse a TOML table header, including quoted dotted key components."""
    try:
        data = tomllib.loads(header + "\n__scaffold_probe__ = 1\n")
    except tomllib.TOMLDecodeError:
        return None
    path: list[str] = []
    while isinstance(data, dict):
        if "__scaffold_probe__" in data:
            return tuple(path)
        if len(data) != 1:
            return None
        key, data = next(iter(data.items()))
        path.append(key)
    return None


def upsert_table(text: str, path: tuple[str, ...], updates: dict[str, Any]) -> str:
    """Replace owned assignments only, preserving comments and unknown keys."""
    lines = text.splitlines(keepends=True)
    headers: list[tuple[int, tuple[str, ...] | None]] = []
    for index, line in enumerate(lines):
        if not line.lstrip().startswith("["):
            continue
        try:
            tomllib.loads("".join(lines[:index]))
        except tomllib.TOMLDecodeError:
            continue  # A bracket inside a multiline value is not a table.
        parsed = table_path(line.strip())
        if parsed is not None or line.lstrip().startswith("[["):
            headers.append((index, parsed))
    if not path:
        start, end = 0, headers[0][0] if headers else len(lines)
    else:
        matches = [(pos, header) for pos, header in enumerate(headers) if header[1] == path]
        if not matches:
            header = "[" + ".".join(json.dumps(part) for part in path) + "]\n"
            return text.rstrip() + "\n\n" + header + "".join(
                f"{key} = {toml_value(value)}\n" for key, value in updates.items()
            )
        position, (index, _) = matches[0]
        start = index + 1
        end = headers[position + 1][0] if position + 1 < len(headers) else len(lines)
    remaining = dict(updates)
    output: list[str] = []
    index = start
    while index < end:
        assignment = re.match(r'^\s*([A-Za-z0-9_-]+|"[^"\\]*"|\x27[^\x27]*\x27)\s*=', lines[index])
        key = assignment.group(1).strip('"\x27') if assignment else None
        if key not in remaining:
            output.append(lines[index])
            index += 1
            continue
        value_end = index + 1
        while value_end <= end:
            try:
                tomllib.loads("".join(lines[index:value_end]))
                break
            except tomllib.TOMLDecodeError:
                value_end += 1
        if value_end > end:
            raise RuntimeError(f"Cannot safely replace TOML assignment {'.'.join(path + (key,))}")
        output.append(f"{key} = {toml_value(remaining.pop(key))}\n")
        index = value_end
    if output and not output[-1].endswith("\n"):
        output[-1] += "\n"
    output.extend(f"{key} = {toml_value(value)}\n" for key, value in remaining.items())
    return "".join(lines[:start] + output + lines[end:])


def toml_value(value: Any) -> str:
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, str):
        return json.dumps(value, ensure_ascii=False)
    if isinstance(value, (int, float)):
        return str(value)
    if isinstance(value, list):
        return "[" + ", ".join(toml_value(item) for item in value) + "]"
    if isinstance(value, dict):
        return "{ " + ", ".join(json.dumps(key) + " = " + toml_value(item)
                                  for key, item in value.items()) + " }"
    raise TypeError(f"Unsupported generated TOML value: {type(value)}")


def configure(root: Path) -> None:
    definitions = server_definitions(root)
    config = root / ".codex/config.toml"
    original = config.read_text(encoding="utf-8-sig") if config.exists() else ""
    previous = tomllib.loads(original)
    text = upsert_table(original, (), {"mcp_optional_startup_grace_ms": 0})
    for name, definition in definitions.items():
        old = previous.get("mcp_servers", {}).get(name, {})
        if "url" in old:
            raise RuntimeError(f"Existing MCP server {name} uses HTTP. Rename that server before configuring the stdio tool.")
        headers = [table_path(line.strip()) for line in original.splitlines() if line.lstrip().startswith("[")]
        inline_env = "env" in old and ("mcp_servers", name, "env") not in headers
        updates = {key: value for key, value in definition.items() if key != "env"}
        if inline_env:
            updates["env"] = {**old["env"], **definition["env"]}
        text = upsert_table(text, ("mcp_servers", name), updates)
        if not inline_env:
            text = upsert_table(text, ("mcp_servers", name, "env"), definition["env"])
    expected = tomllib.loads(text)
    for name, definition in definitions.items():
        for key, value in definition.items():
            actual = expected["mcp_servers"][name][key]
            if key == "env":
                assert all(actual[k] == v for k, v in value.items())
            else:
                assert actual == value
    config.parent.mkdir(exist_ok=True)
    if text != original:
        if original:
            backup = config.with_name("config.toml.before-scaffold")
            if not backup.exists():
                backup.write_text(original, encoding="utf-8")
        temporary = config.with_name("config.toml.tmp")
        temporary.write_text(text, encoding="utf-8", newline="\n")
        temporary.replace(config)
    print(f"Codex MCP configuration ready: {config}")
    print("Servers: ilspy (stdio), sts2mcp (stdio). Reload the project/session to load the new tools.")


def result_text(result: Any) -> str:
    if getattr(result, "isError", False):
        raise RuntimeError("MCP tool returned an error: " + str(result))
    content = [item.text for item in result.content if getattr(item, "type", None) == "text"]
    text = "\n".join(content)
    if not text.strip() or text.lstrip().startswith("Error:"):
        raise RuntimeError("MCP tool returned empty/error content: " + text)
    return text


def save_json(path: Path, value: Any) -> None:
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def describe_error(error: BaseException) -> str:
    """Expose leaf failures hidden by anyio/asyncio TaskGroup wrappers."""
    if isinstance(error, BaseExceptionGroup):
        return "; ".join(describe_error(child) for child in error.exceptions)
    return f"{type(error).__name__}: {error}"


async def run_server(root: Path, name: str, output: Path, game: bool = False,
                     round_trip: bool = False, custom_call: tuple[str, dict[str, Any]] | None = None) -> dict[str, Any]:
    if game or round_trip:
        raise RuntimeError("GUI/menu game verification is prohibited; use verify-environment.ps1 -HeadlessGame")
    from mcp import ClientSession, StdioServerParameters
    from mcp.client.stdio import stdio_client

    config_path = root / ".codex/config.toml"
    loaded = tomllib.loads(config_path.read_text(encoding="utf-8-sig"))
    definition = loaded["mcp_servers"][name]
    if not definition.get("enabled", True):
        raise RuntimeError(f"MCP {name} is disabled in project config")
    expected = server_definitions(root)[name]
    for key in ("command", "args", "cwd"):
        if definition.get(key) != expected[key]:
            raise RuntimeError(f"MCP {name}.{key} differs from generated configuration; run configure-codex.ps1")
    params = StdioServerParameters(command=definition["command"], args=definition["args"],
                                   cwd=definition["cwd"], env={**os.environ, **definition.get("env", {})})
    with (output / f"{name}.stderr.log").open("w", encoding="utf-8") as error_log:
        async with stdio_client(params, errlog=error_log) as (reader, writer):
            async with ClientSession(reader, writer) as session:
                init = await asyncio.wait_for(session.initialize(), timeout=60)
                listing = await asyncio.wait_for(session.list_tools(), timeout=60)
                names = [tool.name for tool in listing.tools]
                save_json(output / f"{name}.initialize.json", init.model_dump(mode="json"))
                save_json(output / f"{name}.tools.json", listing.model_dump(mode="json"))
                report: dict[str, Any] = {"server": name, "protocol_version": init.protocolVersion,
                                          "tool_count": len(names), "tools": names}
                if name == "ilspy":
                    if not listing.tools or any(
                        tool.annotations is None
                        or tool.annotations.readOnlyHint is not True
                        or tool.annotations.destructiveHint is not False
                        for tool in listing.tools
                    ):
                        raise RuntimeError("ILSpy tools must declare their verified read-only behavior; rerun install.bat to apply the compatibility patch")
                    report["read_only_annotations"] = "passed"
                if custom_call:
                    tool, arguments = custom_call
                    if tool not in names:
                        raise RuntimeError(f"Missing tool {tool} in {name}")
                    result = await asyncio.wait_for(session.call_tool(tool, arguments), timeout=120)
                    text = result_text(result)
                    save_json(output / "call.result.json", result.model_dump(mode="json"))
                    (output / "call.content.txt").write_text(text, encoding="utf-8")
                    report["call"] = {"tool": tool, "characters": len(text)}
                elif name == "ilspy":
                    if "decompile_type" not in names:
                        raise RuntimeError("ILSpy did not expose decompile_type")
                    baseline_assembly = root / "references/0.111.0/sts2.dll"
                    result = await asyncio.wait_for(session.call_tool("decompile_type", {
                        "assemblyPath": str(baseline_assembly),
                        "typeName": "MegaCrit.Sts2.Core.Hooks.Hook",
                    }), timeout=120)
                    text = result_text(result)
                    if "class Hook" not in text:
                        raise RuntimeError("ILSpy response did not contain the expected Hook type")
                    (output / "Hook.decompiled.cs").write_text(text, encoding="utf-8")
                    report["decompiled_type"] = "MegaCrit.Sts2.Core.Hooks.Hook"
                    report["assembly_path"] = str(baseline_assembly)
                elif name == "sts2mcp":
                    if "get_game_state" not in names:
                        raise RuntimeError("STS2MCP did not expose get_game_state")
                print(f"PASS {name}: initialize, tools/list ({len(names)} tools)" +
                      (", decompile_type" if name == "ilspy" and not custom_call else ""), flush=True)
                return report


async def smoke(args: argparse.Namespace, root: Path, output: Path) -> None:
    if getattr(args, "game", False) or getattr(args, "no_menu_round_trip", False):
        raise RuntimeError("GUI/menu game verification is prohibited; use verify-environment.ps1 -HeadlessGame")
    report: dict[str, Any] = {"started_utc": datetime.now(timezone.utc).isoformat(), "status": "running",
                              "runtime_check": "offline", "gui_allowed": False}
    try:
        if args.mode == "call":
            arguments = json.loads(Path(args.arguments_file).read_text(encoding="utf-8-sig"))
            report["servers"] = [await run_server(root, args.server, output,
                                                  custom_call=(args.tool, arguments))]
        else:
            results = await asyncio.gather(
                run_server(root, "ilspy", output),
                run_server(root, "sts2mcp", output),
                return_exceptions=True,
            )
            report["servers"] = [value if isinstance(value, dict) else {
                "error": describe_error(value),
                "traceback": "".join(traceback.format_exception(value)),
            } for value in results]
            errors = [value for value in results if isinstance(value, BaseException)]
            if errors:
                raise BaseExceptionGroup("MCP smoke verification failed", errors)
        report["status"] = "passed"
    except BaseException as error:
        report["status"] = "failed"
        report["error"] = describe_error(error)
        report["traceback"] = "".join(traceback.format_exception(error))
        (output / "failure.traceback.txt").write_text(report["traceback"], encoding="utf-8")
        raise
    finally:
        report["finished_utc"] = datetime.now(timezone.utc).isoformat()
        save_json(output / "mcp-report.json", report)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("configure", "smoke", "call"))
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--output-dir", type=Path)
    parser.add_argument("--game", action="store_true")
    parser.add_argument("--no-menu-round-trip", action="store_true")
    parser.add_argument("--server", choices=("ilspy", "sts2mcp"), default="ilspy")
    parser.add_argument("--tool")
    parser.add_argument("--arguments-file")
    args = parser.parse_args()
    if args.game or args.no_menu_round_trip:
        parser.error("GUI/menu game verification is prohibited; use verify-environment.ps1 -HeadlessGame")
    root = args.root.resolve()
    if args.mode == "configure":
        configure(root)
        return
    if args.mode == "call" and (not args.tool or not args.arguments_file):
        parser.error("call requires --tool and --arguments-file")
    output = args.output_dir or root / "artifacts/verification" / datetime.now().strftime("%Y%m%d-%H%M%S-%f")
    output.mkdir(parents=True, exist_ok=True)
    print(f"Verification output: {output}", flush=True)
    asyncio.run(smoke(args, root, output))


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"ERROR: {describe_error(error)}", file=sys.stderr)
        sys.exit(1)
