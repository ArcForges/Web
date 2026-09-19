// SPDX-License-Identifier: AGPL-3.0-only
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import path from "node:path";
import { test } from "node:test";
import { validateRecord } from "../../tooling/provenance.ts";

const owner = path.resolve(import.meta.dirname, "../..");
function record(name: string) {
  const value = JSON.parse(
    readFileSync(path.join(owner, "eng/provenance/records/canonical-agpl-legal-r1.json"), "utf8"),
  );
  value.targets[0].path = name;
  return value;
}
test("legal paths retain supported complete legal filenames and reject source masquerading as notices", () => {
  for (const name of [
    "LICENSE",
    "third-party/protobuf-es-LICENSE.txt",
    "third-party/rolldown-companions.LICENSE.txt",
    "NOTICE.md",
    "third-party/component_v2.COPYING",
  ])
    validateRecord(record(name));
  for (const name of [
    "LICENSE.ts",
    "a--LICENSE.txt",
    "file..NOTICE.md",
    "third-party/implementation.js",
    "copying.exe",
  ])
    assert.throws(() => validateRecord(record(name)), /Legal document target/u);
});
test("long delimiter sequences cannot exhaust licence-path checking", () => {
  const script = `import assert from 'node:assert/strict';
    import {validateRecord} from './tooling/provenance.ts';
    import {readFileSync} from 'node:fs';
    const r=JSON.parse(readFileSync('eng/provenance/records/canonical-agpl-legal-r1.json','utf8'));
    for(const separator of ['--', '..', '.-']) {
      r.targets[0].path=separator.repeat(25000)+'INVALID.txt';
      assert.throws(()=>validateRecord(r));
    }`;
  execFileSync(process.execPath, ["--input-type=module", "-e", script], {
    cwd: owner,
    timeout: 3000,
    windowsHide: true,
  });
});
