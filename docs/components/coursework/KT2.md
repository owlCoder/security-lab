# KT2 — ACC-04 (J) and ADM-08 (N)

Team 8 owns concrete-resource authorization and scoped multi-course administration.
This document describes the current delivered source, including increments 3 and 4.
It does **not** certify complete KT2 or neighboring-team acceptance.

## Current status — 2026-11-17

PR [#199](https://github.com/owlCoder/security-lab/pull/199) was merged on 2026-11-15
as `b53a9bc`. It contains the first two KT2 increments. The new delivery is based on
current main `29db660` and includes the previously unpublished third increment.
Continue on a new `feature/kt2-acc04-adm08-demo` branch and a new PR; do not attach
new work to the already merged PR. No remote PR/CI result for this delivery is inferred.

| KT2 criterion | Current implementation/evidence | Still open |
|---|---|---|
| 1. Assigned functionality | ACC-04 server policies, read/draft consumer API and ATT adapter; ADM-08 persistent scoped delegation/settings/revocation and first-root operator | Actual ATT read/CAS and persistent ACC-02 providers; actual provider acceptance |
| 2. Module integration | Real ADM-08 Identity/session, ACC-01 catalog, shared Access and supported central Audit delivery; actual Kestrel/HTTPS lifecycle | ATT/membership integration; missing central audit mappings; human contract acknowledgement |
| 3. Tests/security | Strict PostgreSQL/HTTP/adapter NUnit suite, existing KT1 and Access regressions, real HTTPS live checks | Actual latest remote CI; coverage against the eventual real ATT/membership providers |
| 4. Startup/documentation | Repeatable live launcher, isolated seed/cleanup, operator commands, current architecture/DFD and actual evidence | Final ACC-04 provider startup/configuration and browser/UI deployment if required for the agreed product |
| 5. Individual contribution | Published first/second increment history and merged #199; key-code defense map below | Student publishes this delivery, records real Tapiz/PR links and explains it individually |

The specification source is `docs/spec/PROJECT_SPECIFICATION_OIB.pdf`, sections 2,
2.1, 3.1, 4.2 and 4.12; SHA-256
`e74f73cf4fdfd25fe469b799488274c826325fd09c3498757e0aa37fd097676c`.

## Reproduce the real ADM-08 live flow

Prerequisites: .NET 10, PowerShell and Docker Desktop running. From a clean checkout
containing this delivery, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\demo-coursework-live.ps1
```

This builds the demo/API and operator tool, starts its own PostgreSQL 17 container,
creates a uniquely named database, applies real provider migrations and seeds
fictional Identity accounts and two ACC-01 courses. It invokes the actual ADM
operator executable to migrate and provision each course root. Those accounts are
isolated demonstration fixtures, not real users or a normal production seeding route.

The launcher then starts the actual `Platform.Api.dll` as a separate Kestrel process
in **Production** with a temporary HTTPS certificate and a random loopback port.
No TestServer, synthetic KT1 cookie, fake subject header, ATT service or membership
provider is substituted in this ADM flow. Existing KT1 routes are disabled. The
certificate is pinned in this demo client only; it is never imported/trusted in the
user certificate store, and another certificate pin is explicitly rejected.

The flow demonstrates normal password login, root/course isolation, limited and
view-only delegation, settings update, stale-version/input/Origin/header rejection,
separation from teaching/global account administration, a genuine server restart,
revocation using the exact previously issued delegate cookie, current blocked-account
and archived-course checks, real central Audit delivery/retry, and real logout.
The administrative label survives restart; it does not rename the shared catalog.

Result: `TestResults/coursework-live-<run>/live-administration.json`. Its check list
contains HTTP status checks, TLS and state assertions, with explicit kinds. This is
not a NUnit test count. It records no passwords, raw cookies, private certificate or
connection strings. The launcher requires successful same-cookie/restart checks,
fresh evidence and cleanup. The API child, uniquely created database and temporary
certificate directory are removed; partial initialization failure is also cleaned up.
No Tapiz database or existing project database is modified by the default launcher.

An existing-server run is optional and must use an explicitly dedicated test server:
set `COURSEWORK_TEST_POSTGRES` and add `-UseExistingServer`. Test bootstrap needs
CREATE DATABASE permissions. It creates/deletes only its GUID-named database.
`-NoBuild` is intended for CI after the full solution build. The normal default builds
what it needs. The CI workflow adds one live-demo step and preserves existing jobs.

## Repeat the NUnit regression/integration checks

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-coursework-postgres.ps1
dotnet test backend/Platform.Kt1.Tests --configuration Release
dotnet test backend/Platform.Access.Tests --configuration Release
```

The first command uses its own disposable PostgreSQL container and requires discovery
and successful execution of the service, HTTP and adapter fixtures. Skipped cases
are rejected. `-Demonstrate` repeats one ADM lifecycle and one explicitly doubled
ACC-04 consumer lifecycle after the suite; those repetitions do not add distinct
NUnit cases. A general solution test run without `COURSEWORK_TEST_POSTGRES` skips the
PostgreSQL fixtures, so it is not proof those cases executed. Actual results and
scope are recorded in `KT2-VALIDATION.md`; new remote CI must be checked separately.

## Normal ADM-08 runtime configuration

The repeatable live tool above supplies isolated fixture configuration automatically.
For a manually provisioned runtime, configure secrets externally:

| Configuration | Meaning |
|---|---|
| `CourseworkAdministration:Enabled=true` | Explicitly enable the four ADM endpoints |
| `ConnectionStrings:Identity` | Real current account/session provider |
| `ConnectionStrings:Team21` | Existing ACC-01 catalog provider |
| `ConnectionStrings:CourseworkAdministration` | ADM-owned schema, with narrow runtime grants |
| `ConnectionStrings:Audit` | Durable Identity/central audit provider |

Provider migrations remain owned by their modules. The web host does not perform
ADM operator provisioning or migrations on an HTTP request. Configure the
schema-owner connection in `COURSEWORK_MIGRATION_CONNECTION`, then run:

```powershell
dotnet run --project backend/Platform.Coursework.AdminTool -- migrate
dotnet run --project backend/Platform.Coursework.AdminTool -- grant-runtime YOUR_EXISTING_RUNTIME_ROLE
dotnet run --project backend/Platform.Coursework.AdminTool -- bootstrap-root COURSE_GUID ACCOUNT_GUID --confirm-initial-root
```

Bootstrap additionally consumes external `ConnectionStrings__Identity` and
`ConnectionStrings__Team21`. Actual confirmed IDs must refer to an existing active
account/course. The tool creates neither account nor catalog course. It serializes
competing initial-root attempts, is idempotent for the same root and rejects replacing
another root. Local metadata/root/operator audit commit together or roll back. There
is no HTTP root endpoint or implicit teaching/global-role promotion. Operator events
identify their operator origin without inventing an end-user actor. The HTTP runtime
role cannot provision root columns or rewrite/delete producer audit. Root recovery or
transfer still requires an agreed operator procedure; no recovery API is claimed.

For configured local HTTPS, the existing `AdministrationKt2` profile listens on
`https://localhost:7188` after `dotnet dev-certs https --trust`. That optional manual
profile uses the existing Development startup and its module seeds; use dedicated
local/test provider connections. It is separate from the Production live launcher,
which needs no certificate-store changes. A real reverse proxy must provide the
correct trusted scheme/host; production forwarding/deployment is not added here.

| API operation | Required administrative authority |
|---|---|
| GET `/api/course-administration/{courseId}` | ViewAdministration in that exact course |
| PUT `/{courseId}/settings` | ManageSettings; expected current version |
| POST `/{courseId}/delegations` | Root with ManageDelegations; allowed limited capabilities only |
| POST `/{courseId}/delegations/{grantId}/revoke` | Same-course root; active non-root target; expected version |

All routes use shared Access metadata. Actor identity comes only from a validated
real session; no client-supplied actor, global role/header or synthetic cookie grants
authority. Mutations require `X-Coursework-Request: 1`; a supplied Origin must match
the request scheme/host. Bodies are bounded at 16 KiB. Unauthenticated requests use
401; foreign/missing/administrative-store-failure responses use the same public 404;
Identity outage uses uniform 503. Responses are `no-store`.

`CatalogCourseDirectory` reads the published ACC-01 context without writing its
tables. Current active course status is checked in policy and before own commit.
Per-course locks order administrative changes/revocation; exact grants and versions
are bound to reads/writes. Identity/catalog/administrative providers are independent
transactions: no distributed atomic revocation guarantee is claimed. A change racing
after a final provider check can overlap an already running operation; every following
request reads current state. Final provider failure rolls back staged local changes.

## Central Audit relay and deliberate unmapped records

Normal writes require their matching durable producer event in the same ADM
PostgreSQL transaction. Outbox records contain metadata only. For explicit central
delivery, configure `ConnectionStrings__Audit` and the producer operator connection:

```powershell
dotnet run --project backend/Platform.Coursework.AdminTool -- relay-once
```

The batch consumes server-authored records via the published `ITrustedAuditWriter`,
uses stored trusted actor/correlation, stable source/action event IDs and separate
acknowledgement receipts. Successful delegation/revocation maps to the existing
`course.delegation.*` types, per administrative capability; producer DENY maps to
`access.denied`. Central failure leaves records unacknowledged; a crash after central
append can be retried without duplicated event content. Original producer events
are never rewritten/deleted. This is at-least-once delivery with central idempotency,
not a distributed transaction. Central occurrence time is delivery time; original
producer occurrence time remains in the outbox.

The central catalog still has no agreed ADM settings-change/root-bootstrap/read-success
type. Those records stay durable locally and are counted as
`retained_without_catalog_mapping`. They are not silently retyped or falsely marked
delivered. The relay is an explicit operator command, not a hidden scheduled task.
Human audit-schema acceptance remains pending.

## ACC-04 actual-provider boundary remains open

The source includes a real-session consumer API and adapters:

| Route | Shared Access requirement | Additional application checks |
|---|---|---|
| GET `/api/resource-attempts/{id}` | existing `attempt.read` | Current owner/account/membership and exact snapshot |
| GET `/{id}/review` | existing `attempt.read.course` | Same-course teacher, Submitted/Completed only |
| PUT `/{id}/answer` | `attempt.draft.update` concrete policy | Student owner, InProgress, valid answer, exact snapshot/CAS and provider audit receipt |

The shared read policies are preserved. A resolver decorator supplies trusted owner/
course metadata and forwards unrelated resource types to the existing resolver. A
fresh-scope bridge consumes `Platform.Access.Contracts.IMembershipReader`; the current
Empty provider grants nothing. No memberships are synthesized by runtime composition.

Enable `CourseworkAttempts:Enabled=true` only with an available trusted provider.
Configuration requires Identity plus `CourseworkAttempts:ProviderBaseUrl` (fixed HTTPS
URL ending in `/`, no credentials/query/fragment) and external `ProviderToken`. The
ATT adapter forwards no browser cookies or client credentials, follows no redirects,
strictly validates/bounds responses at 64 KiB, and applies a five-second deadline to
headers and body. It never automatically retries a mutation.

The proposed `acc04-att-v1` transport contract is in `CONTRACTS.md`. Its provider must
atomically bind Id/OwnerId/CourseId/State/Version, preserve historical answers and
commit mutation audit with a nonempty receipt; operation IDs support deduplication.
A lost reply can leave an unknown remote commit outcome; clients must re-read state
before an intentional subsequent change. The consumer checks the current session
before provider dispatch and before returning a read, but no cross-provider rollback
of an already started remote write is claimed. Local request logs are diagnostic,
not proof of durable ATT audit.

**Actual ATT read/answer/CAS and persistent ACC-02 are still absent on this main
revision.** Positive ACC-04 HTTP cases use real Identity plus explicit ATT/membership
contract doubles. A returned fake receipt does not certify real ATT persistence.
The real ADM live demonstration does not start an invented ATT provider and leaves
those routes disabled. Obtain the actual provider contract, integration and human
acknowledgement before marking criteria 1/2 fully complete.

## Individual defense map

| Key code | Explain and demonstrate |
|---|---|
| `Modules/Access/AttemptPolicy.cs`, `AttemptService.cs` | Server ownership/scope; snapshot binding; authorized-only conflict; immutable submitted/completed answers |
| `Modules/Access/Integration/` | Public membership contract; trusted ATT metadata; fixed-origin credential boundary; exact CAS/audit provider obligation |
| `Modules/Administration/AdministrationPolicy.cs` | Course-scoped capabilities; root-only delegation/revocation; separation from teaching/global account powers |
| `Modules/Administration/Persistence/` | Course locks, version checks, fresh authority, transactionally matched change/audit; rollback on failure |
| `Modules/Administration/Integration/` | Real read-only catalog; controlled first-root bootstrap; central relay acknowledgement/idempotency and unmapped records |
| `Platform.Coursework.Demo/` and live script | Real HTTPS/Kestrel process, pinned temporary certificate, normal login, restart persistence, exact-cookie revocation and cleanup |
| `Platform.Coursework.Tests/` | Identify which cases use real providers and which deliberately use neighboring-provider doubles; explain a positive and negative case |

Use actual Git/PR links and dates for contribution evidence. Published #199 proves
prior work, not this unpublished continuation, external contract review or real
Tapiz creation. No separate CONTRIBUTION file or fabricated completion history is
required. Architecture/DFD links are in `diagrams/README.md`; recorded local checks
are in `KT2-VALIDATION.md`.
