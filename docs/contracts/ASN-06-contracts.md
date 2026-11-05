# Contracts: individual deadline extension (ASN-06)

| | |
|-|-|
| **Owner** | Team 15, Assignments component (ASN-04, ASN-06) |
| **Version** | 0.1 (draft, not yet accepted by the teams listed under Consumers) |
| **Note** | Not listed in specification 3.1. These are module ports proposed by the team. |
| **Requirements** | ASN-06, ASN-16, ASN-19, ACC-05, ACC-17, AUD-01, AUD-03 |

Until a consumer confirms a contract in a reviewed pull request, every name and rule below is a proposal.

## Purpose

`ExtendDeadlineService` lets an authorized instructor give one student a later deadline. The Assignments module owns the rule; it does not own roles, membership or the audit log, so it asks for them through the ports below. The module does not reference `Platform.Access`, `Platform.Audit` or `Platform.Api`.

## Ports

| Port | Method | Implemented by | Rule |
|-|-|-|-|
| `IDeadlineExtensionAuthorization` | `ValueTask<bool> CanExtendAsync(string actorId, Guid assignmentId, string courseId, CancellationToken)` | Access / membership (Team 3) | Decision made on the server. Deny by default (ACC-05): unknown actor, unknown course or missing role returns `false`. `actorId` is the logged-in user, never a value from a request body. |
| `ICourseEnrollmentCheck` | `ValueTask<bool> IsActiveStudentAsync(string studentId, string courseId, CancellationToken)` | Membership (Team 3, ACC-02, ACC-17) | Yes or no only. No membership data leaves the module. |
| `IDeadlineExtensionStore` | `GetAsync(Guid assignmentId, string studentId, CancellationToken)` | Assignments persistence (Team 15) | Returns the current individual deadline of that student, or `null`. |
| `IDeadlineExtensionStore` | `SaveAsync(StudentDeadlineOverride, DeadlineExtensionAuditEntry, CancellationToken)` | Assignments persistence (Team 15) | Saves the deadline and the audit entry **both or neither** (Audit D3: one transaction, or change plus outbox entry). Throws `InvalidOperationException` when the stored deadline is no longer `PreviousDueAtUtc` (concurrent change). Throws when it cannot store both; it never reports success without the audit entry. |
| `IDeadlineExtensionAuditWriter` | `ValueTask<bool> TryWriteAsync(DeadlineExtensionAuditEntry, CancellationToken)` | Host adapter to `IAuditWriter` (Team 12) | Used for denied and failed attempts. Returns `false` when not stored. The adapter raises the technical alarm. A failed write never grants access. |

## Audit entry

`DeadlineExtensionAuditEntry(Outcome, AssignmentId, CourseId?, StudentId?, PreviousDueAtUtc?, NewDueAtUtc?, ReasonCode?)`

- No actor and no time: the adapter takes both from the trusted server context (see [audit/CONTRACTS.md](../components/audit/CONTRACTS.md), decision A3).
- No free text. `ReasonCode` is one of `forbidden`, `invalid_student`, `archived`, `not_enrolled`, `invalid_deadline`, `save_failed` (format `^[a-z][a-z0-9_]{0,31}$`, as `reasonCode` in the Audit catalog). It is set only for `Denied` and `Failure`.
- Outcomes: `Success` (saved by `SaveAsync`), `Denied` (authorization refused), `Failure` (allowed but rejected or failed).

### Proposed Audit catalog entry (needs Team 12)

The Audit catalog (schema version 1) has no event type for this action, and a new type needs a schema and a test. Proposal:

| Event type | Producer | Outcomes | Actor | Course | Resource | Allowed details |
|-|-|-|-|-|-|-|
| `assignment.deadline.extended` | Assignments (ASN-06) | Success, Denied, Failure | `User` (or `Anonymous` for Denied) | required | `Account` (the student) | `previousDueAt`, `newDueAt` (UTC timestamps), `reasonCode` (Denied and Failure only) |

Open point: `AuditEventDraft.CourseId` is a `Guid`, but `Assignment.CourseId` is a `string` (for example `course-oib`). The two modules must agree on one course identifier type before the adapter can be written.

## Errors

| Error | When |
|-|-|
| `UnauthorizedAccessException("Access denied.")` | Invalid actor ID, unknown assignment, or the caller may not extend. Always the same message. |
| `ArgumentException` | Student ID does not match `^[A-Za-z0-9_-]{1,64}\z`. |
| `ArgumentOutOfRangeException` | New deadline is not later than the student's current deadline, or is not in the future. |
| `InvalidOperationException` | Assignment is archived; student is not an active member; or the deadline was changed concurrently. |

## Consumers

| Team / module | Use |
|-|-|
| Member 4 (authorization) with Team 3, Access and membership (ACC-03, ACC-05, ACC-17) | Implements `IDeadlineExtensionAuthorization` and `ICourseEnrollmentCheck`. |
| Member 5 (audit and API) with Team 12, Audit (AUD-01) | Adds the event type and provides the adapter for `IDeadlineExtensionAuditWriter`; with member 1, the atomic audit entry in `SaveAsync`. |
| Member 2 (ASN-04, start attempt) | Reads the student's extension with `IDeadlineExtensionStore.GetAsync` and calls `Assignment.GetAvailability(now, individualDueAtUtc)`. |
| Member 1 (database and domain) | Implements `IDeadlineExtensionStore` on PostgreSQL, based on `StudentDeadlineOverride`. |
| Member 5 (API) | Endpoint that passes the session's user as `actorId`. |

## Fakes

Private nested fakes in `backend/Platform.Tests/Assignments/ExtendDeadlineServiceTests.cs`. The store fake keeps the real contract (both or neither, stale save rejected). The authorization fake allows only listed instructors, so the tests also prove deny by default. These tests do not prove the real database, transactions or the Access integration.

## Contract tests

1. A user who is not an instructor of the course cannot extend (nothing saved, denial audited).
2. The new deadline must be later than the current individual or assignment deadline, and in the future.
3. A second extension records the first one as `PreviousDueAtUtc`.
4. A save over a deadline that changed in the meantime is rejected and the other change is kept.
5. A store failure produces a `Failure` audit entry and no stored deadline.
6. An audit writer failure while denying still ends in `Access denied.`

## Change history

| Version | Date | Change |
|-|-|-|
| 0.1 | 2026-11 | Initial draft |
