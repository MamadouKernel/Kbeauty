# Security policy

## Reporting a vulnerability

**Do not open a public issue** for a security problem. Use GitHub's private channel instead:
**Security tab → "Report a vulnerability"** (private vulnerability reporting). Describe what you found, how to reproduce it and what
it could allow; you will get an answer within five working days.

Please do not test against other people's accounts or data, and do not run denial-of-service or automated scans against a
production site without written agreement.

## What is in scope
The API (`src/backend`), the web app (`src/frontend`), the container and compose configuration, and the CI. Third-party services
(WiniPayer, Zavu, Meta, Google) are out of scope: report those to their owners.

## How security is handled here
- **No secret in the repository**: keys, tokens and passwords live in `.env` or a secrets directory, never in Git. A pre-commit hook
  and CI scan the files and the whole history (gitleaks); GitHub secret scanning and push protection are switched on.
- **Reviews of the attack surface** are written down in [docs/PENTEST.md](docs/PENTEST.md) and the standard checklist in
  [docs/ASVS-L2.md](docs/ASVS-L2.md). They are internal reviews, not an independent audit.
- **Dependencies, images and actions** are updated by Dependabot, audited on every build, and scanned (Trivy for images, CodeQL for code).
- **Deployment hardening** and the production checklist: [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md), [docs/PRODUCTION-CHECKLIST.md](docs/PRODUCTION-CHECKLIST.md).

## Supported versions
Only the latest release on `main` (and the `v1.0.1` branch until it is merged) receives security fixes.
