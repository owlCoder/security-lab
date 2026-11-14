# KT2 increment 1 — local verification

Verified on 2026-11-14. Source base: current `origin/main`, commit
`5bca4e41187a3f4354bfdc342fa4f6479637e48c`, plus the ADM-08 persistence diff.
The source branch is `feature/kt2-acc04-adm08`. Nothing in this record certifies a
new remote PR, external review, Tapiz status or a GitHub Actions result.

| Check | Actual result | Evidence |
|---|---|---|
| Build the changed backend/test project graph | Passed, .NET SDK 10.0.403 | PostgreSQL run includes restore/build |
| ADM-08 real PostgreSQL integration tests | **25 total, 25 passed, 0 failed, 0 skipped** | `administration-postgres.trx` |
| Existing KT1 tests on this main revision | **65 total, 65 passed, 0 failed, 0 skipped** | `kt1-main-regression.trx` |
| PowerShell test launcher, existing-server mode | Passed, PowerShell 7.7.0 | `test-coursework-postgres.ps1 -UseExistingServer` |
| Database engine | PostgreSQL **17.12**, UTF-8, ephemeral local cluster | Each test created/dropped its own database |
| Git whitespace/conflict-marker check | Passed | `git diff --cached --check` |
| Full solution, frontend and new live HTTP demonstration | Not rerun for this increment | No claim of a full platform demonstration |
| Docker launcher on Windows | Not executed in this environment | Run the packaged PowerShell script on the user's machine |
| New remote CI | **Pending** | Workflow step configured; actual run must be checked after a PR |

These 25 cases are **database/service tests**, not 25 live HTTP scenarios. The
65-test KT1 count belongs to the main revision above. The earlier 82-test result
belonged to the separate ACC-05 integration branch including PRs #194/#195; counts
from different branches are not combined or reused as current evidence. Those
integration changes are not copied into this KT2 patch.

The PostgreSQL suite verifies:

- Repeatable schema migration while preserving another schema.
- Delegation, settings update and revocation across new connections/service instances.
- Denial of a previously captured grant after revocation commits.
- Concurrent same-version writes: exactly one update and one successful change event.
- Duplicate active grant rejection and preservation of revoked grant history.
- Database-injected audit INSERT failures: both settings and grant creation roll back.
- Commit rejection for a missing/mismatched producer audit event.
- Input validation, Unicode and SQL punctuation treated as bound data.
- Capability allowlist, no redelegation/root escalation, cross-course and missing-course denials.
- Current-account block/outage behavior through an explicit test double.
- Administrative permissions do not create teaching membership or answer access.
- Metadata-only audit, including an anonymous denial.
- Global Access denial cannot mutate persistent state.
- A restricted database LOGIN can perform the application flow but cannot create
  tables, modify root/capability columns, delete or truncate producer audit.
- Cancellation before commit rolls back already staged data and audit.

Identity, membership and ATT in these tests remain explicit deterministic doubles.
Only the administrative database and shared Access decision engine are real
integrations here. Production endpoint/session composition, ATT persistence,
trusted course provisioning and central audit delivery remain open in `KT2.md`.
