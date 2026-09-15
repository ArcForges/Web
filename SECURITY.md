# Security policy

This repository is an early Web foundation. Security fixes are applied to current `main`; there is no promise of support for historical preview builds.

Report vulnerabilities through [GitHub private vulnerability reporting](https://github.com/ArcForges/Web/security/advisories/new). Include the affected source/version, reproduction steps, impact and sanitized evidence. Do not disclose a vulnerability in a public issue before maintainers have investigated it. Never send deployment tokens, passwords or personal user data.

Maintainers review private reports and coordinate a fix and disclosure. Routine bugs belong in the public issue tracker. Deployment credentials belong only in the protected GitHub environment, never in browser code. If a credential is exposed, revoke/rotate it at its issuer and update the environment secret.

The static preview has no login, AI proxy or business API. Before adding those capabilities, follow the Cloud authority, session, permissions and quota design. CodeQL, audit and secret scanning are useful checks, not guarantees that an application is secure.
