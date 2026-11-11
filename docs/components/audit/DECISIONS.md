# Audit module: decisions and KT1 scope

**Status:** maintained by the Audit component (AUD-01, AUD-06), story S-03. A decision is
**accepted** only when its owner's confirmation is linked (pull request, review comment or
written answer). Until then it is **open** and the temporary rule applies.

## Decisions already taken in code

These were decided inside the Audit component and are visible in merged or proposed code.
They can still change through review.

| # | Decision | Reason | Where |
|-|-|-|-|
| A1 | The contract lives in `Platform.Audit/Contracts`; there is no separate contracts project. Only contract types and `AddAudit()` are public, everything else is `internal`. | `internal` gives the same protection without changing the skeleton from S-01. | PR #42 |
| A2 | `IAuditWriter.WriteAsync` returns `AuditWriteResult` (`Recorded`, `Rejected` with a stable code, `Unavailable`) instead of throwing for expected failures. | No silent success; same style as `Platform.Access` (records and enums). | PR #42, CONTRACTS.md |
| A3 | The draft has no actor, time or correlation ID. A separate `ITrustedAuditWriter` exists for server adapters. | The client cannot forge who, when or which request (AUD-01, SEC-01). | PR #42 |
| A4 | Details are an allowlist per event type, string values only, at most 20 keys and 2048 bytes; secret-like key names are rejected as an extra check. | The system cannot reliably detect every secret in free text, so free text is not accepted (AUD-03). | PR #42 |
| A5 | The catalog and draft validation belong to S-02; the writer (S-06) calls `AuditDraftValidator` before storing. | Avoids two validators for the same rules. Proposed in PR #42; member 3 to confirm. | PR #42 |
| A6 | `AuditEvent` is an immutable domain record with its own invariants, independent of the validator. A retry with the same `EventId` and content is not a conflict; the time is not compared. | Defence in depth (ADM-07); supports idempotent retries. | S-03 |
| A7 | Exception to A1: `IAuditContextProvider` is a public application integration port so the composition host can supply trusted server identity and correlation state without making the writer implementation public. `Platform.Identity.Tests` is a test-only friend assembly so its PostgreSQL integration test can exercise internal Audit persistence; production consumers still use only public contracts and composition entry points. | Keeps the runtime boundary explicit while avoiding public persistence types solely for cross-module integration testing. | PR #113; owner review pending |

## Open decisions from the team plan (v2.2)

| # | Question | Owner | Target | Temporary rule |
|-|-|-|-|-|
| D1 | Identity and membership sources; owners of AUD-02..05; does Identity switch to the Audit contract or keep its own `IAuditWriter` behind an adapter? | Member 9 | 03.11 | Only test adapters until confirmed. Proposal: [INTEGRATION.md](INTEGRATION.md) S11-07, S11-08, S11-10. |
| D2 | Access DI lifetimes, explicit replacement of `NullAccessAuditWriter`, behaviour when the audit write fails. | Members 1, 5, 9 with Team 3 | 03.11 | No claim of a finished Access integration. Proposal: [INTEGRATION.md](INTEGRATION.md) S11-01 to S11-05. |
| D3 | Shared transaction or transactional outbox for privileged changes; meaning of each outcome per producer. | Members 2, 3, 9 with producers | per producer | IDN-03 uses one PostgreSQL transaction for status, session revocation and Audit (PR #148, S-16 proof). Other privileged producers still require their own agreed transaction or outbox; see [INTEGRATION.md](INTEGRATION.md) S11-06. |
| D4 | Global or administrator view of events; events without a course; which fields a teacher sees. | Members 5, 7, 9 with the assistant | before S-14 | Only the course view; no automatic global access. |
| D5 | Query limits and behaviour when writing `audit.events.viewed` fails. | Members 4, 5, 6 | before S-14 | Proposed in S-07 (see [Query limits](#d5-query-limits-proposal-s-07)); 503 before any data is sent if the viewed event cannot be written. |
| D6 | Audit volume of Deny decisions and load on the database. | Member 9 with Team 3 | before S-13 | No silent sampling of mandatory records; measure and alarm on loss. Proposal: [INTEGRATION.md](INTEGRATION.md) S11-04, S11-07. |
| D7 | KT1 and KT2 dates and team capacity. | Member 1 | 03.11 | 09–10.11 is a working assumption. Everyone re-estimates the stories. |
| D8 | Retention, restore and extra integrity protection. | Members 2, 7, 9 with ADM owners | KT2 | No public delete; stronger protection only with a justified design. |

## New questions found while working

| # | Question | Found in | Owner | Temporary rule |
|-|-|-|-|-|
| D9 | IDN-01 account registration (PR #40) creates accounts without an audit record, although it is a privileged action (spec 2.1). Add an `account.created` event type to the catalog? | `Platform.Identity` | Member 9 with Team 1 | Not in catalog v1; a new type needs a schema and a test. |
| D10 | Three interfaces named `IIdentityContext` now exist (`Platform.Api.Services.Identity`, `Platform.Identity.Contracts`, and the reference contract in the spec). Which one gives the current caller for the audit actor? | `Platform.Api`, `Platform.Identity` | Members 3 and 9 with Identity teams | The writer's context adapter is not built until this is answered. |
| D11 | `Platform.Identity.Tests` is not in `SecurityLab.slnx`, so its tests do not run locally or in CI. | `backend/SecurityLab.slnx` | Identity team; member 1 for CI | Reported only; the Audit component does not change another team's project. |
| D12 | `ChangeAccountStatus` writes a free-text administrator reason into its audit detail, which conflicts with AUD-03. Proposed: a reason code. | `Platform.Api` | Member 9 with Team 2 | Same as open question 1 in CONTRACTS.md. |
| D13 | `DefaultDenyAccessDecision` calls the audit writer outside any `try`/`catch`, so a writer exception replaces the computed decision. | `Platform.Access` | Member 9 with Team 3 | Forwarder not connected; proposal in INTEGRATION.md S11-04. |
| D14 | Resolved in current code: Access returns `PolicyDenied` when a policy returns false and `PolicyError` on evaluation failure. | `Platform.Access` and REV-05 audit adapter | Member 9 with Team 3 | REV-05 preserves `policy_denied` separately; Audit accepts both codes. The revoked-grant HTTP test checks the stored reason (2026-11-06). |
| D15 | `SubjectRef.Id` is a free string, while the audit actor ID is an account GUID. | `Platform.Access` | Member 9 with Team 3 | The forwarder records nothing for a non-GUID subject and raises an alarm (S11-05). |

## D5 query limits (proposal, S-07)

Implemented in `CourseAuditQueryNormalizer` and `CourseAuditQueryLimits`. Proposed by member 4;
members 5 and 6 to confirm. Every refused value is a 400 in S-14 with a field name and a stable
code (`Required`, `InvalidFormat`, `UnknownValue`, `OutOfRange`); the value itself is never returned.

| Filter | Rule |
|-|-|
| `courseId` | From the route, mandatory, not empty. The SQL always has `course_id = @course`. There is no global mode. |
| `page`, `pageSize` | Decimal digits only (at most 6). Defaults 1 and 50; `page >= 1`; `pageSize` 1 to 100; `(page - 1) * pageSize <= 10 000` (T11). |
| `type` | Exact event type from `AuditEventCatalog`, case-sensitive. |
| `outcome` | `success`, `denied` or `failure` (lower case, as in `audit.events.viewed`). |
| `actorId` | GUID in `D` format, not empty. |
| `correlationId` | `[A-Za-z0-9._-]{1,64}`, the same rule as the audit contract. |
| `from`, `to` | ISO 8601 date and time with `Z` or `+hh:mm`/`-hh:mm` (no date-only or local time), at most 40 characters, converted to UTC. `from` inclusive, `to` exclusive, `from < to`, range at most 31 days. |
| no date | Last 7 days up to the current server time. |
| only `from` | `[from, from + 7 days)`. |
| only `to` | `[to - 7 days, to)`. |

Sorting is `occurred_at DESC, id DESC` (index `ix_audit_events_course_time_id`). Instead of a
`COUNT`, the query reads `pageSize + 1` rows and returns `hasNextPage`; there is no total count.
`hasNextPage` is false on the last page the offset limit allows, so it never points to a refused page.
Offset pages are not a snapshot: new events can shift later pages. A cursor stays a KT2 option.
The DTO is an allowlist (id, time, type, outcome, actor kind and id, resource, correlation ID);
`details` is not selected from the database until D4 decides field visibility.
A storage failure gives `Unavailable` (503) without exception text.

In S-14 a `+` in a query-string offset must be sent as `%2B`, otherwise it arrives as a space
and the time is refused.

## KT1 scope

The official KT1 minimum comes from `docs/CHECKPOINT_1.md`: started code and behaviour,
individual contribution, component architecture, DFD with trust boundaries, a short
demonstration and a plan for the remaining work. Documentation alone is not enough.

### Minimum for KT1

| Item | Stories | State on 09.11 |
|-|-|-|
| Audit module skeleton, solution and CI that fails on zero tests | S-01 | Done (PR #35) |
| Audit contract, event catalog, draft validation and T03 tests | S-02 | Done (PR #42) |
| Domain record, decisions and KT1 scope | S-03 | Done (PR #51) |
| Threat model, security matrix and DFD of the Audit component | S-04, S-18 | Done for S-04 (PR #76); the DFD predates later merges and is updated in S-18 |
| A verifiable write of an event, at least in memory and clearly marked as such | S-06, S-12 | Writer done (PR #72), PostgreSQL storage (PR #73); IDN-03 status change writes through `ITransactionalAuditWriter` (PR #148); S-12 PR #113 open |
| Individual evidence for each of the 10 members | S-20, S-21 | S-20 in [AUD-01-AUD-06.md](../AUD-01-AUD-06.md); S-21 not started |

If the AUD-06 endpoint is shown as working, server-side authorization and course isolation are
mandatory. An unfinished endpoint stays unreachable, and every stub is labelled as a stub.

### Extended internal goal for KT1

PostgreSQL storage with migration and restricted runtime rights (S-05, S-16), the Access adapter
(S-13), the course query API with test memberships (S-07, S-14, S-15), correlation context (S-10)
and integration tests against PostgreSQL (S-09). These are team goals, not an official KT1
condition. Unfinished items move to the KT2 backlog with a new date.

## Story estimates

The plan's 113 story points are the historical estimate from v2.1. Every member re-estimates
their own stories at the team sync; the new value is entered in Tapiz and here.

| Story | Owner | Plan SP | New SP | Card in Tapiz |
|-|-|-|-|-|
| S-01 | 1 | 5 | done | |
| S-02 | 8 | 5 | done | |
| S-03 | 8 | 3 | | |
| S-04 | 7 | 5 | | |
| S-05 | 2 | 5 | | |
| S-06 | 3 | 5 | | |
| S-07 | 4 | 5 | 5 | S-07 CourseAuditQuery |
| S-08 | 5 | 5 | | |
| S-09 | 6 | 5 | | |
| S-10 | 10 | 5 | | |
| S-11 | 9 | 3 | | |
| S-12 | 3 | 8 | | |
| S-13 | 9 | 8 | | |
| S-14 | 4 | 8 | | |
| S-15 | 5 | 5 | | |
| S-16 | 2 | 5 | | |
| S-17 | 6 | 5 | | |
| S-18 | 7 | 8 | | |
| S-19 | 10 | 5 | | |
| S-20 | 8 | 5 | | |
| S-21 | 1 | 5 | | |

## Change history

| Date | Change |
|-|-|
| 2026-11-03 | Initial register, KT1 scope and estimate table (S-03) |
| 2026-11-03 | Links to the S-11 proposals; new questions D13 to D15 (S-11) |
| 2026-11-05 | D5 query limits proposal and S-07 course query (S-07) |
| 2026-11-09 | KT1 minimum state updated; link to the component document (S-20) |
