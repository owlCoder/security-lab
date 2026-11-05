# Audit component security assessment (AUD-01, AUD-06)

**Owner:** Vanja Jovanov (Team 12, member 7)
**Stories:** S-04 threat model and security matrix; S-18 DFD and implementation status
**Assessment basis:** repository commit `84166fb258681a5bc47aa510a3719226b7891ddd` (2026-11-04).
**Status:** documents the inspected code at that commit. Planned controls and integrations are explicitly marked; this is not a production-readiness claim.

## Scope and security objective

AUD-01 records security-relevant events with trustworthy actor, event, time and outcome fields. AUD-06 is intended to let an authorized teacher inspect only permitted events for a course. The audit record is security evidence, not a diagnostic log, an attempt history or laboratory content.

The assessment covers the Audit contract, domain, writer, PostgreSQL store, host composition and the current Access/Identity integration points. It does not claim that the writer is active in the running API, that login and rights changes reach PostgreSQL, or that an AUD-06 endpoint is available. Current implementation facts and residual work are summarized in [INTEGRATION.md](INTEGRATION.md), [PERSISTENCE.md](PERSISTENCE.md) and the [DFD source](diagrams/audit-dfd.mmd).

## Assets and trust boundaries

| Asset | Security property |
|---|---|
| Audit events and schema | Integrity, provenance, append-only runtime access, bounded retention |
| Actor and course scope | Derived from trusted server-side identity and resource context; never inferred from client claims |
| Event details | Allowlisted, bounded, non-secret metadata only |
| Audit read results (future AUD-06) | Confidentiality by active per-course authorization and least disclosure |
| Correlation and event identifiers | Trace a request and deduplicate retries without granting authority |
| Database credentials and migrations | Runtime has minimum rights; schema changes use a separately controlled identity |

Trust boundaries:

1. Browser/client to API: request fields, route IDs, filters, headers and cookies are untrusted.
2. API/application to identity, Access and membership providers: these are separate contracts; their ownership and semantics require agreement. A provider outage is not an authorization grant.
3. Application to PostgreSQL: parameterized queries cross into a separate data boundary. Database runtime permissions reduce impact but do not prove caller authorization.
4. Migration/bootstrap operator to database: privileged DDL and grants are separate from normal application writes.
5. Audit data to teacher UI (future): course filtering and field minimization must happen server-side before selection, count, paging or serialization.

## Threat model and controls

| Threat / misuse | Asset and boundary | Current control in code | Evidence / coverage | Residual risk or required follow-up |
|---|---|---|---|---|
| Client forges actor, time, outcome or correlation | Provenance; client/API | Public draft omits actor, time and correlation; writer adds trusted context and UTC time. Contract distinguishes `IAuditWriter` from server-only `ITrustedAuditWriter`. | `AuditWriterTests.WriteAsync_WhenDraftIsValid_WritesTrustedImmutableEventWithServerUtcTime`; `AuditDraftValidatorTests` actor/correlation cases | No concrete context provider is registered by `AddAudit()` yet. Public trusted-context interface can be misused by server code; review each caller. |
| Unrecognized event, forged outcome, course or resource | Event integrity; producer/application | Versioned event catalog validates type, outcome, actor kind, course presence and resource schema; domain record rechecks core invariants. | `AuditDraftValidatorTests`; `AuditEventCatalogTests`; `AuditEventTests` | Catalog ownership and new producer types require reviewed contract changes. Validation does not prove that a supplied course came from a trusted resolver. |
| Password, token, free text, oversized or malformed details enter storage | Confidentiality/integrity; producer to writer | Per-event allowlist, typed formats, secret-like key rejection, 20-key and 2048-byte limits; rejected values are not echoed. No request body is accepted as event detail. | `AuditWriterTests.WriteAsync_WhenDetailIsNotAllowed_RejectsWithoutWritingOrLeaking`; size boundary tests; `AuditDraftValidatorTests` detail cases | No validator can detect every secret embedded in arbitrary text; free text/nested objects are therefore excluded. Upstream producers remain responsible for not logging sensitive inputs elsewhere. |
| Anonymous login is falsely attributed to an account | Provenance/privacy; Identity to Audit | Anonymous actor has no ID; login form name is not an actor; catalog constrains actor/outcome combinations. | `TrustedWriteAsync_WhenAnonymousContextIsValid_RecordsWithoutActorId`; actor validation tests | Actual login integration and accepted policy for blocked-account login remain open with Identity owners. |
| Retry duplicates an event or overwrites history | Integrity; writer to PostgreSQL | Stable event ID; equal retry is idempotent; changed payload conflicts; SQL uses `ON CONFLICT DO NOTHING`; runtime role has no update/delete/truncate rights. | `AuditWriterTests` retry cases; `PostgresAuditEventStoreTests.Retry_WithSameContent_ReturnsOriginalWithoutDuplicate`, `Retry_WithDifferentContent_DoesNotOverwriteOriginal`; `AuditRuntimePermissionsTests.Runtime_RejectsForbiddenSqlWithoutChangingEvent` | PostgreSQL integration requires configured test database. A database administrator or compromised application can still alter/insert data; retention, backup and restore controls are separate work. |
| SQL injection or accidental cross-module schema mutation | Store integrity; application/database | Parameterized interpolated SQL, dedicated `audit` schema, migration history, least-privilege runtime grants. | `PostgresAuditEventStoreTests`; `AuditRuntimePermissionsTests` forbidden SQL cases; `AuditPersistenceMappingTests` | These controls do not protect against a privileged database operator. Do not grant runtime migration/DDL privileges. |
| Audit storage failure is reported as success or leaks internals | Integrity/availability; writer boundary | Writer returns `Unavailable` for store errors without echoing exception details; expected validation failures return stable codes. | `AuditWriterTests.WriteAsync_WhenStoreFails_ReturnsUnavailableWithoutLeakingFailure` | `AddAudit()` is still a skeleton, so runtime failure behavior is not active. Producers must fail closed for privileged mutations and coordinate transaction/outbox semantics (D3). Access currently calls its writer outside a catch boundary; agreement/change belongs with Access owners. |
| Change of rights commits without durable audit evidence | Integrity; producer/data transaction | PostgreSQL store can participate in an existing `AuditDbContext` transaction; its rollback test covers an uncommitted audit insert. | `PostgresAuditEventStoreTests.Rollback_RemovesUncommittedAuditInsert` | No integrated rights-change producer/outbox currently demonstrates atomicity. Audit insert rollback alone does not prove business-change rollback. D3/S-12/S-16 remains open. |
| Teacher reads another course's events or learns through count/page metadata | Confidentiality; future API/query boundary | No AUD-06 route/query is currently registered; absence keeps the incomplete capability unavailable. Course-scoped event schema and database index are not authorization controls. | No endpoint test exists in current implementation. | Before exposure: authenticate active caller; authorize `audit.events.read` for requested course; include course predicate in SQL before count/paging; recheck current membership/delegation on every request; default deny provider errors; return minimal DTO; prevent leakage through totals, cursors, filters and errors. |
| Student, assistant without explicit delegation, blocked account or global admin gets teacher view | Confidentiality; identity/membership to query | No teacher view is exposed. | No AUD-06 authorization tests exist yet. | Implement only against accepted Identity/Access/Membership contracts; global role/admin status alone must not grant course visibility. Revocation must affect the next request. |
| Stored metadata causes XSS, query manipulation or cache leakage in future UI/API | Confidentiality/integrity; API to browser | Contract restricts details to plain bounded strings; no audit UI or endpoint is exposed. | Catalog/value validation tests | Future query must parameterize filters, encode output as text, set appropriate no-store policy, and test malicious filter/text inputs. |
| Scoped identity/DbContext is captured by singleton | Cross-request isolation; DI composition | Scope/build validation fixtures and negative captive-dependency tests exist. | `CompositionScopeTests.AccessAndAuditRegistrations_PassScopeAndBuildValidation`; `HostStartupScopeTests.Host_WithCaptiveDependency_FailsToStart` | Actual Audit registrations are not yet installed. Revalidate all real adapters and lifetime changes when composition is wired. |
| Secrets appear in operational diagnostics | Confidentiality; application/log boundary | Writer does not return rejected values or storage exception text. Contract forbids credentials, cookies, tokens, session IDs, request/response bodies and student answers in details. | Writer rejection/failure tests | Existing Identity `LoggerAuditWriter` is a separate implementation and logs a free-text detail; it is not proof of AUD-01 structured persistent coverage. Review and migrate with its owning team. |

## Security status and acceptance conditions

At the assessment commit, the Audit contract/catalog, validator, immutable domain record, PostgreSQL mapping/store/migration and runtime database permission bootstrap/tests exist. The writer implementation exists in the module but is not registered: `AddAudit()` returns without adding services. The API still registers Identity's separate logger writer; Access still uses its null audit writer. There is no course audit query, route, or teacher UI. Therefore these artifacts establish component-level controls and tests, not completed end-to-end AUD-01/AUD-06 behavior.

Do not mark AUD-06 implemented until positive and negative API/query tests demonstrate active membership/delegation checks, cross-course isolation, provider outage denial, current revocation, minimal fields, and no leakage through counts or pagination. Do not claim durable AUD-01 coverage of login or rights changes until producer integration and the accepted transaction/outbox failure behavior are demonstrated against persisted state.

## Individual contribution

Vanja Jovanov prepared this threat assessment, the linked security matrix, and the Audit data-flow diagram for S-04/S-18. The source commit above is the inspected baseline; this document does not claim code integration or external review acceptance.
