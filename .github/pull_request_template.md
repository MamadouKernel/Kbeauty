## What changes

## Checklist
- [ ] No key, token, password or personal data in the diff (the secrets scan also checks)
- [ ] Tests added or updated, and the three suites pass (unit, integration, front)
- [ ] New route: authorisation checked (role **and** ownership), inputs validated, no 5xx on bad input
- [ ] New outside call: timeout, no secret in logs, failure handled without breaking the user's action
- [ ] `docs/PENTEST.md` updated when the attack surface changes
