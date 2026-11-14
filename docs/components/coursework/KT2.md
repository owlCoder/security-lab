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
| ADM-08: scoped administrative view | Explicit per-course grant, separate from teaching roles | PostgreSQL current-authority read; new operation sessions see committed grants | Production API composition and trusted course directory |
| ADM-08: limited delegation | Root-only creation; view/settings allowlist; no redelegation or teaching rights | Persistent grant with one active grant per account/course | Agreed operator root provisioning/recovery and real account provider |
| ADM-08: settings update | Validation and expected version | Course-row locking and compare/version update across connections | The stored label is administrative metadata; it does not rename the ACC-01 catalog course |
| ADM-08: immediate revocation | Active authority rechecked, same-cookie KT1 regression on its integration branch | Previously captured database authority cannot read/write after a committed revocation; revoked rows retained | Real-session HTTP demonstration using the production composition |
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

### Production wiring remains open

`Program.cs`, the existing routes and the React demonstration are not switched to
this database adapter yet. Setting `COURSEWORK_TEST_POSTGRES` does not enable a
production feature. The existing `/api/kt1` and React screen remain synthetic,
Development-only demonstrations. Tests use explicit Identity/ATT doubles and the
real PostgreSQL adapter/shared Access engine, not a claimed live platform login.

Before wiring a production API: consume validated Identity sessions/current
account state, the actual membership provider, and the ATT snapshot/CAS contract;
define account-block/revocation consistency across provider transactions. Do not
use a synchronous wait on asynchronous Identity calls. Root provisioning and
course existence/archive behavior require the course provider's agreement.

## Repeatable check for this increment

Prerequisites: .NET 10, Docker Desktop running, PowerShell. From the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-coursework-postgres.ps1
```

This creates a uniquely named PostgreSQL 17 test container on a random loopback
port with an ephemeral secret, creates a separate database for each test, and
cleans up its own container. It does not use the user's Identity, Tapiz or course
database. The TRX report is written under `TestResults/coursework-postgres-<run>/`.
The script fails if any database case is failed, skipped or absent.

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
| 1. Assigned functionality | In progress | Complete actual-provider use flow for ACC-04 and production ADM-08 composition |
| 2. Module integration | Open | Identity, course/membership, ATT and central audit adapters; real neighboring-team contract references |
| 3. Tests/security | Started | PostgreSQL suite and CI step exist; extend to real-provider HTTP flows and confirm the actual remote run |
| 4. Startup/documentation | Started | This increment has repeatable checks; update complete deployment instructions, architecture and DFD after integration |
| 5. Individual contribution | External evidence pending | Student's real task, commits/PR and ability to explain the code; no separate contribution file or fabricated history |

Validation for this source increment is recorded in `KT2-VALIDATION.md`. Remote CI,
actual Tapiz state and external contract acceptance are not inferred from local tests.
