# SPDX-License-Identifier: AGPL-3.0-only
"""Verify selected local/CI executables before dependency resolution."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]


def check(root: Path = ROOT, tools: tuple[str, ...] = ("python", "dotnet", "node", "npm")) -> dict:
    expected = {"python": (root / ".python-version").read_text().strip(),
                "dotnet": json.loads((root / "global.json").read_text())["sdk"]["version"],
                "node": (root / ".node-version").read_text().strip(),
                "npm": json.loads((root / "package.json").read_text())["packageManager"].removeprefix("npm@")}
    evidence = {"expected": expected, "invokingPython": {"version": platform.python_version(), "path": sys.executable},
                "tools": {}}
    if platform.python_version() != expected["python"]:
        raise ValueError("Invoking Python differs from .python-version")

    def run(name: str, *arguments: str) -> str:
        executable = shutil.which("npm.cmd" if name == "npm" and os.name == "nt" else name)
        if not executable:
            raise ValueError(name + " missing from PATH")
        evidence["tools"].setdefault(name, {"path": executable})
        result = subprocess.run([executable, *arguments], cwd=root, capture_output=True, text=True,
                                encoding="utf-8", timeout=60, check=True)
        return result.stdout.strip()

    if "python" in tools and run("python", "--version") != "Python " + expected["python"]:
        raise ValueError("PATH Python differs from .python-version")
    if "dotnet" in tools:
        actual = run("dotnet", "--version")
        if actual != expected["dotnet"]:
            raise ValueError("Selected SDK differs from global.json")
        rows = run("dotnet", "--list-sdks").splitlines()
        roots = [Path(row.split("[", 1)[1].removesuffix("]")) / actual
                 for row in rows if row.startswith(actual + " [")]
        props = json.loads(run("dotnet", "msbuild", "tools/ArcForges.Web.Tooling/ArcForges.Web.Tooling.csproj",
                               "-nologo", "-getProperty:NETCoreSdkVersion,MSBuildToolsPath,MSBuildSDKsPath"))["Properties"]
        if props["NETCoreSdkVersion"] != actual or not any(
                Path(props["MSBuildToolsPath"]).resolve() == sdk.resolve()
                and Path(props["MSBuildSDKsPath"]).resolve() == (sdk / "Sdks").resolve() for sdk in roots):
            raise ValueError("Effective MSBuild SDK and tools roots differ from the selected SDK")
        configured = os.environ.get("DOTNET_ROOT")
        if configured and not any((Path(configured) / "sdk" / actual).resolve() == sdk.resolve() for sdk in roots):
            raise ValueError("DOTNET_ROOT differs from the selected SDK installation")
        evidence["tools"]["dotnet"].update(version=actual, effective=props)
    if "node" in tools:
        actual = json.loads(run("node", "--eval", "console.log(JSON.stringify({version:process.versions.node,path:process.execPath}))"))
        if actual["version"] != expected["node"]:
            raise ValueError("Node differs from .node-version")
        evidence["tools"]["node"].update(actual)
    if "npm" in tools:
        actual = json.loads(run("npm", "version", "--json"))
        if actual["npm"] != expected["npm"] or actual["node"] != expected["node"]:
            raise ValueError("npm or its Node host differs from package.json")
        evidence["tools"]["npm"].update(version=actual["npm"], node=actual["node"])
    paths = subprocess.check_output(["git", "ls-files", "--", "*packages.lock.json", "*package-lock.json"],
                                    cwd=root, text=True).splitlines()
    evidence["locksSha256Lf"] = {name: hashlib.sha256((root / name).read_bytes().replace(b"\r\n", b"\n")).hexdigest()
                                 for name in sorted(paths)}
    return evidence


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tools", nargs="+", choices=("python", "dotnet", "node", "npm"))
    args = parser.parse_args()
    try:
        print(json.dumps(check(tools=tuple(args.tools) if args.tools else ("python", "dotnet", "node", "npm")), sort_keys=True))
    except (OSError, ValueError, KeyError, subprocess.SubprocessError) as error:
        sys.exit("toolchain mismatch: " + str(error))
