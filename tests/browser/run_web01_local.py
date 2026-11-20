#!/usr/bin/env python3
"""Explicitly opt in to the real WEB-01 local browser journey."""

# SPDX-License-Identifier: AGPL-3.0-only

import argparse
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import re
import runpy
import signal
import subprocess
import tempfile
import time
from urllib.parse import urlsplit


CI_MARKERS = ("CI", "GITHUB_ACTIONS", "TF_BUILD", "BUILD_BUILDID", "GITLAB_CI")
NODE_SOURCE = "215125cffbab6737a71a540006dc6b2e56d4ca1a"
POSTGRES_IMAGE = "postgres:18.7-alpine3.24@sha256:b6972e1115a4ff8eba5d7f7d19184926d1e16d7d1136ae1e1a7ebf286da0f015"
WEB = Path(__file__).resolve().parents[2]
PROJECT = WEB / "tests/browser/ArcForges.Web.Browser.Tests/ArcForges.Web.Browser.Tests.csproj"
PEER = WEB / "tests/browser/fixtures/web01_native_peer_test.go"
HOOK = "\tf.server.StartTLS()\n"


def digest(path):
    with path.open("rb") as artifact:
        return hashlib.file_digest(artifact, "sha256").hexdigest()


def git(root, *args):
    return subprocess.check_output(["git", "-C", root, *args], text=True, timeout=15).strip()


def check_checkout(root, revision):
    if git(root, "rev-parse", "HEAD") != revision or git(root, "status", "--porcelain"):
        raise SystemExit("The selected Cloud and Node inputs must be independently fetched, clean exact checkouts.")


def prepare_overlay(cloud, exchange, *, with_static=True):
    original = cloud / "center/cmd/arcforges-center/host03_selfhost_integration_test.go"
    target = original.with_name(PEER.name)
    if target.exists():
        raise SystemExit("The WEB-01 peer would overwrite a tracked or pre-existing Cloud file.")
    replaces = {str(target): str(PEER)}
    if with_static:
        body = original.read_text(encoding="utf-8")
        if body.count(HOOK) != 1:
            raise SystemExit("The admitted HOST-03 test-only listener seam changed.")
        private = exchange / original.name
        private.write_text(
            body.replace(HOOK, "\tf.server.Config.Handler = web01NativeStaticHandler(t, f, f.server.Config.Handler)\n" + HOOK),
            encoding="utf-8",
        )
        replaces[str(original)] = str(private)
    overlay = exchange / "cloud-test-overlay.json"
    overlay.write_text(json.dumps({"Replace": replaces}), encoding="utf-8")
    return overlay


def child_environment():
    deployment_prefixes = ("ARCFORGES_", "AWS_", "CLOUDFLARE_", "GH_", "GITHUB_", "NEON_", "PADDLE_")
    return {
        name: value for name, value in os.environ.items()
        if not name.upper().startswith(deployment_prefixes) and not name.upper().endswith("_PROXY")
    }


@contextmanager
def postgres(cloud):
    selected = os.environ.get("ARCFORGES_TEST_POSTGRES_URL")
    if selected:
        parsed = urlsplit(selected)
        if parsed.scheme not in ("postgres", "postgresql") or parsed.hostname not in ("127.0.0.1", "::1"):
            raise SystemExit("The supervised disposable PostgreSQL instance must have a literal loopback URL.")
        yield selected
        return
    if os.name == "nt":
        raise SystemExit("Windows requires the existing supervised disposable native PostgreSQL fixture URL.")
    center = runpy.run_path(str(cloud / "eng/center.py"))
    if center["POSTGRES_IMAGE"] != POSTGRES_IMAGE:
        raise SystemExit("The expected exact PostgreSQL image pin changed.")
    server = center["Postgres"]("web01")
    try:
        with server:
            if not server.address.startswith("127.0.0.1:"):
                raise SystemExit("The owned PostgreSQL fixture did not bind to loopback.")
            yield server.url()
    finally:
        remaining = subprocess.run(
            ["docker", "container", "ls", "--all", "--filter", "name=^/" + server.name + "$", "--format", "{{.Names}}"],
            capture_output=True, text=True, check=True, timeout=20,
        )
        if remaining.stdout.strip():
            # The random name was allocated by this invocation before any start.
            server.__exit__(None, None, None)
            remaining = subprocess.run(
                ["docker", "container", "ls", "--all", "--filter", "name=^/" + server.name + "$", "--format", "{{.Names}}"],
                capture_output=True, text=True, check=True, timeout=20,
            )
        if remaining.stdout.strip():
            raise SystemExit("The owned PostgreSQL fixture could not be confirmed clean.")


def start(command, root, environment, log):
    with log.open("xb") as output:
        return subprocess.Popen(
            command, cwd=root, env=environment, stdin=subprocess.DEVNULL, stdout=output, stderr=subprocess.STDOUT,
            creationflags=subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0,
            start_new_session=os.name != "nt",
        )


def stop(process, *, allow_wait=0):
    if process.poll() is not None:
        return
    if allow_wait:
        try:
            return process.wait(timeout=allow_wait)
        except subprocess.TimeoutExpired:
            pass
    if os.name == "nt":
        subprocess.run(
            ["taskkill", "/pid", str(process.pid), "/T", "/F"],
            stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=15, check=False,
        )
    else:
        try:
            os.killpg(process.pid, signal.SIGTERM)
        except ProcessLookupError:
            pass
    process.wait(timeout=15)
    if allow_wait:
        raise SystemExit("The Go peer needed termination before cooperative cleanup could be confirmed.")


def ready(path, process, duration):
    deadline = time.monotonic() + duration
    while time.monotonic() < deadline:
        if path.is_file():
            try:
                return json.loads(path.read_bytes())
            except json.JSONDecodeError:
                pass
        if process.poll() is not None:
            raise SystemExit("A local fixture exited before its ready receipt; inspect the private logs.")
        time.sleep(0.15)
    raise SystemExit("A local fixture did not become ready before its deadline; inspect the private logs.")


def wait_browser(browser, peer):
    deadline = time.monotonic() + 360
    while time.monotonic() < deadline:
        browser_result = browser.poll()
        if browser_result is not None:
            return browser_result
        peer_result = peer.poll()
        if peer_result is not None and peer_result != 0:
            # A failed Center/Node peer cannot finish the paired journey.
            # Allow the test reporter a brief drain without waiting the full
            # browser deadline while the peer has already gone away.
            try:
                return browser.wait(timeout=5)
            except subprocess.TimeoutExpired:
                raise SystemExit("The real peer ended before the browser journey; inspect browser-phases.log and center-node.log.") from None
        time.sleep(0.2)
    raise SystemExit("The local Chrome journey exceeded its deadline; inspect browser-phases.log.")


def native_environment(args, environment, exchange, database, node, node_digest):
    return environment | {
        "ARCFORGES_WEB01_PEER_REQUIRED": "1", "ARCFORGES_WEB01_EXCHANGE": str(exchange),
        "ARCFORGES_TEST_POSTGRES_URL": database, "ARCFORGES_TEST_POSTGRES_REQUIRED": "1",
        "ARCFORGES_HOST03_NODE": str(node), "ARCFORGES_HOST03_NODE_SHA256": node_digest,
        "ARCFORGES_HOST03_NODE_SOURCE": NODE_SOURCE, "SSL_CERT_FILE": str(args.tls_ca),
        "ARCFORGES_HOST03_TLS_CERT": str(args.tls_cert), "ARCFORGES_HOST03_TLS_KEY": str(args.tls_key),
    }


def native_api(args, environment, exchange, evidence, node, node_digest):
    overlay = prepare_overlay(args.cloud, exchange, with_static=False)
    with postgres(args.cloud) as database:
        go_env = native_environment(args, environment, exchange, database, node, node_digest)
        race = ["-race"] if os.name != "nt" else []
        peer = start(
            ["go", "test", *race, "-overlay", overlay, "-count=1", "-timeout=4m", "-run", "^TestWeb01NativeAPI$",
             "-v", "./cmd/arcforges-center"],
            args.cloud / "center", go_env, evidence / "center-node.log",
        )
        try:
            if peer.wait(timeout=260) != 0:
                raise SystemExit("The real native isolated Dashboard API witness failed; inspect center-node.log.")
        finally:
            stop(peer)
        receipt = json.loads((exchange / "native-receipt.json").read_bytes())
        if (receipt.get("browser") is not False or receipt.get("nodeSha256") != node_digest
                or receipt.get("realScopedEmptyBeforeAndAfterReconnect") is not True
                or receipt.get("realScopedOfflineRefusal") is not True):
            raise SystemExit("The native API receipt does not bind the actual scoped sentinel cases.")
        return receipt


def main():
    if os.environ.get("ARCFORGES_LOCAL_BROWSER") != "1" or any(os.environ.get(name) for name in CI_MARKERS):
        raise SystemExit("WEB-01 browser execution is explicit local opt-in only.")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cloud", type=Path, required=True)
    parser.add_argument("--cloud-revision", required=True)
    parser.add_argument("--node-source", type=Path, required=True)
    parser.add_argument("--node-install", type=Path, required=True)
    parser.add_argument("--api-only", action="store_true", help="qualify the real native API and Node without Chromium or static files")
    parser.add_argument("--bundle", type=Path)
    parser.add_argument("--bundle-sha256")
    parser.add_argument("--tls-cert", type=Path, required=True)
    parser.add_argument("--tls-key", type=Path, required=True)
    parser.add_argument("--tls-ca", type=Path, required=True)
    parser.add_argument("--chromium", type=Path)
    parser.add_argument("--evidence", type=Path, required=True, help="new private receipt directory")
    args = parser.parse_args()
    inputs = ["cloud", "node_source", "node_install", "tls_cert", "tls_key", "tls_ca"]
    if not args.api_only:
        if args.bundle is None or args.chromium is None or not re.fullmatch(r"[0-9a-f]{64}", args.bundle_sha256 or ""):
            parser.error("an existing Chromium and exact static archive with its lowercase digest are required for browser mode")
        inputs += ["bundle", "chromium"]
    for name in inputs:
        setattr(args, name, getattr(args, name).resolve(strict=True))
    if not re.fullmatch(r"[0-9a-f]{40}", args.cloud_revision):
        raise SystemExit("An exact lowercase Cloud source revision is required.")
    check_checkout(args.cloud, args.cloud_revision)
    check_checkout(args.node_source, NODE_SOURCE)
    web_revision = git(WEB, "rev-parse", "HEAD")
    web_dirty = bool(git(WEB, "status", "--porcelain"))
    if not args.api_only and digest(args.bundle) != args.bundle_sha256:
        raise SystemExit("The supplied static archive bytes do not match their exact digest.")
    rid = "win-x64" if os.name == "nt" else "linux-x64"
    verify = runpy.run_path(str(args.node_source / "packaging/mxc/verify_install.py"))["verify"]
    verify(args.node_install, rid, NODE_SOURCE)
    node = args.node_install / ("arcforges.exe" if os.name == "nt" else "arcforges")
    node_digest = digest(node)

    os.umask(0o077)
    evidence = args.evidence.resolve()
    evidence.mkdir(mode=0o700)
    environment = child_environment()
    environment.update({"GOTOOLCHAIN": "local", "GOWORK": "off", "GOFLAGS": "-mod=readonly", "GOTELEMETRY": "off"})
    if args.api_only:
        with tempfile.TemporaryDirectory(prefix=".exchange-", dir=evidence) as private:
            receipt = native_api(args, environment, Path(private), evidence, node, node_digest)
    else:
        receipt = run_browser(args, environment, evidence, node, node_digest)
    check_checkout(args.cloud, args.cloud_revision)
    check_checkout(args.node_source, NODE_SOURCE)
    if git(WEB, "rev-parse", "HEAD") != web_revision or (not args.api_only and digest(args.bundle) != args.bundle_sha256):
        raise SystemExit("The selected Web source head or static archive changed during the local journey.")
    receipt.update({"cloudRevision": args.cloud_revision, "cloudTree": git(args.cloud, "rev-parse", "HEAD^{tree}"),
                    "webRevision": web_revision, "webDirty": web_dirty,
                    "centerNodeLogSha256": digest(evidence / "center-node.log")})
    with (evidence / "receipt.json").open("x", encoding="utf-8") as output:
        output.write(json.dumps(receipt, indent=2) + "\n")


def run_browser(args, environment, evidence, node, node_digest):
    with tempfile.TemporaryDirectory(prefix=".exchange-", dir=evidence) as private:
        exchange = Path(private)
        overlay = prepare_overlay(args.cloud, exchange)
        static_ready = exchange / "static-ready.json"
        static = start(
            ["node", WEB / "tests/browser/dashboard-origin.ts", "--bundle", args.bundle,
             "--sha256", args.bundle_sha256, "--ready-file", static_ready],
            WEB, environment, evidence / "static.log",
        )
        try:
            static_origin = ready(static_ready, static, 30)["origin"]
            with postgres(args.cloud) as database:
                go_env = native_environment(args, environment, exchange, database, node, node_digest)
                go_env["ARCFORGES_WEB01_STATIC_ORIGIN"] = static_origin
                peer = start(
                    ["go", "test", "-overlay", overlay, "-count=1", "-timeout=8m", "-run", "^TestWeb01DashboardPeer$",
                     "-v", "./cmd/arcforges-center"],
                    args.cloud / "center", go_env, evidence / "center-node.log",
                )
                try:
                    origin = ready(exchange / "browser.json", peer, 110)["origin"]
                    browser_env = environment | {
                        "ARCFORGES_LOCAL_BROWSER": "1", "ARCFORGES_BROWSER_BASE_URL": origin,
                        "ARCFORGES_CHROMIUM_PATH": str(args.chromium), "ARCFORGES_WEB01_EXCHANGE": str(exchange),
                        "ARCFORGES_WEB01_PHASE_LOG": str(evidence / "browser-phases.log"),
                    }
                    browser = start(
                        ["dotnet", "test", "--project", PROJECT, "-c", "Release", "--no-build",
                         "--filter-class", "ArcForges.Web.Browser.Tests.LocalDashboardBrowserTests",
                         "--minimum-expected-tests", "1", "--fail-skips", "on"],
                        WEB, browser_env, evidence / "browser.log",
                    )
                    try:
                        if wait_browser(browser, peer) != 0:
                            raise SystemExit("The local Chromium dashboard journey failed; inspect browser.log.")
                    finally:
                        stop(browser)
                    if peer.wait(timeout=55) != 0:
                        raise SystemExit("The real Go/PostgreSQL and native Node peer failed; inspect center-node.log.")
                    receipt = json.loads((exchange / "native-receipt.json").read_bytes())
                    browser_observed = json.loads((exchange / "browser-observed.json").read_bytes())
                    if (receipt.get("deviceRevoked") is not True or receipt.get("nodeSha256") != node_digest
                            or receipt.get("deviceId") != browser_observed.get("deviceId")):
                        raise SystemExit("The native receipt did not bind the qualified revocation and Node bytes.")
                finally:
                    if peer.poll() is None:
                        (exchange / "abort").touch(exist_ok=False)
                        stop(peer, allow_wait=55)
        finally:
            stop(static)
    receipt.update({"staticBundleSha256": args.bundle_sha256,
                    "browser": browser_observed, "browserExecutableSha256": digest(args.chromium),
                    "browserPhasesSha256": digest(evidence / "browser-phases.log"),
                    "browserLogSha256": digest(evidence / "browser.log")})
    return receipt


if __name__ == "__main__":
    main()
