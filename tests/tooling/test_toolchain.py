# SPDX-License-Identifier: AGPL-3.0-only
"""Selection failures must precede any restore or build."""
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("web_toolchain", Path(__file__).parents[2] / "eng/check_toolchain.py")
toolchain = importlib.util.module_from_spec(spec)
spec.loader.exec_module(toolchain)


class ToolchainSelectionTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        for name, text in {".python-version": "3.15.2", ".node-version": "26.13.1",
                           "global.json": '{"sdk":{"version":"11.0.100"}}',
                           "package.json": '{"packageManager":"npm@12.2.2"}'}.items():
            (self.root / name).write_text(text)

    def run_selected(self, tools, outputs):
        def invoke(argv, **kwargs):
            key = tuple(argv[1:])
            return subprocess.CompletedProcess(argv, 0, outputs[key], "")
        with patch.object(toolchain.platform, "python_version", return_value="3.15.2"), \
                patch.object(toolchain.shutil, "which", side_effect=lambda name: str(self.root / name)), \
                patch.object(toolchain.subprocess, "run", side_effect=invoke), \
                patch.object(toolchain.subprocess, "check_output", return_value=""):
            return toolchain.check(self.root, tools)

    def test_python_only_does_not_execute_sdk(self):
        self.assertEqual({}, self.run_selected(("python",), {("--version",): "Python 3.15.2"})["locksSha256Lf"])

    def test_wrong_node_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "Node differs"):
            self.run_selected(("node",), {("--eval", "console.log(JSON.stringify({version:process.versions.node,path:process.execPath}))"):
                                          '{"version":"24.21.0","path":"other/node"}'})

    def test_npm_running_another_node_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "npm or its Node"):
            self.run_selected(("npm",), {("version", "--json"): '{"npm":"12.2.2","node":"24.21.0"}'})

    def test_effective_sdk_root_mismatch_is_rejected(self):
        properties = {"NETCoreSdkVersion": "11.0.100", "MSBuildToolsPath": str(self.root / "other/sdk/11.0.100"),
                      "MSBuildSDKsPath": str(self.root / "other/sdk/11.0.100/Sdks")}
        with self.assertRaisesRegex(ValueError, "Effective MSBuild"):
            self.run_selected(("dotnet",), {
                ("--version",): "11.0.100", ("--list-sdks",): f"11.0.100 [{self.root / 'sdk'}]",
                ("msbuild", "tools/ArcForges.Web.Tooling/ArcForges.Web.Tooling.csproj", "-nologo",
                 "-getProperty:NETCoreSdkVersion,MSBuildToolsPath,MSBuildSDKsPath"): json.dumps({"Properties": properties})})


if __name__ == "__main__":
    unittest.main()
