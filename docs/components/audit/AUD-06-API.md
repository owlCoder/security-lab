# AUD-06: course audit view (API) and request context for `IAuditWriter`

| | |
|-|-|
| **Owner** | Team 12, Audit component (AUD-01, AUD-06) |
| **Status** | Implemented in code, **not yet compiled or run by the author** (no .NET SDK was available when it was written). Run `dotnet test backend/SecurityLab.slnx` before relying on it. |
| **Requirements** | AUD-01, AUD-03, AUD-04, AUD-06, ACC-04, SEC-01, SEC-02 |

Everything below is a proposal until the owners named in [INTEGRATION.md](INTEGRATION.md) accept it.

## What was added

| Part | Where | Purpose |
|-|-|-|
| `IAuditActorSource` (public) | `Platform.Audit/Contracts` | Host hook: who is the caller of this request. Lets the ordinary `IAuditWriter` work without the caller passing an actor. |
| `AddAuditRequestContext<T>()` | `Platform.Audit/Composition` | Replaces the "no ambient context" provider explicitly. Actor from the host, correlation ID from `CorrelationMiddleware`. |
| `SessionAuditActorSource` | `Platform.Api/Modules/Audit` | Host implementation. Reads **only** the server-validated session. No session means `Anonymous`; no HTTP context means no actor (the writer then rejects with `InvalidContext`). |
| `IAuditCourseReader` (public) | `Platform.Audit/Contracts` | The AUD-06 use case: read the events of one course. |
| `AuditCourseReader`, `PostgresAuditEventQueryStore` (internal) | `Platform.Audit` | Keyset query (newest first), always filtered by `course_id`; only constant SQL, all values are parameters. |
| `AuditEventsController` | `Platform.Api/Controllers/Auditing` | `GET /api/audit/courses/{courseId}/events` |
| `/api/audit` in `RevisionRequestIdentity.AppliesTo` | `Platform.Api/Security/RevisionAuthorization` | **One-line change in another team's file.** The Access middleware must see the real caller on this route, and the REV-05 provider already does exactly that. Needs the REV-05 owner's review. |

## Endpoint

`GET /api/audit/courses/{courseId:guid}/events`

| Query | Rule |
|-|-|
| `eventType` | A type from the catalog (`AuditEventCatalog`). Anything else: 400. |
| `outcome` | `success`, `denied`, `failure` (letters only, case-insensitive). |
| `from`, `to` | Explicit UTC, ending with `Z` (`2026-11-08T10:00:00Z`). `from` must not be after `to`. |
| `limit` | 1 to 200, default 50 (proposal for D5, not accepted). |
| `cursor` | The `nextCursor` of the previous page. Strictly parsed. |

| Status | Meaning |
|-|-|
| 200 | `{ items: [...], nextCursor }`, `Cache-Control: no-store`. |
| 400 | `{ code: "invalid_query" }` |
| 401 / 403 | Decided by the Access middleware **before** the controller (no session / not allowed). |
| 503 | `{ code: "audit_unavailable" }`: storage failed or the view could not be recorded. **No events are in the body.** |

## Security properties (each has a test)

1. **Authorization is server-side and per course.** `[RequireAccess("audit.events.read", ResourceType="course", IdRouteParam="courseId")]`. Teacher of the course, or an assistant with a delegation of that action. A student, an assistant without the grant and a teacher of another course are denied, and the query store is never called (`AuditEventsHttpTests`).
2. **The course in the data query is the authorized route value.** A `courseId` in the query string is ignored.
3. **The view is audited before data leaves the module.** `audit.events.viewed` is written first (actor from the session, correlation ID from the server, filters and `resultCount` as catalog-valid details). If that write is not `Recorded`, the answer is 503 with no events (fail closed).
4. **No secrets.** Responses contain only what the catalog allowed at write time: allowlisted details, no free text, no answers, no tokens. Logs contain only event type, status and correlation ID, never filters, details or exception text.
5. **No HTTP write route.** Actor, time and correlation ID must come from the server, so an endpoint that accepts an audit draft from a client would let it forge them. Producers call `IAuditWriter` or `ITrustedAuditWriter` in code.
6. **Append-only is untouched.** The read side needs only `SELECT`, already granted to `securitylab_audit_runtime`.

## Visible fields (decision D4, still open)

A teacher sees `id`, `occurredAt`, actor kind and **account ID**, `eventType`, `outcome`, resource, `correlationId` and `details`. The actor account ID is shown because AUD-01 requires "who". If the assistant decides teachers must not see account IDs, remove `Actor.Id` in `AuditEventResponse`; the stored data does not change.

## Known limits

- **Membership source.** Until ACC-03/ACC-07 provide a real `IMembershipReader`, the host uses `EmptyMembershipReader`, so every call is denied. This is the safe default. `AuditEventsHttpTests` proves the route works with a real reader.
- **Events without a course** (login, logout, account status) are not visible through this API (open question 4 in CONTRACTS.md). The teacher API shows only events of the course.
- **Keyset cursor.** Positions are `(occurred_at, id)`. Events written during paging appear only if newer than the first page, as expected for a newest-first view.
- **Producers.** Identity still writes to its own log writer (S11-07). This change does not make login or logout count as AUD-01 coverage; that needs the Identity teams' acceptance.
