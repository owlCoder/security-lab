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


# KT2 increment 2 — real-session administration API

Verified on **2026-11-15**. Patch base: the student's published first increment,
`f8f551d658cd4edb539eb24e7c20358d6da59a72`, already containing current main
`b50ae3329461b0339a72726218d27a31e7857ced`, plus this diff. The continuation is
intended for the same `feature/kt2-adm08-persistence` branch.

| Check | Actual result | Evidence |
|---|---|---|
| Full `backend/SecurityLab.slnx` Release build | Passed; **0 errors, 0 warnings** | `solution-build-final.log`, .NET SDK 10.0.403 |
| Coursework PostgreSQL/HTTP suite | **52 total, 52 passed, 0 failed, 0 skipped** | `administration-postgres.trx` |
| Database/service cases within that suite | **25 passed** | `AdministrationPostgresTests` |
| Real-session HTTP cases within that suite | **27 passed** | `AdministrationHttpTests` |
| Existing KT1 suite | **65/65 passed**, no skips | `kt1-kt2-02.trx` |
| Shared `Platform.Access.Tests` | **361/361 passed**, no skips | `access-kt2-02.trx` |
| PowerShell launcher, existing-server mode with `-Demonstrate` | Passed; requires both integration fixtures | PowerShell 7.7.0; full suite plus one repeated lifecycle case |
| Operator migration CLI | Passed on a separate disposable PostgreSQL database; schema version count **1** | `operator-test.log` |
| Git patch/whitespace checks | Passed | `git diff --cached --check`; clean-base apply verification |
| New remote CI, Windows/Docker run, browser/Kestrel deployment | Not confirmed for this increment | Confirm from actual user/remote runs |

The 52 cases are 25 database/service cases and 27 HTTP integration cases, not a
claim of 52 distinct interactive demonstration steps. `-Demonstrate` repeats the
single full lifecycle case in a new fixture after the suite; it is not counted as
another distinct case. HTTP integration uses ASP.NET TestServer in Production
configuration by default, with actual Identity password verification, persistent
sessions/current account status, central Access and PostgreSQL. A Development
case additionally proves that a valid synthetic KT1 cookie cannot authenticate
the new API. Credentials and data are disposable fictional test fixtures.

The demonstrated lifecycle signs root and delegate in through `/api/auth/login`,
creates limited authority, reads/updates a label, rejects a stale version, revokes
the grant, then denies both read and write using the exact cookie captured before
the delegation, without signing in again. The database confirms one delegate
session and the expected persisted version/audit changes.

Additional HTTP cases cover current blocked/archived account status, logout,
expiry, MFA-pending sessions, forged headers, capability escalation, missing and
inactive grantees, origin/header checks, an unknown-length oversized request,
audit-failure rollback, dependency outages, server-generated correlation and
session revocation during an in-flight operation. That final race rolls back both
the attempted mutation and its ALLOW event, then records one SESSION_INVALIDATED
DENY event when producer storage is available.

The body-size regression initially exposed a 400 response from DTO validation
instead of the intended 413 for an unknown-length request. The final implementation
bounds the body before binding, after central authorization; the regression and
full suite now pass. Test expectations were not weakened.

A full solution build was run; a full solution **test run** and frontend/browser
tests are not claimed. The remaining course/ATT/membership and administrative
outbox-delivery boundaries in `KT2.md` are still open. Published Identity interface
consumption is demonstrated technically; it is not an invented neighboring-team
review acknowledgement.
