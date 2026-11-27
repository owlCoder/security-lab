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


# KT2 increment 3 — course/bootstrap/Audit and ACC-04 consumer boundary

Verified **2026-11-15** against published increment 2
`4c4abfc065491e2ef077aebdbaf0438db65961d8`. `git fetch origin` was run first;
merging current main `b50ae3329461b0339a72726218d27a31e7857ced` reported
Already up to date. The delivery continues `feature/kt2-adm08-persistence`.

| Check | Actual local Linux result | Evidence |
|---|---|---|
| Coursework suite, strict PowerShell existing-server launcher | **98/98 passed**, 0 failed, 0 skipped | `administration-postgres.trx`; full PowerShell log |
| PostgreSQL service cases | **25 passed** | AdministrationPostgresTests; deterministic account doubles remain explicit |
| ADM-08 real-provider HTTP cases | **42 passed** | Actual Identity/session, ACC-01 catalog, central Access and PostgreSQL; Audit relay cases use actual central writer/database |
| ACC-04 HTTP consumer cases | **18 passed** | Actual Identity/session/PostgreSQL; ATT and membership are explicit contract doubles |
| ATT transport adapter unit cases | **13 passed** | AttemptProviderAdapterTests; malformed data, receipt, timeout, cancellation and no retry |
| Existing KT1 regression | **65/65 passed**, no skips | `kt1-kt2-03.trx` |
| Shared Access regression | **361/361 passed**, no skips | `access-kt2-03.trx` |
| Whole backend solution Release build | **0 warnings, 0 errors** | `solution-build.log`, .NET SDK 10.0.403 |
| Operator CLI as a separate process | Migrate, successful bootstrap, same-root retry and relay-once passed | `operator-tool.log` |
| Operator DB proof | 1 root, 2 immutable producer events, 1 receipt, 1 central event; unsupported bootstrap event retained | Disposable real Identity/catalog/Audit PostgreSQL DB |
| `-Demonstrate` | **2/2 repeated lifecycle tests passed** | One real ADM lifecycle; one explicitly doubled ATT/membership ACC-04 flow |
| New remote CI / Windows-Docker / browser-Kestrel deployment | **Not confirmed here** | Must use the actual subsequent run; no claim of remote success |

The 98 total is **25 + 42 + 18 + 13**. It is not 98 live demonstration steps,
98 all-real-provider integration cases or the whole-platform test count. The two
lifecycle repetitions do not add distinct cases. PostgreSQL version is 17.12;
PowerShell existing-server launcher is 7.7.0. No user's real accounts/data are used.

The initial expanded run found a correlation-ID inconsistency (79/80 passed):
the response and producer event had different IDs. The fix preserves the existing
central server-generated correlation across both, rather than weakening the test.
Final runtime policy metadata uses the central audit-compatible `v1` format.

The final suite additionally proves operator idempotency/concurrency and rollback,
archived/deleted/unavailable courses with the same previously issued cookie, a
course archive during an operation rolling back its mutation, central delivery
acknowledgement/deduplication after lost receipts, audit outages and explicitly
unmapped producer events. ACC-04 cases cover real-session authentication, server
metadata, ownership/teacher scope, no implicit administrative answer access,
missing/revoked membership, ATT outage, historical state, stale/concurrent writes,
owner changes after central approval, logout and in-flight session revocation.

**Unclosed external boundaries:** the actual ATT read/answer/CAS implementation
is absent on main; ACC-02 persistence is on a separate unmerged branch. The ATT v1
HTTP contract is a consumer proposal, not an accepted external API. Settings,
root-bootstrap and read-success events have no accepted central Audit type and stay
durable locally. Human provider acknowledgements, root recovery agreement, actual
Tapiz evidence, final deployed demonstration and the new CI run remain unconfirmed.
No other team's whole branch, central Access implementation or audit catalogue was
changed to bypass those boundaries. Full solution **build**, not full solution
**test execution**, is claimed.


# KT2 increment 4 — actual HTTPS/Kestrel demonstration

Verified **2026-11-17**. Source base: current main
`29db660d42449335dceff2882adcf011734eaf7f`, after fetching origin. Main includes
merged PR #199 (`b53a9bc`); this delivery includes the unpublished third increment
plus the new live runner. The intended student branch is
`feature/kt2-acc04-adm08-demo`. A new PR is needed; #199 is already merged.

| Check | Actual local Linux result | Evidence |
|---|---|---|
| Whole backend solution Release build, including the live runner | **0 errors, 0 warnings** | `solution-build.log`, .NET SDK 10.0.403 |
| Coursework PostgreSQL/HTTP/adapter NUnit suite on current main | **98/98 passed**, 0 failed, 0 skipped | `administration-postgres.trx` |
| Existing KT1 regression | **65/65 passed**, no skips | `kt1-kt2-04.trx` |
| Shared Access regression | **361/361 passed**, no skips | `access-kt2-04.trx` |
| Actual live HTTPS/Kestrel demonstration | **59/59 checks passed**, including **36 HTTP checks**, 1 TLS negative check and 22 state/transport assertions | `live-administration.json`, live launcher log |
| Real API restart | **1**, same delegate cookie and one stored delegate session; persisted label/version preserved | Live JSON/check list and real PostgreSQL queries |
| Exact-cookie revocation | Previously captured cookie denied both read/write after revoke, without re-login | Live HTTP 404 checks |
| Real provider status checks | Existing blocked-account cookie denied; archived ACC-01 course denied | Live HTTP 401/404 checks |
| Actual central Audit | Supported events acknowledged; 5 delegation/capability records persisted, retry did not duplicate; 3 settings/bootstrap producer events retained without false receipts | Actual operator subprocesses, PostgreSQL Audit queries and JSON checks |
| Normal cleanup | Own API/database/private certificate directory removed | `isolatedStateRemoved=true`, no cleanup errors |
| Partial initialization failure | Expected nonzero result before operator startup; failure report retained; created database/private directory removed | `partial-initialization-cleanup.json`, failure smoke log |
| Remaining fixture databases after cleanup smoke | **0** | Dedicated test-server query |
| Remote CI, Windows/Docker and browser/UI deployment | **Not confirmed in this environment** | Check the new actual PR run and user machine results |

These are distinct evidence categories. The NUnit count remains 98, with the same
25 service + 42 real ADM HTTP + 18 ACC-04 contract-double HTTP + 13 adapter cases.
The live demonstration's 59 checks are not 59 additional NUnit tests or all HTTP
requests. Its 36 HTTP checks use the actual API executable as a separate Kestrel
process in Production, real normal Identity password login/persistent sessions,
real ACC-01/ADM PostgreSQL state and real central Audit. It does not use TestServer
or an invented ATT/membership provider. The root has a normal Student global role;
a separate Teacher account demonstrates that teaching role alone grants no ADM
powers. Only isolated fictional fixture accounts/courses are created.

The PowerShell launcher passed in `-UseExistingServer` mode with PostgreSQL 17.12
and PowerShell 7.7.0. The default Docker launcher and Windows execution must still
be checked on the user's machine. The per-run certificate is pinned to its exact
loopback host/port; the negative case verifies the certificate callback actually
rejected the wrong pin. Proxy use, redirects and automatic cookie handling are
explicitly disabled in this local demo client. No OS certificate trust is changed.
The child receives an explicit fixture environment, not inherited provider secrets.

An initial smoke found an environment-copy problem on Unix systems containing
case-distinct variable names. The runner now copies only named OS/runtime essentials
before adding isolated fixture settings; final runs pass. Failure output persists
only stage/type/status data, never exception messages or raw credential-bearing
responses. The explicit partial-failure smoke uses a temporary repository marker
without an operator DLL, expects FileNotFoundException/nonzero status and verifies
cleanup rather than interpreting the failed demonstration as success.

CI adds one focused live-demo step using the existing solution build; existing jobs
and evidence uploads are preserved. Documentation now describes current startup,
provider limits and individual-defense code locations. No standalone CONTRIBUTION
or instruction file is added.

**KT2 is still partial:** actual ATT read/CAS and persistent ACC-02 integration,
missing central audit type agreements, human neighboring-team acknowledgement and
real Tapiz/PR evidence are not fabricated. The current frontend remains the KT1
simulator. Public/browser deployment and all-platform test execution are not claimed.
