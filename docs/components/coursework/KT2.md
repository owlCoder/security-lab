# KT2 — ACC-04 and ADM-08

This is an incremental KT2 implementation, starting with criterion 1. It does not
certify the complete KT2 rubric. The assigned requirements remain ACC-04 (J) and
ADM-08 (N). No ACC-08 implementation belongs to this contribution.

Specification checked: `docs/spec/PROJECT_SPECIFICATION_OIB.pdf`, sections 2,
2.1, 3.1, 4.2 and 4.12. SHA-256:
`e74f73cf4fdfd25fe469b799488274c826325fd09c3498757e0aa37fd097676c`.

## 1. Implemented component — in progress

| Use case | Existing implementation | This increment | Still required before full closure |
|---|---|---|---|
| ACC-04: own attempt read and foreign-ID rejection | Server-side ownership, active account, current membership and exact authorized snapshot | Existing behavior retained | Real ATT adapter and real-session/membership composition |
| ACC-04: own draft update | Owner/student/state checks, validation, atomic simulator CAS, historical answers protected | Existing behavior retained | ATT persistence and its atomic snapshot/version contract |
| ADM-08: scoped administrative view | Explicit per-course grant, separate from teaching roles | PostgreSQL current-authority read; new operation sessions see committed grants | Trusted course directory and agreed root provisioning/recovery |
| ADM-08: limited delegation | Root-only creation; view/settings allowlist; no redelegation or teaching rights | Persistent grant with one active grant per account/course | Agreed operator root provisioning/recovery; account provider now uses Identity |
| ADM-08: settings update | Validation and expected version | Course-row locking and compare/version update across connections | The stored label is administrative metadata; it does not rename the ACC-01 catalog course |
| ADM-08: immediate revocation | Active authority rechecked, same-cookie KT1 regression on its integration branch | Previously captured database authority cannot read/write after a committed revocation; revoked rows retained | External course-provider review; actual-session HTTP lifecycle is covered in this increment |
| Change audit | KT1 metadata-only in-memory log | Matching producer event and business change commit together; missing/failed audit rolls back | Delivery/acknowledgement with Platform.Audit under an agreed event contract |

### Storage and transaction boundary

`backend/Platform.Api/Modules/Administration/Persistence/` contains:

- `AdministrationSchema`: explicit, transactional, serialized v1 migration and runtime-role grants.
- `PostgresAdministrationSession`: one connection/transaction per operation, persistent grants and settings, read binding to the exact grant, per-course write lock, version checks and matching audit enforcement.
- `PersistentAdministrationService`: invokes the existing application logic and returns a result only after commit. Storage/audit failure does not report success.
- `PersistentAdministrationPolicy`: a singleton-safe policy that creates a fresh database session per evaluation and calls the existing scoped administrative rules. It consumes the shared `IAccessDecision` architecture; there is no second decision engine.

Only schema `coursework_admin` is owned. `course_settings` stores an administrative
label for a trusted course ID; `grants` stores technical permissions. Neither is a
replacement for the course catalog, course membership, teacher assignment, ATT
answers or grade history. No other team's tables are changed.

The migration deliberately creates no accounts, courses in the shared catalog,
or root grants. Synthetic test roots are created only in disposable test databases.
There is no HTTP root-provisioning endpoint. A schema-owner operator must use
confirmed course/account IDs under the future agreed provisioning process.

The runtime permission helper grants reads, limited updates and non-root grant
inserts, plus INSERT-only producer audit. It grants no schema creation, root
columns, deletion or audit rewrite. Use a dedicated role with no pre-existing
owner/superuser privileges. Credentials are supplied externally.

### Durable producer audit is not delivered central audit

`outbox` is a transactionally persisted producer record, with actor, action,
target/course, outcome, reason, policy/version, time and server correlation ID.
It contains no display labels, answers, cookies, passwords or raw request bodies.
An administrative mutation cannot commit without its corresponding event. This
increment does **not** implement a relay, central delivery acknowledgement,
retention or a new audit-reading API. Those remain integration work with the audit
team; no central-delivery claim is made.

### Increment 2: real-session administration API

`Program.cs` now wires the PostgreSQL adapter when
`CourseworkAdministration:Enabled=true`. Four routes exist under
`/api/course-administration/{courseId}`:

| HTTP operation | Application action | Required current capability |
|---|---|---|
| GET course administration | `course.admin.read` | ViewAdministration |
| PUT `settings` | `course.settings.update` | ManageSettings |
| POST `delegations` | `course.delegation.create` | Root with ManageDelegations |
| POST `delegations/{grantId}/revoke` | `course.delegation.revoke` | Root with ManageDelegations |

All routes carry one explicit central Access requirement. The module decorates
request identity only for its own endpoint metadata, forwarding all other routes
to the existing provider chain. Only `SecurityLab.Session`, validated through
Identity's `ISessionManager`, supplies the caller. An active account is re-read
through `IAccountStatusReader`; roles, headers, `Http.User` and the synthetic KT1
cookie do not create authority.

Database/account calls are asynchronous and cancellable. The default async port
adapters preserve the existing in-memory KT1 consumers; no synchronous wait on
Identity tasks is used. Persistent policies open their own short-lived sessions,
so singleton policies do not capture scoped EF contexts.

The API revalidates the original Identity session in a fresh scope immediately
before committing an operation. If it has been revoked/expired or its account is
inactive, the staged change and success event are rolled back. When producer storage
is available, a separate `DENY / SESSION_INVALIDATED` event records that rejection. Administrative
revocation is serialized with course mutations in the database. Identity and
administration are separate provider transactions: there is **no distributed
transaction** guaranteeing that an Identity change concurrent with the final
commit has the same atomic boundary. Every following request re-reads state.

Unauthenticated requests receive 401. A narrow response formatter maps central
403 denials to the module's uniform 404, after the central engine has denied the
request; it never admits/retries a denial. Identity-provider outage returns a
uniform 503 before resource lookup. Denied/missing resources during an
administration-store outage have the same public 404. Endpoint responses use
`no-store`. Mutations require `X-Coursework-Request: 1`; a supplied Origin must
match the request's scheme/host. Bodies are bounded at 16 KiB.

The existing `/api/kt1` and React screen still demonstrate the explicitly enabled
KT1 simulator. They are separate from this real-session API. No ACC-04 ATT or
membership adapter is claimed by this increment.

### Startup after operator provisioning

Required runtime configuration: `ConnectionStrings:Identity` and
`ConnectionStrings:CourseworkAdministration`; configure `ConnectionStrings:Audit`
for Identity's durable login/logout audit. These may be separate PostgreSQL
connections. Keep credentials in environment variables or existing user secrets.
The ordinary platform startup remains unchanged when the feature flag is absent.

Schema migration is an explicit operator command, using a schema-owner connection
in `COURSEWORK_MIGRATION_CONNECTION`:

```powershell
dotnet run --project backend/Platform.Coursework.AdminTool -- migrate
dotnet run --project backend/Platform.Coursework.AdminTool -- grant-runtime YOUR_EXISTING_RUNTIME_ROLE
```

The tool grants only the narrow privileges already tested in increment 1. It
creates no login role/password, identity account, shared course or root grant.
The root setup/recovery and import of confirmed course IDs remain an operator
agreement with the course provider. With no configured roots, API access is denied.

For local HTTPS after configuration/provisioning, use dedicated local/test databases
(the existing Development startup also owns its development seed):

```powershell
dotnet dev-certs https --trust
dotnet run --project backend/Platform.Api --launch-profile AdministrationKt2
```

This listens on `https://localhost:7188`. Use the normal `/api/auth/login` with an
existing provisioned account, then the routes above. The cookie is Secure,
HttpOnly and SameSite=Strict. A reverse proxy must supply the correct trusted
scheme/host for HTTPS and same-origin checks; deployment forwarding is not added
by this increment. Production enables the same module via configuration, without
the Development-only launch profile.

Current architecture and DFD:
`diagrams/adm08-kt2-architecture.mmd/.svg` and `adm08-kt2-dfd.mmd/.svg`.
The older diagrams retain their KT1 simulator scope.

## Repeatable check for this increment

Prerequisites: .NET 10, Docker Desktop running, PowerShell. From the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-coursework-postgres.ps1
```

This creates a uniquely named PostgreSQL 17 test container on a random loopback
port with an ephemeral secret, creates a separate database for each test, and
cleans up its own container. It does not use the user's Identity, Tapiz or course
database. The TRX report is written under `TestResults/coursework-postgres-<run>/`.
The script fails if any case is failed/skipped, and requires discovery of both
the PostgreSQL and real-session HTTP fixtures. For the visible HTTP lifecycle:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-coursework-postgres.ps1 -Demonstrate
```

The full suite runs first, followed by one fresh, detailed lifecycle demonstration:
real password login for separate clients, delegation, persisted settings change,
stale-version rejection, revocation and denial with the exact previously issued
cookie. The HTTP host is ASP.NET TestServer with real Identity/session, Audit,
shared Access and PostgreSQL services. It is not a claim of browser/Kestrel or
production deployment verification. Accounts, roots and courses are fictional,
isolated test fixtures; no production credentials/data are used.

Alternatively, on an explicitly dedicated test server, set
`COURSEWORK_TEST_POSTGRES` and use `-UseExistingServer`. The test role requires
CREATE DATABASE and CREATE ROLE on that disposable server; those are **test
bootstrap permissions**, not runtime privileges. Never point this test fixture
at a production server. The runtime-role test proves the application can work
with the narrower grants in `runtime-permissions.sql`.

A normal solution test run without that environment variable explicitly skips
the PostgreSQL fixture. It must not be reported as proof that database tests ran.
The backend workflow has a separate ADM-08 PostgreSQL step so its database cases
are required in CI as well. Existing jobs and result uploads are preserved. The
new remote run has not yet been observed.

## Remaining rubric items

| Criterion | Status | Completion evidence still needed |
|---|---|---|
| 1. Assigned functionality | In progress | Complete actual-provider ACC-04 flow, trusted course/root provisioning and remaining ADM-08 product integration |
| 2. Module integration | Partial | ADM-08 uses actual Identity/session and Access providers; course/membership, ATT and administrative outbox delivery/review remain |
| 3. Tests/security | Started | PostgreSQL and real-session HTTP suites/CI step exist; confirm the actual remote run and complete ACC-04 integration coverage |
| 4. Startup/documentation | Started | ADM-08 API startup, operator tool, architecture and DFD are updated; finalize whole-product deployment/ACC-04 flow |
| 5. Individual contribution | Started | First increment is visible in commit `f8f551d`; this increment still needs the student's actual commit/PR and explanation |

Validation for this source increment is recorded in `KT2-VALIDATION.md`. Remote CI,
actual Tapiz state and external contract acceptance are not inferred from local tests.


The first increment's published Git trace is
[commit f8f551d](https://github.com/owlCoder/security-lab/commit/f8f551d658cd4edb539eb24e7c20358d6da59a72).
Record this continuation's actual commit and PR after publishing it; no future
Git history or completed Tapiz card is fabricated by this document.
