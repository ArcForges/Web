# SPDX-License-Identifier: AGPL-3.0-only
"""Local verification steps of Web, loaded by eng/verify.py (which injects `Step`).

The full profile is every validation job of .github/workflows/ci.yml plus `candidate` (the sealed C# static candidate and
profile bundle that `deploy` ships; main pushes no longer re-validate it), each on the host the hosted job used:
- lane `win` (Windows, in <main checkout>/.worktree/_verify): `fresh-artifacts`, then `source (windows-latest)`: npm ci
  and the evaluated licence report; then `csharp (windows-latest)`: the locked restore, the formatting gate, the Release
  build and tests of the CSHARP_PROJECTS / CSHARP_TEST_PROJECTS of ci.yml, the Account and Chat and Operations publishes,
  and the static Site built twice and compared byte for byte.
- lane `linux` (Linux, in ~/.cache/arcforges-verify/Web): `clone-identity`, `fresh-artifacts`, the dependency audit of
  `quality`, `csharp (ubuntu-latest)` (the same steps as on Windows), `source (ubuntu-latest)` (`npm run check`, which
  runs the candidate tests against the Release tool the csharp build produced from the same locked restore; hosted, the
  source job builds that tool itself), then `candidate`. Everything that runs dotnet or npm in one checkout is in one lane:
  concurrent restores or builds of shared projects in one checkout would race.
- lane `scan`: the whitespace check of the commits since the merge base (`git diff --check`) and the secret
  scan of `quality` (the pinned gitleaks image digest, .gitleaks.toml) over the same range ({range}), in Docker
  without network and with a read-only mount of the clone; lane `actionlint` only when .github/workflows/** changed.

Differences from the hosted jobs:
- The clone has the identity of a pull-request checkout of ArcForges/Web (`clone-identity`): its `origin` URL is the
  GitHub repository, which the naming scan requires, HEAD is a local branch at the pushed commit and `origin/main` is the
  merge base with main, which the provenance audit compares against (hosted: the PR base from the event, read through
  GITHUB_EVENT_PATH). Nothing is fetched from a remote.
- `CI` and `GITHUB_*` are not set (the browser opt-in guards and the Site parity test read them). The two MSBuild
  properties that ci.yml's `CI=true` selects in Directory.Build.props are set directly: RestoreLockedMode (the implicit
  restores are locked, as the explicit `--locked-mode` restores are) and ContinuousIntegrationBuild (deterministic source
  paths, as in the hosted build). The Blazor App is built as each hosted runner builds it (see LINUX_ENV and WIN_ENV):
  on Linux without the wasm-tools workload (WasmNativeWorkload=false; the ubuntu-latest runner has none and publishes
  without relinking), on Windows with it (the hosted Windows C# job installs wasm-tools at workload set 10.0.401 into its
  isolated SDK root and relinks dotnet.native with Emscripten 10.0.12 in its Release build and publishes). The Windows
  steps use one SDK root whatever shell runs the leg: DOTNET_ROOT, which the workstation's environment loaders set (Env.md:
  the user-local .dotnet of the user profile), first on PATH; `win-workload-preflight` checks that its workload set and
  wasm-tools match the lock and the hosted runner (see WORKLOAD_PREFLIGHT). The Site and the candidate take `{sha}` where
  ci.yml passes GITHUB_SHA, and the
  candidate's release version is the repository's local version `0.1.0-local` (hosted: `0.1.0-ci.<run number>.<attempt>`;
  the build identity is then `local.<commit>` instead of the run id). The candidate verification gets GITHUB_SHA={sha} for
  that one command only, because tooling/candidate.ts passes `--expected-source` from it (hosted: the same value). The
  hosted `if-no-files-found: error` of the licence evidence upload is the explicit evidence test after `npm run check`.
  Artifacts are not uploaded; they stay in artifacts/ until the next run, which starts from an empty artifacts/ as a
  fresh hosted checkout does.
- npm on Windows is the npm bundled with the selected Node (hosted: setup-node's Node with its bundled npm), because a
  globally installed newer npm earlier on PATH fails the repository's engine-strict pin.
- The secret scan covers the commits since the merge base, not the full history: history before it is main, scanned
  when its own PRs were verified.
- Not reproduced: GitHub dependency review (a GitHub service; external PRs keep it; the locked restores with NuGet audit
  and `npm audit` cover vulnerable packages locally) and CodeQL (external PRs, dispatch and the weekly schedule).
- The browser test project stays local opt-in, as in CI: it is not in the project lists and is never restored or built.
Every change runs the full profile: the naming scan and the provenance inventory read Markdown too.
"""

import os
import sys
from pathlib import Path

REPOSITORY = "Web"

# The one .NET SDK root of the Windows steps: DOTNET_ROOT (a persistent user variable on this workstation, also set by
# the environment loaders), not whichever dotnet the running shell finds first on PATH (a plain PowerShell finds
# C:\Program Files\dotnet through the machine PATH; the loaders select the user-local root). Without a valid DOTNET_ROOT
# the Windows tool probe fails and the leg reports an error.
WIN_DOTNET_ROOT = os.environ.get("DOTNET_ROOT", "") if os.name == "nt" else ""
if WIN_DOTNET_ROOT and not Path(WIN_DOTNET_ROOT, "dotnet.exe").is_file():
    WIN_DOTNET_ROOT = ""
WIN_DOTNET = str(Path(WIN_DOTNET_ROOT, "dotnet.exe")) if WIN_DOTNET_ROOT else ""


def _win_dotnet(*args: str) -> list[str]:
    if WIN_DOTNET:
        return [WIN_DOTNET, *args]
    return [sys.executable, "-c", "import sys; sys.exit('DOTNET_ROOT does not name a .NET SDK root with dotnet.exe; set it "
            "to the root the Windows steps use (Env.md: the user-local .dotnet of the user profile)')"]

# The npm bundled with the Node that `node` selects (Windows; see above).
_BUNDLED_NPM = ("require('path').join(require('path').dirname(process.execPath), 'node_modules', 'npm', "
                "'bin', 'npm-cli.js')")

# Tool versions recorded in each leg's summary (the python running eng/verify.py is added by the core).
FINGERPRINT = {
    "win": {
        "dotnet-root": [sys.executable, "-c", f"print({WIN_DOTNET_ROOT!r})"] if WIN_DOTNET else _win_dotnet(),
        "dotnet-sdks": _win_dotnet("--list-sdks"),
        "dotnet-runtimes": _win_dotnet("--list-runtimes"),
        "node": ["node", "--version"],
        "npm": ["node", "-p", "require(" + _BUNDLED_NPM.replace("'bin', 'npm-cli.js'", "'package.json'") + ").version"],
        "dotnet-workloads": _win_dotnet("workload", "--version"),
        # No Windows git entry: the steps use git only for read-only plumbing queries (ls-files, rev-parse, status, show,
        # log), and which git a shell finds differs here (Git Bash: Scoop's 2.56; PowerShell: Git for Windows 2.55 from
        # the machine PATH), so it would record the running shell instead of the tools that build and test.
    },
    "linux": {
        "dotnet-sdks": "dotnet --list-sdks | tr '\\n' ' '",
        "dotnet-runtimes": "dotnet --list-runtimes | tr '\\n' ' '",
        "dotnet-workloads": "dotnet workload --version",
        "node": "node --version",
        "npm": "npm --version",
        "python": "python --version",
        "docker": "docker version --format '{{.Server.Version}}'",
        "git": "git --version",
    },
}

# ci.yml CSHARP_PROJECTS and CSHARP_TEST_PROJECTS (the CI authority; win.slnx is not used). Keep them identical.
CSHARP_PROJECTS = [
    "src/ArcForges.Web.Ui/ArcForges.Web.Ui.csproj",
    "src/ArcForges.Web.Site/ArcForges.Web.Site.csproj",
    "src/ArcForges.Web.App/ArcForges.Web.App.csproj",
    "src/ArcForges.Web.Operations/ArcForges.Web.Operations.csproj",
    "tools/ArcForges.Web.Tooling/ArcForges.Web.Tooling.csproj",
    "tests/ArcForges.Web.Tooling.Tests/ArcForges.Web.Tooling.Tests.csproj",
    "tests/ArcForges.Web.Site.Tests/ArcForges.Web.Site.Tests.csproj",
    "tests/ArcForges.Web.Ui.Tests/ArcForges.Web.Ui.Tests.csproj",
    "tests/ArcForges.Web.App.Tests/ArcForges.Web.App.Tests.csproj",
    "tests/ArcForges.Web.Operations.Tests/ArcForges.Web.Operations.Tests.csproj",
    "tests/ArcForges.Web.Policy.Tests/ArcForges.Web.Policy.Tests.csproj",
]
CSHARP_TEST_PROJECTS = [p for p in CSHARP_PROJECTS if p.startswith("tests/")]

TOOL = "tools/ArcForges.Web.Tooling/bin/Release/net10.0/ArcForges.Web.Tooling.dll"
VERSION = "0.1.0-local"

# Timeouts are five to ten times the warm durations measured on this workstation (18 logical processors, both hosts'
# lanes concurrently; eng/verify.py's summary.json): the longest steps took about 75 s (format), 45 s (build) and 45 s
# (publish), the whole profile about 3.5 minutes. The floor is one to five minutes, and the restores allow a cold NuGet
# cache.

# The environment of ci.yml without CI=true (see above). MSBuild node reuse is off, so that no build node outlives its step
# (a step's process tree is what a timeout or a failure stops).
ENV = {"DOTNET_NOLOGO": "1", "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
       "WRANGLER_SEND_METRICS": "false", "MSBUILDDISABLENODEREUSE": "1", "RestoreLockedMode": "true",
       "ContinuousIntegrationBuild": "true"}
# Linux: the ubuntu-latest runner has no wasm-tools workload, so its Release publish ships the runtime pack's prebuilt
# dotnet.native files ("Publishing without optimizations"). A Linux host with wasm-tools installed would relink
# dotnet.native with emcc (measured on the workstation's former Linux environment: initial JS 142154 gzip bytes, over the
# 138538 budget limit, against 125997 without the workload, at a baseline of 125944). WasmNativeWorkload=false gives the
# App the runner's evaluation on any Linux host; the App sets no property that needs the workload (no AOT, no native
# build).
LINUX_ENV = {**ENV, "WasmNativeWorkload": "false"}
# Windows: the hosted Windows C# job installs wasm-tools at workload set 10.0.401 into its isolated SDK root, and its
# Release build and publishes relink with Emscripten 10.0.12, so the workload stays in. The SDK root is WIN_DOTNET_ROOT,
# first on PATH for every tool the steps start (pwsh, node and python find dotnet through it).
WIN_ENV = {**ENV, "DOTNET_ROOT": WIN_DOTNET_ROOT,
           "PATH": os.pathsep.join(filter(None, [WIN_DOTNET_ROOT, os.environ.get("PATH", "")]))}

# ci.yml's secret scan (same pinned image digest and redaction, with .gitleaks.toml named explicitly), over the pushed
# range, without network and with a read-only mount of the verification clone.
SECRET_SCAN = (
    "docker run --rm --network none -v \"$PWD:/repo:ro\" "
    "-e GIT_CONFIG_COUNT=1 -e GIT_CONFIG_KEY_0=safe.directory -e GIT_CONFIG_VALUE_0=/repo "
    "ghcr.io/gitleaks/gitleaks:v8.30.1@sha256:c00b6bd0aeb3071cbcb79009cb16a60dd9e0a7c60e2be9ab65d25e6bc8abbb7f "
    "git /repo --redact=100 --no-banner --verbose --config /repo/.gitleaks.toml "
    "--log-opts=\"--full-history {range}\""
)

# actionlint as pinned in ci.yml, cached once on the Linux host; the archive is checked on every run.
ACTIONLINT = (
    "set -euo pipefail; v=1.7.12; "
    "sum=8aca8db96f1b94770f1b0d72b6dddcb1ebb8123cb3712530b08cc387b349a3d8; "
    "d=\"$HOME/.cache/arcforges-verify/tools\"; t=\"$d/actionlint_${v}_linux_amd64.tar.gz\"; mkdir -p \"$d\"; "
    "if [ ! -f \"$t\" ]; then "
    "curl --fail --silent --show-error --location --output \"$t.part\" "
    "\"https://github.com/rhysd/actionlint/releases/download/v${v}/actionlint_${v}_linux_amd64.tar.gz\"; "
    "echo \"$sum  $t.part\" | sha256sum --check --quiet; mv \"$t.part\" \"$t\"; fi; "
    "echo \"$sum  $t\" | sha256sum --check --quiet; "
    "mkdir -p \"$d/actionlint-$v\"; tar -xzf \"$t\" -C \"$d/actionlint-$v\" actionlint; "
    "\"$d/actionlint-$v/actionlint\" -color"
)

# ci.yml `candidate`, from the clean checkout CI builds it from. The tool's locked restore and Release build are the
# csharp steps of this lane on the same commit, so they are not repeated; the App is published again into the candidate's
# own directory, as in the hosted job.
CANDIDATE = (
    "set -euo pipefail; unset CLOUDFLARE_API_TOKEN CLOUDFLARE_ACCOUNT_ID; "
    "if [ -n \"$(git status --porcelain)\" ]; then git status --porcelain; "
    "echo 'the checkout changed during verification; the candidate is built from a clean checkout' >&2; exit 1; fi; "
    f"tool={TOOL}; version={VERSION}; "
    "rm -rf artifacts/publish artifacts/profiles artifacts/inputs artifacts/candidate artifacts/candidate-repeat; "
    "dotnet publish src/ArcForges.Web.App/ArcForges.Web.App.csproj --no-restore -c Release -o artifacts/publish/app; "
    "dotnet \"$tool\" profiles budget --publish artifacts/publish/app --budgets eng/policy/profile-budgets.json; "
    "mkdir -p artifacts/inputs; "
    "node tooling/candidate.ts worker artifacts/inputs/index.js; "
    "node tooling/candidate.ts identity \"$version\" artifacts/inputs/build-info.json; "
    "for out in artifacts/candidate artifacts/candidate-repeat; do "
    "dotnet \"$tool\" candidate build --out \"$out\" --repo \"$PWD\" --source-ref {sha} --version \"$version\" "
    "--identity artifacts/inputs/build-info.json --worker artifacts/inputs/index.js --dirty false; done; "
    "diff -r artifacts/candidate artifacts/candidate-repeat; rm -rf artifacts/candidate-repeat; "
    "GITHUB_SHA={sha} node tooling/candidate.ts verify artifacts/candidate; "
    "dotnet \"$tool\" profiles bundle --publish artifacts/publish/app --out artifacts/profiles; "
    "bundles=(artifacts/profiles/web-profiles-*.tar); test \"${#bundles[@]}\" = 1; "
    "dotnet \"$tool\" profiles verify --bundle \"${bundles[0]}\""
)

# The Windows SDK root must build the App as the windows-latest runner does: Microsoft.NET.Sdk.WebAssembly.Pack at the
# version the App's packages.lock.json records (the SDK's bundled version, as on the hosted runners and on Linux; an
# installed workload set of the SDK's feature band replaces it with its Mono toolchain manifest's version, and a different
# set fails the locked restore with NU1004), and the wasm-tools workload installed with the packs of its current
# manifests (the runner relinks with it; a workload listed without those packs leaves WasmNativeWorkload10 unset). This
# preflight checks both on WIN_DOTNET_ROOT, read-only, and names the exact commands for that root instead.
WORKLOAD_PREFLIGHT = (
    "import json, os, subprocess, sys\n"
    "root = os.environ.get('DOTNET_ROOT', '')\n"
    "dotnet = os.path.join(root, 'dotnet.exe')\n"
    "app = 'src/ArcForges.Web.App/ArcForges.Web.App.csproj'\n"
    "def run(*a):\n"
    "    r = subprocess.run([dotnet, *a], capture_output=True, text=True)\n"
    "    if r.returncode: sys.exit(f'{dotnet} {\" \".join(a)} failed ({r.returncode}):\\n{r.stdout}{r.stderr}')\n"
    "    return r.stdout\n"
    "info = json.loads(run('msbuild', app, '-getItem:KnownWebAssemblySdkPack', '-getProperty:NETCoreSdkVersion',\n"
    "                      '-getProperty:WasmNativeWorkload10'))\n"
    "version = info['Properties']['NETCoreSdkVersion']\n"
    "items = info['Items']['KnownWebAssemblySdkPack']\n"
    "sdk = sorted({i['WebAssemblySdkPackVersion'] for i in items if i.get('TargetFramework') == 'net10.0'})\n"
    "lock = json.load(open('src/ArcForges.Web.App/packages.lock.json', encoding='utf-8'))\n"
    "locked = lock['dependencies']['net10.0']['Microsoft.NET.Sdk.WebAssembly.Pack']['resolved']\n"
    "workload_set = run('workload', '--version').strip()\n"
    "wasm_tools = any(line.split()[:1] == ['wasm-tools'] for line in run('workload', 'list').splitlines())\n"
    "problems, commands = [], []\n"
    "if sdk != [locked]:\n"
    "    problems.append(f'SDK {version} with workload set {workload_set} resolves Microsoft.NET.Sdk.WebAssembly.Pack '\n"
    "                    f'{\", \".join(sdk)} for {app}, which differs from {locked} in its packages.lock.json (as on the '\n"
    "                    'hosted runners and on Linux)')\n"
    "    commands.append(f'& \"{dotnet}\" workload update --version {version}')\n"
    "if not wasm_tools:\n"
    "    problems.append('the wasm-tools workload, which the windows-latest runner builds and publishes the App with, is '\n"
    "                    'not installed')\n"
    "    commands.append(f'& \"{dotnet}\" workload install wasm-tools --version {version}')\n"
    "elif info['Properties']['WasmNativeWorkload10'] != 'true':\n"
    "    problems.append('wasm-tools is listed, but the packs of its current manifests are not installed, so the App '\n"
    "                    'would not relink as on the windows-latest runner')\n"
    "    if not commands: commands.append(f'& \"{dotnet}\" workload update --version {version}')\n"
    "if problems:\n"
    "    sys.exit(f'The Windows SDK root {root} does not build the App as hosted CI does: ' + '; '.join(problems)\n"
    "             + '. Prerequisite (an install by the user, in PowerShell): ' + ' ; '.join(commands)\n"
    "             + ' (the workload set whose Mono toolchain and Emscripten packs match the lock), then verify again.')\n"
    "print(f'SDK root {root}: SDK {version}, workload set {workload_set}, wasm-tools installed, '\n"
    "      f'Microsoft.NET.Sdk.WebAssembly.Pack {locked} as locked')\n"
)

# The Site built twice from the same commit and compared byte for byte (ci.yml uses `diff -r`; Windows has no diff on
# the step's PATH, so the comparison there is a recursive byte comparison in Python with the same result).
COMPARE = (
    "import filecmp, sys\n"
    "def same(a, b):\n"
    "    d = filecmp.dircmp(a, b, ignore=[])\n"
    "    if d.left_only or d.right_only or d.funny_files: return False\n"
    "    m, x, e = filecmp.cmpfiles(a, b, d.common_files, shallow=False)\n"
    "    return not x and not e and all(same(f'{a}/{s}', f'{b}/{s}') for s in d.common_dirs)\n"
    "sys.exit(0 if same(sys.argv[1], sys.argv[2]) else 'the two Site builds differ')\n"
)


def select_profile(changed: list[str]) -> str:
    return "full"


def workflows_changed(changed: list[str]) -> bool:
    return any(path.startswith(".github/workflows/") for path in changed)


def pwsh(script: str) -> list[str]:
    return ["pwsh", "-NoProfile", "-NonInteractive", "-Command",
            "$ErrorActionPreference = 'Stop'; " + script + "; exit $LASTEXITCODE"]


def each(projects: list[str], command: str) -> str:
    """A PowerShell loop that runs `dotnet <command>` for each project and stops at the first failure."""
    listed = ", ".join(f"'{p}'" for p in projects)
    return (f"foreach ($p in @({listed})) {{ dotnet {command}; "
            "if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }")


def bash_each(projects: list[str], command: str) -> str:
    return f"set -euo pipefail; for p in {' '.join(projects)}; do dotnet {command}; done"


def win(step_id: str, cmd: list[str], timeout: int, **options) -> Step:
    return Step(step_id, "win", cmd, timeout=timeout, env=WIN_ENV, **options)


def linux(step_id: str, cmd: str, timeout: int, **options) -> Step:
    return Step(step_id, "linux", cmd, timeout=timeout, env=LINUX_ENV, **options)


STEPS = [
    # ci.yml source (windows-latest), then csharp (windows-latest)
    win("win-fresh-artifacts", pwsh("if (Test-Path artifacts) { Remove-Item -Recurse -Force artifacts }"), 120),
    win("win-npm-ci", pwsh(f"node (node -p \"{_BUNDLED_NPM}\") ci --ignore-scripts"), 300),
    win("win-licence-evaluated", ["node", "tooling/project.ts", "licence-evaluated"], 300),
    win("win-workload-preflight", ["python", "-I", "-c", WORKLOAD_PREFLIGHT], 120),
    win("win-restore", pwsh(each(CSHARP_PROJECTS, "restore $p --locked-mode")), 600),
    win("win-format", pwsh(each(CSHARP_PROJECTS, "format $p --verify-no-changes --no-restore")), 900),
    win("win-build", pwsh(each(CSHARP_PROJECTS, "build $p --no-restore -c Release")), 600),
    win("win-test", pwsh(each(CSHARP_TEST_PROJECTS, "test $p --no-build -c Release")), 600),
    win("win-publish-profiles", pwsh(
        "dotnet publish src/ArcForges.Web.App/ArcForges.Web.App.csproj --no-restore -c Release -o artifacts/profiles/app; "
        "if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; "
        "dotnet publish src/ArcForges.Web.Operations/ArcForges.Web.Operations.csproj --no-restore -c Release "
        "-o artifacts/profiles/operations"), 600),
    win("win-site-twice", pwsh(
        f"dotnet {TOOL} site build --out artifacts/site --source-ref {{sha}}; "
        "if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; "
        f"dotnet {TOOL} site build --out artifacts/site-repeat --source-ref {{sha}}; "
        "if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; "
        "python -I -c '" + COMPARE.replace("'", "''") + "' artifacts/site artifacts/site-repeat; "
        "if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; Remove-Item -Recurse -Force artifacts/site-repeat"), 300),
    # the dependency audit of quality, csharp (ubuntu-latest), source (ubuntu-latest), then candidate
    linux("clone-identity", "set -e; git config remote.origin.url https://github.com/ArcForges/Web.git; "
        "git checkout -q -B arcforges-verify {sha}; git update-ref refs/remotes/origin/main {base}", 60),
    linux("fresh-artifacts", "rm -rf artifacts", 120),
    linux("npm-ci", "npm ci --ignore-scripts", 300),
    linux("npm-audit", "npm audit --audit-level=high", 300),
    linux("restore", bash_each(CSHARP_PROJECTS, "restore \"$p\" --locked-mode"), 600),
    linux("format", bash_each(CSHARP_PROJECTS, "format \"$p\" --verify-no-changes --no-restore"), 900),
    linux("build", bash_each(CSHARP_PROJECTS, "build \"$p\" --no-restore -c Release"), 600),
    linux("test", bash_each(CSHARP_TEST_PROJECTS, "test \"$p\" --no-build -c Release"), 300),
    linux("publish-profiles", "set -euo pipefail; "
        "dotnet publish src/ArcForges.Web.App/ArcForges.Web.App.csproj --no-restore -c Release -o artifacts/profiles/app; "
        "dotnet publish src/ArcForges.Web.Operations/ArcForges.Web.Operations.csproj --no-restore -c Release "
        "-o artifacts/profiles/operations", 600),
    linux("site-twice", f"set -euo pipefail; tool={TOOL}; "
        "dotnet \"$tool\" site build --out artifacts/site --source-ref {sha}; "
        "dotnet \"$tool\" site build --out artifacts/site-repeat --source-ref {sha}; "
        "diff -r artifacts/site artifacts/site-repeat; rm -rf artifacts/site-repeat", 300),
    linux("check", "set -euo pipefail; npm run check; "
        "test -f artifacts/evidence/licence-boundary.json; test -f artifacts/evidence/source-provenance.json", 600),
    linux("candidate", CANDIDATE, 600),
    # the whitespace check and quality's secret scan over the commits since the merge base; actionlint when a workflow changed
    linux("whitespace", "git diff --check {base} {sha}", 120, lane="scan"),
    linux("secret-scan", SECRET_SCAN, 600, lane="scan"),
    linux("actionlint", ACTIONLINT, 300, lane="actionlint", when=workflows_changed),
]
