# SPDX-License-Identifier: AGPL-3.0-only
"""Local verification steps of Web, loaded by eng/verify.py (which injects `Step`).

The full profile is every validation job of .github/workflows/ci.yml plus `candidate` (the sealed C# static candidate and
profile bundle that `deploy` ships; main pushes no longer re-validate it), each on the host the hosted job used:
- lane `win` (Windows, in <main checkout>/.worktree/_verify): `fresh-artifacts`, then `source (windows-latest)`: npm ci
  and the evaluated licence report; then `csharp (windows-latest)`: the locked restore, the formatting gate, the Release
  build and tests of the CSHARP_PROJECTS / CSHARP_TEST_PROJECTS of ci.yml, the Account and Chat and Operations publishes,
  and the static Site built twice and compared byte for byte.
- lane `wsl` (WSL Debian, in ~/.cache/arcforges-verify/Web): `clone-identity`, `fresh-artifacts`, the dependency audit of
  `quality`, `csharp (ubuntu-latest)` (the same steps as on Windows), `source (ubuntu-latest)` (`npm run check`, which
  runs the candidate tests against the Release tool the csharp build produced from the same locked restore; hosted, the
  source job builds that tool itself), then `candidate`. Everything that runs dotnet or npm in one checkout is in one lane:
  concurrent restores or builds of shared projects in one checkout would race.
- lane `scan`: the whitespace check of the pushed range (the former pre-push hook, `git diff --check`) and the secret
  scan of `quality` (the pinned gitleaks image digest, .gitleaks.toml) over the pushed range ({range}), in WSL Docker
  without network and with a read-only mount of the clone; lane `actionlint` only when .github/workflows/** changed.

Differences from the hosted jobs:
- The clone has the identity of a pull-request checkout of ArcForges/Web (`clone-identity`): its `origin` URL is the
  GitHub repository, which the naming scan requires, HEAD is a local branch at the pushed commit and `origin/main` is the
  merge base with main, which the provenance audit compares against (hosted: the PR base from the event, read through
  GITHUB_EVENT_PATH). Nothing is fetched from a remote.
- `CI` and `GITHUB_*` are not set (the browser opt-in guards and the Site parity test read them). The two MSBuild
  properties that ci.yml's `CI=true` selects in Directory.Build.props are set directly: RestoreLockedMode (the implicit
  restores are locked, as the explicit `--locked-mode` restores are) and ContinuousIntegrationBuild (deterministic source
  paths, as in the hosted build). WasmNativeWorkload=false keeps the installed wasm-tools workload out of the Blazor
  build, as on a hosted runner (see ENV); on Windows, the installed workload set must also match the SDK
  (`win-workload-preflight`, see WORKLOAD_PREFLIGHT). The Site and the candidate take `{sha}` where ci.yml passes GITHUB_SHA, and the
  candidate's release version is the repository's local version `0.1.0-local` (hosted: `0.1.0-ci.<run number>.<attempt>`;
  the build identity is then `local.<commit>` instead of the run id). The candidate verification gets GITHUB_SHA={sha} for
  that one command only, because tooling/candidate.ts passes `--expected-source` from it (hosted: the same value). The
  hosted `if-no-files-found: error` of the licence evidence upload is the explicit evidence test after `npm run check`.
  Artifacts are not uploaded; they stay in artifacts/ until the next run, which starts from an empty artifacts/ as a
  fresh hosted checkout does.
- npm on Windows is the npm bundled with the selected Node (hosted: setup-node's Node with its bundled npm), because a
  globally installed newer npm earlier on PATH fails the repository's engine-strict pin.
- The secret scan covers the pushed range, not the full history: history before it was scanned when it was pushed.
- Not reproduced: GitHub dependency review (a GitHub service; external PRs keep it; the locked restores with NuGet audit
  and `npm audit` cover vulnerable packages locally) and CodeQL (external PRs, dispatch and the weekly schedule).
- The browser test project stays local opt-in, as in CI: it is not in the project lists and is never restored or built.
Every change runs the full profile: the naming scan and the provenance inventory read Markdown too.
"""

REPOSITORY = "Web"

# The npm bundled with the Node that `node` selects (Windows; see above).
_BUNDLED_NPM = ("require('path').join(require('path').dirname(process.execPath), 'node_modules', 'npm', "
                "'bin', 'npm-cli.js')")

# Tool versions that key a reusable pass (the python running eng/verify.py is added by the core).
FINGERPRINT = {
    "win": {
        "dotnet-sdks": ["dotnet", "--list-sdks"],
        "dotnet-runtimes": ["dotnet", "--list-runtimes"],
        "node": ["node", "--version"],
        "npm": ["node", "-p", "require(" + _BUNDLED_NPM.replace("'bin', 'npm-cli.js'", "'package.json'") + ").version"],
        "dotnet-workloads": ["dotnet", "workload", "--version"],
        "git": ["git", "--version"],
    },
    "wsl": {
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
# (a step's process tree is what a timeout or a failure stops). WasmNativeWorkload=false gives the Blazor App the
# evaluation of a hosted runner, which has no wasm-tools workload: this workstation has it installed on both hosts, and
# with it the Release publish relinks dotnet.native with emcc instead of shipping the runtime pack's prebuilt files
# (measured in WSL: initial JS 142154 gzip bytes, over the 138538 budget limit, against 125997 without the workload, at a
# baseline of 125944). The App sets no property that needs the workload (no AOT, no native build).
ENV = {"DOTNET_NOLOGO": "1", "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
       "WRANGLER_SEND_METRICS": "false", "MSBUILDDISABLENODEREUSE": "1", "RestoreLockedMode": "true",
       "ContinuousIntegrationBuild": "true", "WasmNativeWorkload": "false"}

# ci.yml's secret scan (same pinned image digest and redaction, with .gitleaks.toml named explicitly), over the pushed
# range, without network and with a read-only mount of the verification clone.
SECRET_SCAN = (
    "docker run --rm --network none -v \"$PWD:/repo:ro\" "
    "-e GIT_CONFIG_COUNT=1 -e GIT_CONFIG_KEY_0=safe.directory -e GIT_CONFIG_VALUE_0=/repo "
    "ghcr.io/gitleaks/gitleaks:v8.30.1@sha256:c00b6bd0aeb3071cbcb79009cb16a60dd9e0a7c60e2be9ab65d25e6bc8abbb7f "
    "git /repo --redact=100 --no-banner --verbose --config /repo/.gitleaks.toml "
    "--log-opts=\"--full-history {range}\""
)

# actionlint as pinned in ci.yml, cached once in WSL; the archive is checked on every run.
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

# The Blazor App's locked restore needs the Microsoft.NET.Sdk.WebAssembly.Pack version its packages.lock.json records
# (the SDK's bundled version, as on the hosted runners and in WSL). An installed workload set of the SDK's feature band
# overrides it with the version of its Mono toolchain manifest, so an older set fails the restore with NU1004. This
# preflight names the prerequisite instead.
WORKLOAD_PREFLIGHT = (
    "import json, subprocess, sys\n"
    "app = 'src/ArcForges.Web.App/ArcForges.Web.App.csproj'\n"
    "def run(*a):\n"
    "    r = subprocess.run(['dotnet', *a], capture_output=True, text=True)\n"
    "    if r.returncode: sys.exit(f'dotnet {\" \".join(a)} failed ({r.returncode}):\\n{r.stdout}{r.stderr}')\n"
    "    return r.stdout\n"
    "items = json.loads(run('msbuild', app, '-getItem:KnownWebAssemblySdkPack'))['Items']['KnownWebAssemblySdkPack']\n"
    "sdk = {i['WebAssemblySdkPackVersion'] for i in items if i.get('TargetFramework') == 'net10.0'}\n"
    "lock = json.load(open('src/ArcForges.Web.App/packages.lock.json', encoding='utf-8'))\n"
    "locked = lock['dependencies']['net10.0']['Microsoft.NET.Sdk.WebAssembly.Pack']['resolved']\n"
    "if sdk != {locked}:\n"
    "    sys.exit(f'the Windows .NET SDK resolves Microsoft.NET.Sdk.WebAssembly.Pack {sorted(sdk)} for {app}, but its '\n"
    "             f'packages.lock.json (like hosted CI and WSL) has {locked}: the installed workload set '\n"
    "             f'{run(\"workload\", \"--version\").strip()} pins older WebAssembly manifests. Prerequisite (an install '\n"
    "             'by the user): on Windows, select a workload set whose Mono toolchain manifest matches, e.g. '\n"
    "             '`dotnet workload update --version 10.0.401`, then verify again.')\n"
    "print(f'Microsoft.NET.Sdk.WebAssembly.Pack {locked} as locked')\n"
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
    return Step(step_id, "win", cmd, timeout=timeout, env=ENV, **options)


def wsl(step_id: str, cmd: str, timeout: int, **options) -> Step:
    return Step(step_id, "wsl", cmd, timeout=timeout, env=ENV, **options)


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
    wsl("clone-identity", "set -e; git config remote.origin.url https://github.com/ArcForges/Web.git; "
        "git checkout -q -B arcforges-verify {sha}; git update-ref refs/remotes/origin/main {base}", 60),
    wsl("fresh-artifacts", "rm -rf artifacts", 120),
    wsl("npm-ci", "npm ci --ignore-scripts", 300),
    wsl("npm-audit", "npm audit --audit-level=high", 300),
    wsl("restore", bash_each(CSHARP_PROJECTS, "restore \"$p\" --locked-mode"), 600),
    wsl("format", bash_each(CSHARP_PROJECTS, "format \"$p\" --verify-no-changes --no-restore"), 900),
    wsl("build", bash_each(CSHARP_PROJECTS, "build \"$p\" --no-restore -c Release"), 600),
    wsl("test", bash_each(CSHARP_TEST_PROJECTS, "test \"$p\" --no-build -c Release"), 300),
    wsl("publish-profiles", "set -euo pipefail; "
        "dotnet publish src/ArcForges.Web.App/ArcForges.Web.App.csproj --no-restore -c Release -o artifacts/profiles/app; "
        "dotnet publish src/ArcForges.Web.Operations/ArcForges.Web.Operations.csproj --no-restore -c Release "
        "-o artifacts/profiles/operations", 600),
    wsl("site-twice", f"set -euo pipefail; tool={TOOL}; "
        "dotnet \"$tool\" site build --out artifacts/site --source-ref {sha}; "
        "dotnet \"$tool\" site build --out artifacts/site-repeat --source-ref {sha}; "
        "diff -r artifacts/site artifacts/site-repeat; rm -rf artifacts/site-repeat", 300),
    wsl("check", "set -euo pipefail; npm run check; "
        "test -f artifacts/evidence/licence-boundary.json; test -f artifacts/evidence/source-provenance.json", 600),
    wsl("candidate", CANDIDATE, 600),
    # the former pre-push hook, and quality's secret scan, over the pushed range; actionlint when a workflow changed
    wsl("whitespace", "git diff --check {base} {sha}", 120, lane="scan"),
    wsl("secret-scan", SECRET_SCAN, 600, lane="scan"),
    wsl("actionlint", ACTIONLINT, 300, lane="actionlint", when=workflows_changed),
]
