# Contributing

Work on a branch in a separate worktree. Read [development](docs/development.md) and the relevant product design before implementation. Keep changes bounded and describe observable behavior and acceptance criteria in the PR.

Use English in source and documentation, exact dependency versions, the committed lockfile, and the pinned toolchain. Install the worktree-local hooks with `npm run hooks`. Run `npm run check`, then build and run the browser tests for application/delivery changes. Include any unverified external configuration in the PR description.

Do not commit credentials, generated builds or private account data. Security issues use the [private reporting process](SECURITY.md). Dependency source and license notices must be retained; sibling repositories are consumed only through their published packages.

Contributions to first-party code are provided under the repository's AGPL-3.0-only license. Preserve third-party copyright and license terms. PRs are reviewed before merge; CI and deployment evidence do not replace product acceptance testing.
