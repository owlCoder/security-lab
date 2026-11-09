# Contract: `IAuditWriter` (Audit module)

| | |
|-|-|
| **Owner** | Team 12, Audit component (AUD-01, AUD-06) |
| **Version** | 0.1 (draft, not yet accepted by the consumers listed below) |
| **Spec reference** | Specification 3.1: structured security record, without personal secrets or sensitive solutions |
| **Requirements** | AUD-01, AUD-03, AUD-04, SEC-02, OPS-05 |

Until a consumer confirms it in a reviewed pull request, every name, field and limit below is a
proposal. Nothing here is claimed as an existing or accepted API.

## Purpose

Any module that performs a security-relevant action (login, change of rights, denied access)
reports it through one contract. The Audit module validates the report, adds the trusted
context (who, when, which request) and stores it as an append-only record. AUD-06 later reads
those records for teachers, filtered by course on the server.

## What this contract is not

- **Not a technical log.** `ILogger` output is for diagnostics. An audit record is a security
  record with an owner, a schema and access control.
- **Not the attempt action log.** `AttemptAction` (ATT-08) belongs to the Attempts module.
- **Not laboratory data.** Scenarios 5.2 and 5.7 use synthetic events, never the platform audit.

## Visibility and dependency direction

- Types in `Platform.Audit.Contracts`, the composition extensions and the PostgreSQL
  transactional writer are public integration entry points.
  Domain, application and infrastructure types of the module are `internal`.
- `Platform.Access` does **not** reference `Platform.Audit`. The host (`Platform.Api`) connects
  them with an adapter that implements `IAccessAuditWriter` and calls this contract (story S-13).
- Producers never read or write the audit table directly.

## Members

### `IAuditWriter`

```csharp
public interface IAuditWriter
{
    ValueTask<AuditWriteResult> WriteAsync(
        AuditEventDraft draft,
        CancellationToken cancellationToken = default);
}
```

The usual entry point. The caller describes **what** happened. The writer takes the actor and
the correlation ID from the trusted server context of the current request, and the time from
`TimeProvider` (UTC). The caller cannot set the actor, the time or the correlation ID.

### `ITrustedAuditWriter`

```csharp
public interface ITrustedAuditWriter
{
    ValueTask<AuditWriteResult> WriteAsync(
        AuditEventDraft draft,
        AuditContext context,
        CancellationToken cancellationToken = default);
}
```

For server-side adapters only, when the actor is already confirmed but is not yet (or no longer)
in the request context:

- successful login: credentials were verified, the session is not issued yet;
- the Access adapter: it forwards the subject of an access decision that was already evaluated;
- background jobs, such as expiry of a delegation: the actor is a service identity.

`AuditContext` must never be built from an HTTP body, query string or arbitrary header. The
compiler cannot enforce this; it is checked in code review (see residual risk below).

### `AuditEventDraft`

| Field | Type | Rule |
|-|-|-|
| `EventType` | `string` | Required. Must be a type from the catalog below. An unknown type is rejected. |
| `Outcome` | `AuditOutcome` | Required. Must be allowed for that event type. |
| `CourseId` | `Guid?` | Required, forbidden or optional depending on the event type. Comes from a trusted business context, never from client input. |
| `Resource` | `AuditResourceRef?` | The subject of the change (for example the account whose rights changed). It is not the actor. |
| `Details` | `IReadOnlyDictionary<string, string>?` | Only keys allowed for that event type, with string values. No nested objects. |
| `EventId` | `Guid?` | Optional idempotency key. Required for producers that retry or use an outbox. When `null`, the writer generates one. |

### `AuditOutcome`

| Value | Meaning |
|-|-|
| `Success` | The action was carried out and persisted. |
| `Denied` | The action was refused by an authorization or policy check. |
| `Failure` | The action was allowed but failed (invalid input, missing target, technical error). |

A technical failure is never recorded as `Success`.

### `AuditResourceRef`

`AuditResourceRef(string Type, string Id)`. `Type` is one of `Account`, `Course`, `Delegation`, `Revision`.
`Id` has at most 64 characters: ASCII letters, digits, `-` and `_`.

### `AuditContext` and `AuditActor`

```csharp
public sealed record AuditContext(AuditActor Actor, string CorrelationId);
public sealed record AuditActor(AuditActorKind Kind, Guid? Id);
public enum AuditActorKind { User, Service, Anonymous }
```

| Kind | `Id` | Used for |
|-|-|-|
| `User` | required | an authenticated person |
| `Service` | required | a registered service identity (background job) |
| `Anonymous` | must be `null` | a caller without a confirmed identity, for example a failed login |

The email address or user name typed into a login form is **not** an actor and is never stored.
Users sometimes type their password into that field.

`CorrelationId` has 1 to 64 characters: ASCII letters, digits, `-`, `_` and `.`. One request can
produce several events with the same correlation ID, so it is not a deduplication key.

### `AuditWriteResult`

```csharp
public sealed record AuditWriteResult(
    AuditWriteStatus Status,
    AuditRejectionCode RejectionCode,
    Guid? EventId);

public enum AuditWriteStatus { Recorded, Rejected, Unavailable }
```

| Status | Meaning | Producer must |
|-|-|-|
| `Recorded` | The event is stored. `EventId` identifies it. | continue |
| `Rejected` | The draft breaks the contract. Nothing is stored. `RejectionCode` says why. | treat it as a defect in its own code; do not confirm a privileged change |
| `Unavailable` | Storage failed. Nothing is guaranteed to be stored. | treat it as failure (see producer obligations) |

`AuditRejectionCode` values: `None`, `UnknownEventType`, `OutcomeNotAllowed`, `CourseRequired`,
`CourseNotAllowed`, `InvalidResource`, `InvalidContext`, `UnknownDetailKey`, `ForbiddenDetailKey`,
`InvalidDetailValue`, `DetailsTooLarge`, `EventIdConflict`.

The writer does not throw for expected failures. The only exception it lets through is
`OperationCanceledException` when the caller's token was cancelled before the event was stored.

**Repeated `EventId`:** the same `EventId` with the same content returns `Recorded` with that
`EventId` and stores nothing new. The same `EventId` with different content returns `Rejected`
with `EventIdConflict` and never replaces the stored record.

## Producer obligations

1. **Privileged change that was allowed** (status change, role change, delegation): the business
   change may be confirmed only when its audit record is guaranteed. Either both are committed in
   one transaction, or the change and an outbox entry are committed together. Which of the two is
   decided in D3 (stories S-12 and S-16). Returning an error after the change is already committed
   is not fail-closed.
2. **Denied action:** the denial stays a denial even if the audit write fails. The producer raises
   a technical alarm without secrets. An audit failure never grants access.
3. **Audit of an action that is already committed:** pass `CancellationToken.None` (or hand the
   event to the outbox), so that a cancelled HTTP request does not lose the record.
4. **Never put into a draft:** passwords (even wrong ones), tokens, cookies, session identifiers,
   `Authorization` headers, evaluation keys, student answers, request or response bodies, typed
   login names or any free text.

## Event catalog (schema version 1)

| Event type | Producer (requirement) | Outcomes | Actor | Course | Resource | Allowed details |
|-|-|-|-|-|-|-|
| `auth.login` | Identity (IDN-02) | Success, Failure | Success: `User`. Failure: `Anonymous`, or `User` when the credentials were valid but the account is not active | forbidden | optional `Account` (the account that logged in) | `reasonCode` |
| `auth.logout` | Identity (IDN-02) | Success | `User` | forbidden | none | none |
| `auth.login.throttled` | Identity (IDN-05) | Denied | `Anonymous` | forbidden | none | none |
| `account.status.changed` | Identity (IDN-03), Admin (ADM-01) | Success, Denied, Failure | `User` or `Anonymous` (Denied, not logged in) | forbidden | `Account` (required) | `oldStatus`, `newStatus`, `reasonCode` |
| `course.member.role.changed` | Memberships (ACC-03) | Success, Denied, Failure | `User` or `Anonymous` (Denied) | required | `Account` (required) | `oldRole`, `newRole`, `reasonCode` |
| `course.delegation.granted` | Memberships (ACC-07) | Success, Denied, Failure | `User` or `Anonymous` (Denied) | required | `Account` (required) | `action`, `delegationId`, `reasonCode` |
| `course.delegation.revoked` | Memberships (ACC-07) | Success, Denied, Failure | `User` or `Anonymous` (Denied) | required | `Account` (required) | `action`, `delegationId`, `reasonCode` |
| `course.delegation.expired` | Memberships (ACC-07), background job | Success | `Service` | required | `Account` (required) | `action`, `delegationId` |
| `access.denied` | Access, Team 3 (ACC-05), through the host adapter | Denied | `User` or `Anonymous` | optional, only from a trusted resource resolver | optional | `action`, `denialReason`, `policyId`, `policyVersion` |
| `audit.events.viewed` | Audit (AUD-06) | Success | `User` | required | `Course` (required) | `filterType`, `filterOutcome`, `filterFrom`, `filterTo`, `resultCount` |

`reasonCode` is only allowed when the outcome is `Denied` or `Failure`. An empty GUID is never a
valid `CourseId`.

The catalog is `AuditEventCatalog` in `Platform.Audit.Contracts`, so producers can see what each
type allows. The checks are done by the internal `AuditDraftValidator` in
`Platform.Audit.Application`, which the writer (story S-06) calls before storing an event.

### Detail value formats

| Key | Format |
|-|-|
| `reasonCode` | code: `^[a-z][a-z0-9_]{0,31}$`, for example `invalid_credentials`, `account_blocked`, `account_locked` (IDN-05: `auth.login` Failure while the account is temporarily locked), `forbidden`, `not_found` |
| `oldStatus`, `newStatus` | one of `active`, `blocked`, `archived` |
| `oldRole`, `newRole` | one of `none`, `student`, `teacher`, `assistant` |
| `action`, `filterType` | action or event type: `^[a-z][a-z0-9]*(\.[a-z][a-z0-9]*)+$`, at most 64 characters (same rule as `ActionId` in `Platform.Access`) |
| `denialReason` | one of `unauthenticated`, `account_not_active`, `unknown_action`, `no_policy`, `policy_denied`, `policy_error` |
| `policyId` | `^[a-z][a-z0-9_.-]{0,63}$` |
| `policyVersion` | `^v[0-9]{1,4}(\.[0-9]{1,4}){0,2}$` |
| `delegationId` | UUID |
| `filterOutcome` | one of `success`, `denied`, `failure` |
| `filterFrom`, `filterTo` | UTC timestamp, ISO 8601, ending with `Z` |
| `resultCount` | integer from 0 to 100000 |

## Limits

| Item | Limit |
|-|-|
| `EventType` | at most 64 characters, same pattern as `action` |
| Detail keys per event | at most 20 |
| Detail key | `^[a-z][a-zA-Z0-9]{0,31}$`, and it must be allowed for the event type |
| Detail value | its format above; at most 64 characters; no control characters |
| All details together | at most 2048 bytes after serialization to UTF-8 JSON |

Details are checked in this order: number of keys and total size first, so an oversized input is
refused before any per-key work; then, for each key, the forbidden-name check, the allowlist and
the value format.

The allowlist is the primary control. As an additional check, a key whose name contains
`password`, `passwd`, `secret`, `token`, `authorization`, `apikey`, `cookie` or `session`
(ignoring case) is rejected with `ForbiddenDetailKey` even if someone adds it to the catalog by
mistake. A rejected value is never copied into a log, exception message or HTTP response.

## Versioning

- The contract has a version (now `0.1`). Every stored event records the schema version of its
  type (now `1`).
- Adding a new event type needs a catalog entry, its detail schema and a test. It does not break
  existing producers.
- Removing or renaming a field, an enum value or an event type, or changing what an outcome
  means, is a breaking change even if no method signature changes. It needs a migration plan
  (OPS-05).

## Consumers

| Team / module | Events | Current state |
|-|-|-|
| Identity, Teams 1 and 2 (IDN-02, IDN-03, IDN-05) | `auth.*`, `account.status.changed` | Has its own `IAuditWriter` in `Platform.Api.Services.Identity`, which writes to `ILogger`. Needs an agreed adapter or a switch to this contract. |
| Access, Team 3 (ACC-05, ACC-08) | `access.denied` | Calls `IAccessAuditWriter`; today `NullAccessAuditWriter`. Connected by the host adapter (S-13). |
| Memberships and delegation (ACC-03, ACC-07) | `course.*` | Not on `main` yet. |
| Revision publishing (AUD-02) | not in this version | Can add its event type through a catalog change. |
| Audit query (AUD-06) | `audit.events.viewed` | Our own story S-14/S-15. |

Two interfaces named `IAuditWriter` exist in different namespaces. In `Program.cs` use an alias,
as is already done for the two `IAccessDecision` interfaces.

## Test double

Planned, not in this version: `FakeAuditWriter` will implement both writer interfaces, record
every draft and return a result that the test sets (`Recorded`, `Rejected` with a code, or
`Unavailable`). It will not validate like the real writer, so a test that passes with the fake
does not prove the contract rules.
Those are proven by the Audit module's own tests.

## How the contract is verified (test group T03)

Planned tests in `backend/Platform.Audit.Tests`, each with its negative case:

- unknown event type: `Rejected`, `UnknownEventType`, nothing stored;
- outcome not allowed for the type: `OutcomeNotAllowed`;
- missing or forbidden `CourseId`: `CourseRequired` / `CourseNotAllowed`;
- unknown detail key, forbidden key name, invalid value, too many keys, more than 2048 bytes;
- actor rules: `User` or `Service` without `Id`, `Anonymous` with `Id`: `InvalidContext`;
- every catalog entry accepts a valid example, so the catalog and its tests stay in sync.

## Residual risk

- `ITrustedAuditWriter` is a public interface, so the compiler cannot stop a module from
  passing a forged `AuditContext`. Mitigation: code review of every use, and only server
  adapters registered in composition.
- Append-only rules in the application do not protect against a database administrator or a
  fully compromised server process. Storage rights are handled in S-05/S-16.

## Open questions

| # | Question | Owner |
|-|-|-|
| 1 | Does Identity switch to this contract or keep its own interface behind an adapter? `ChangeAccountStatus` currently writes a free-text administrator reason into `Detail`, which conflicts with AUD-03; the proposal is a `reasonCode` instead. | Member 9 with Teams 1 and 2 (D1) |
| 2 | Shared transaction or transactional outbox for privileged changes. | Members 2, 3 and 9 with producers (D3) |
| 3 | Is `auth.login` with valid credentials on a blocked account recorded with a `User` actor? (Proposed: yes; the identity is confirmed, the login is refused.) | Members 3, 8 and 9 with Team 1 |
| 4 | Who sees events without a course (global or administrator view)? Proposed: nobody through the teacher API. | Members 5, 7 and 9 with the assistant (D4) |
| 5 | Is an incoming `X-Correlation-Id` header accepted, and in which format? | Member 10 with the AUD-04 owner |
| 6 | Retention, archive and anonymization rules. | Members 2, 7 and 9 with the ADM owners (D8) |

Agreements with the consumers and producers listed above are tracked in
[INTEGRATION.md](INTEGRATION.md) (story S-11).

## Change history

| Version | Date | Change |
|-|-|-|
| 0.1 | 2026-11-03 | Initial draft for review (S-02) |

## REV-05 integration (2026-11-06)

`AddAuditPersistence(connectionString)` registers the actual PostgreSQL store,
scoped `ITrustedAuditWriter` and `IAuditWriter`, and the singleton
`ITransactionalAuditWriter`. The ordinary writer has no invented ambient actor:
without a host-provided context it rejects that call. Trusted host producers use
an explicitly confirmed AuditContext. AddAudit() continues to provide correlation.

`ITransactionalAuditWriter.WriteAsync(draft, context, DbTransaction, ct)` validates
and appends using the supplied PostgreSQL transaction. The caller must commit
before reporting success. Audit schema and business mutation must share a database.
No internal Audit domain/store type is exposed to the producer. Failure is returned
as Rejected or Unavailable; RevisionReviews rolls its transaction back and returns 503.

The new successful event types are `revision.submitted`, `revision.returned` and
`revision.approved`. All require a User actor, a course, a Revision resource, and
`authorId` (UUID), `operationId` (UUID), `contentHash` (64 lowercase hex characters).
An approval/publication also requires `laboratoryId` (UUID) and `revisionNumber`
(integer 1..2147483647). Required fields are declared in AuditEventSchema.RequiredDetails
and enforced before insertion. EventId equals the committed review operation ID.
Retries are idempotent. Comments, snapshots, expected answers and secrets are not
accepted detail keys. Existing event schemas and historical schema-version-1 records
remain valid. This is a concrete local integration change for review, not a claim of
prior acceptance of the other proposals in this document.
