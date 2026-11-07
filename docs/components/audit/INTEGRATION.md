# Audit module: agreements with other teams (S-11)

**Story:** S-11, owner member 9, review member 10, extra reviewer member 1.
**Requirements:** AUD-01, AUD-06, with AUD-03, AUD-04, ACC-05, ACC-08, IDN-02, IDN-03, IDN-05.
**Status:** proposals. Nothing in this file is accepted until the row in the
[status table](#status-of-each-agreement) links a confirmation from the owner (a reviewed pull
request, a review comment or a written answer). Until then the temporary rule applies.

This file collects what the Audit component needs from other teams before S-12, S-13, S-14 and
S-16 start, what we propose, and what we found in the code. It feeds the open decisions D1, D2,
D3 and D6 in [DECISIONS.md](DECISIONS.md) and the contract in [CONTRACTS.md](CONTRACTS.md).

## Counterparts

| Counterpart | Requirements | What we need from them | Contact in Team 12 |
|-|-|-|-|
| Team 3, Access | ACC-05, ACC-08 | DI lifetimes, a public way to register an action and its policy, behaviour when the audit writer fails, mapping of denial reasons | Members 9, 1 and 5 |
| Team 1, Identity login | IDN-02 | Where `auth.login` and `auth.logout` are written, which actor is used, behaviour when the audit write fails | Members 9 and 3 |
| Team 2, Identity account status | IDN-03 | Moving `account.status.change` to the shared contract, reason codes instead of free text, transaction | Members 9, 3 and 2 |
| IDN-05 owner (rate limiting) | IDN-05 | Whether and how often `auth.login.throttled` is written | Member 9 |
| Membership and delegation owner | ACC-03, ACC-07 | Who owns them; a reader for current membership and delegation (AUD-06); producing `course.*` events | Members 9 and 5 |
| Owners of AUD-02..05, AUD-07 and ADM-01, ADM-05, ADM-07 | AUD, ADM | Who owns them; correlation (AUD-04); retention and archive | Members 9, 10, 2 and 7 |
| Course assistant | | Events without a course, administrator view, KT1/KT2 dates | Member 1 (dates), member 9 (scope) |

The owner of ACC-03 and ACC-07 and the owners of AUD-02..05, AUD-07 and ADM-01/05/07 are not
listed in [PROJECT_COMPONENTS.md](../../PROJECT_COMPONENTS.md). Finding them is the first step of
agreements S11-07 and S11-08.

## What the code shows today

Checked on `main`, commit `0a4f3d1`. These are facts that the proposals below depend on.

| # | Finding | Where | Why it matters |
|-|-|-|-|
| F1 | `AddAccessControl()` registers `IAccessDecision` and `IAccessAuditWriter` as singletons. | `Platform.Access/Composition/AccessServiceCollectionExtensions.cs` | A writer that uses a scoped `DbContext` cannot be injected into a singleton decision (captive dependency). |
| F2 | `DefaultDenyAccessDecision.DecideAsync` calls `auditWriter.WriteAsync` after the decision and outside any `try`/`catch`. The request's cancellation token is passed to the writer. | `Platform.Access/Application/Services/DefaultDenyAccessDecision.cs` | An exception from the writer replaces the decision with an exception. A cancelled request can drop the audit record of a denial. |
| F3 | `IActionCatalog` and `IAccessPolicyRegistry` only read. `Register` exists only on `InMemoryActionCatalog` and `InMemoryAccessPolicyRegistry`. Identity resolves these concrete classes after `Build()` and calls `Register`. | `Platform.Identity/Composition/IdentityAccessRegistration.cs` | Other modules depend on Access infrastructure classes. Registration happens after the container is built, so a duplicate is found only when that code runs. |
| F4 | When a policy returns `false`, the decision is `Deny` with reason `PolicyError`. The same reason is used when the policy throws. | `DefaultDenyAccessDecision.cs` | An `access.denied` record cannot tell a normal refusal from a broken policy. |
| F5 | `SubjectRef.Id` is a `string` (the demo uses `"demo-student"`). `AuditActor.Id` is a `Guid?`. | `Platform.Access/Domain/Models/SubjectRef.cs`, `Platform.Audit/Contracts/AuditActor.cs` | The forwarder needs a rule for subjects whose ID is not an account GUID. |
| F6 | Identity has its own `IAuditWriter` (`void Write(AuditEvent)`), registered as the singleton `LoggerAuditWriter`, which writes to `ILogger`. The caller sets actor, time, outcome as a string and a free-text detail. | `Platform.Api/Services/Identity/IAuditWriter.cs`, `Adapters.cs` | Conflicts with decision A3 (actor and time come from the server, not the caller) and with AUD-03 (no free text). A log line is not a guaranteed audit record. |
| F7 | `ChangeAccountStatus` writes `account.status.change` (catalog: `account.status.changed`) after `accounts.Save`, with detail `Old->New reason=<free text up to 200 characters>`. | `Platform.Api/Services/Identity/ChangeAccountStatus.cs` | Name mismatch, free text, and no transaction between the change and its record. |
| F8 | `LoginAccount` writes no audit record. `SessionValidationMiddleware` writes `session.validation`, which is not in the catalog. | `LoginAccount.cs`, `Middlewares/SessionValidationMiddleware.cs` | AUD-01 requires a record of login. A new event type needs a catalog change (decision A4). |
| F9 | Correlation IDs come from different places: `HttpContext.TraceIdentifier` in the session middleware, `Guid.NewGuid()` in the Access demo endpoint. | `Program.cs`, `SessionValidationMiddleware.cs` | The same request cannot be followed from the HTTP call to the decision and the audit record (AUD-04). |
| F10 | `Platform.Api` contains three interfaces named `IAccessDecision` (Access, Identity, Laboratories) and several `IIdentityContext`. | `Program.cs` uses aliases | The audit actor must come from one agreed identity source (D10). |
| F11 | Team 12 has no row in `PROJECT_COMPONENTS.md`. | `docs/PROJECT_COMPONENTS.md` | Added by this story. |

## Proposals

### S11-01 DI lifetimes (Team 3, members 1 and 5)

- When the audit forwarder (S-13) replaces `NullAccessAuditWriter`, `IAccessDecision` and
  `IAccessAuditWriter` become **scoped together**, in `Platform.Access/Composition`, reviewed by
  Team 3.
- `IActionCatalog` and `IAccessPolicyRegistry` stay singletons. They are filled once before the
  first request and hold no request data.
- A policy that needs membership or the current identity is resolved per request (scoped or
  through a factory), never captured in a singleton.
- Proof: `CompositionScopeTests` and `HostStartupScopeTests` already build the container with
  `ValidateScopes` and `ValidateOnBuild`. S-13 adds a case with the real forwarder.

### S11-02 Replacing `NullAccessAuditWriter` explicitly (Team 3, member 1)

- Order in `Program.cs`: `AddAccessControl()`, then `AddAudit()`, then a host method
  `AddAccessAuditForwarding()` (name is a proposal) that **replaces** the registration with
  `services.Replace(...)`. No reliance on "last registration wins".
- A test checks that exactly one `IAccessAuditWriter` is registered and that it is the forwarder,
  not `NullAccessAuditWriter`, outside the `Test` environment.
- `Platform.Access` keeps no reference to `Platform.Audit`. The forwarder lives in the host or in
  `Platform.Audit/Infrastructure` and implements `IAccessAuditWriter`.

### S11-03 Public registration of actions and policies (Team 3, member 5)

Finding F3. Proposal for Team 3 to choose from:

```csharp
// in Platform.Access.Composition, called before Build()
services.AddAccessAction("audit.events.read", sp => new CourseAuditReadPolicy(...));
```

- Registration happens during composition, once per action. A duplicate action or a missing
  policy fails at startup with a clear message.
- Modules no longer resolve `InMemoryActionCatalog` or `InMemoryAccessPolicyRegistry`. Existing
  Identity registration moves to the new method in the same or a follow-up pull request,
  reviewed by Identity.
- The Audit component registers `audit.events.read` with resource `Course/{courseId}` (S-08).
  We do not create an attempt or other fake resource to pass a policy.

### S11-04 What happens when the audit write fails inside an access decision (Team 3, members 1 and 9)

Finding F2. Proposal:

1. **The forwarder does not throw for expected failures.** It calls `ITrustedAuditWriter`,
   which returns `Unavailable` instead of throwing. On `Unavailable` or `Rejected` it raises a
   technical alarm (log with event type, status and correlation ID only, no details) and returns.
2. **Team 3 adds a guard** around `auditWriter.WriteAsync` in `DefaultDenyAccessDecision`, as
   defence in depth: any exception other than cancellation is caught, an alarm is logged, and the
   already computed decision is returned. Changing `Platform.Access` is their decision.
3. **Deny stays deny.** A failed audit write never changes a `Deny` into an `Allow`, and never
   turns a decision into a 500 that the caller might handle as success.
4. **Allow decisions are not forwarded.** The catalog has only `access.denied`. A privileged
   change that was allowed is recorded by its own producer under S11-06, not by the forwarder.
5. **Cancellation:** the forwarder writes a denial with `CancellationToken.None` and its own short
   timeout, so a client that disconnects does not erase the trace of the denial.

Required tests (S-13, group T10): writer throws, writer returns `Unavailable`, request cancelled
after the decision; in every case the caller receives the computed decision and exactly one
or zero records exist, never two.

### S11-05 Mapping a decision to `access.denied` (Team 3, members 9 and 8)

| Access field | Audit field | Rule |
|-|-|-|
| `Subject = null` | actor `Anonymous` | |
| `Subject.Id` that is an account GUID | actor `User` with that ID | |
| `Subject.Id` that is not a GUID (finding F5) | none | Proposal: Access agrees that `SubjectRef.Id` is the account GUID. Until then the forwarder writes nothing for such subjects and raises an alarm; it does not invent an actor. |
| `Action` | detail `action` | Copied only if it matches the `ActionId` format. Otherwise the detail is left out and an alarm is raised; the raw value is never stored or logged. |
| `DenialReason` | detail `denialReason` | `Unauthenticated` → `unauthenticated`, `AccountNotActive` → `account_not_active`, `UnknownAction` → `unknown_action`, `NoPolicy` → `no_policy`, `PolicyError` → `policy_error`. |
| `PolicyId`, `PolicyVersion` | details `policyId`, `policyVersion` | Only when present and valid. |
| `Resource` of type `Course` | `CourseId` | Only from a trusted resource resolver, never from `Details` or client input. Other resource types: no course. |
| `CorrelationId` | `AuditContext.CorrelationId` | Must be the request's server correlation ID (S11-09). |

Question for Team 3 (finding F4): can a normal refusal by a policy get its own reason, for
example `PolicyDenied`, separate from `PolicyError`? If yes, the catalog adds `policy_denied` to
`denialReason` as a compatible change.

### S11-06 Transaction or outbox for privileged changes (D3; members 2, 3, 9 with producers)

- **Proposal for KT1 (demo producer, S-12):** variant A, one database transaction. The business
  change and the audit `INSERT` are committed together on the same PostgreSQL connection. If
  either fails, both are rolled back and the producer returns a generic technical error.
- **For KT2:** variant B (transactional outbox) is considered only for a producer that cannot
  share the transaction (another database or an external identity provider).
- **Identity producers today** (finding F7) save to an in-memory repository and write to a log.
  No claim is made that their changes are guaranteed to be audited until they use the agreed
  pattern.
- Proof required (S-16, group T06): the state of rights is read from a new database session
  after a forced audit failure, not only the type of the exception.

### S11-07 Identity events (Teams 1 and 2, IDN-05 owner; members 9 and 3)

Proposal: Identity calls the shared contract (`IAuditWriter` for normal requests,
`ITrustedAuditWriter` for login before the session exists) instead of its own `IAuditWriter`. An
adapter that copies free text from the old interface is not acceptable (AUD-03).

`account.status.changed` (IDN-03), from `ChangeAccountStatus`:

| `ChangeStatusOutcome` | Audit outcome | Actor | `reasonCode` |
|-|-|-|-|
| `Success` | Success | `User` (administrator) | none; `oldStatus`, `newStatus` |
| `Unauthenticated` | Denied | `Anonymous` | `unauthenticated` |
| `Forbidden` | Denied | `User` | `forbidden` |
| `InvalidStatus` | Failure | `User` | `invalid_status` |
| `NotFound` | Failure | `User` | `not_found` |
| `SelfChangeNotAllowed` | Failure | `User` | `self_change_not_allowed` |
| `InvalidTransition` | Failure | `User` | `invalid_transition` |

The administrator's free-text reason is not stored (D12). If a reason is required, Team 2 and the
Audit component agree a short list of codes.

`auth.login` (IDN-02), from `LoginAccount`, which writes nothing today (finding F8):

| `LoginOutcome` | Audit outcome | Actor | `reasonCode` |
|-|-|-|-|
| `Success` | Success | `User`, through `ITrustedAuditWriter` after the credentials are verified | none |
| `InvalidCredentials` | Failure | `Anonymous` | `invalid_credentials` |
| `AccountBlocked` | Failure | `User` (open question 3 in CONTRACTS.md) | `account_blocked` |
| `AccountArchived` | Failure | `User` (same question) | `account_archived` |
| `Unavailable` | Failure | `Anonymous` | `unavailable` |

- The typed email is never stored, even hashed.
- `auth.logout`: the actor is captured before the session is closed.
- `auth.login.throttled` (IDN-05): one record per refused request is the default. If the volume
  is too high, the IDN-05 owner and member 9 agree a documented limit; no silent sampling. The
  client IP address is not stored without a separate data-minimization decision.
- `session.validation` is not in the catalog. Either it is added with a schema and a test, or it
  stays a technical log entry. Decision for the session owner and member 8.
- When the audit write of a login fails, the behaviour of authentication is decided by the
  Identity team. The Audit component asks only that the failure is visible (alarm) and that the
  login is never reported as audited when it was not.

### S11-08 Membership and delegation (ACC-03, ACC-07 owner; members 9 and 5)

- First, find the owner. Until then S-08 uses test doubles that are clearly marked as such.
- AUD-06 needs a server-side reader that answers, for the current request, whether an account is
  an active teacher of course K or has a valid delegation of `audit.events.read` in K. No cache
  across requests, so a revoked right applies to the next request.
- The reader returns `Allowed`, `Denied`, `NotFound` or `Unavailable`. `Unavailable` never gives
  access.
- Membership changes produce `course.member.role.changed` and `course.delegation.*` under the
  transaction rule of S11-06. Delegation expiry is written by a background job with a `Service`
  actor.

### S11-09 Correlation (AUD-04 owner; member 10)

- One server-generated correlation ID per request, kept in a scoped context set by middleware at
  the start of the pipeline. Access requests, audit records and structured logs read it from
  there (finding F9).
- An incoming `X-Correlation-Id` is ignored until its format and length are agreed (open
  question 5 in CONTRACTS.md).
- Before adding a second middleware, member 10 checks with the AUD-04 owner who owns it.

### S11-10 Owners, retention and the course view (AUD/ADM owners and the assistant)

- Ask who owns AUD-02..05, AUD-07, ADM-01, ADM-05 and ADM-07, and whether any of them already
  plans its own audit store. There must be one audit store and one contract.
- Retention, archive and anonymization (D8): no public delete or export until agreed.
- Events without a course and an administrator view (D4): proposal to the assistant is that the
  teacher API shows only events of course K, and that events without a course are not shown
  there.

## Status of each agreement

A row becomes **Accepted** only with a link. "Sent" means the request was delivered; it is not
acceptance.

| # | Topic | Counterpart | Decision | Status | Confirmation | Temporary rule |
|-|-|-|-|-|-|-|
| S11-01 | DI lifetimes | Team 3 | D2 | Proposed | | No claim of a finished Access integration. |
| S11-02 | Explicit replacement of `NullAccessAuditWriter` | Team 3 | D2 | Proposed | | `NullAccessAuditWriter` stays; nothing is forwarded. |
| S11-03 | Public registration of actions and policies | Team 3 | D2 | Proposed | | `audit.events.read` is not registered; the AUD-06 route stays unreachable. |
| S11-04 | Audit failure inside an access decision | Team 3 | D2, D6 | Proposed | | Forwarder not connected. |
| S11-05 | Mapping to `access.denied`, `PolicyDenied` reason | Team 3 | D2 | Proposed | | Mapping is tested only against the forwarder's own tests. |
| S11-06 | Transaction or outbox | Producers, members 2 and 3 | D3 | Proposed: A for KT1 | | Demo producer only. |
| S11-07 | Identity events | Teams 1, 2, IDN-05 owner | D1, D10, D12 | Proposed | | Identity keeps its log writer; it is not counted as AUD-01 coverage. |
| S11-08 | Membership and delegation reader | ACC-03/07 owner (unknown) | D1 | Owner not found | | Test doubles only, labelled as such. |
| S11-09 | Correlation context | AUD-04 owner | | Proposed | | Each module keeps its current ID; no claim of end-to-end correlation. |
| S11-10 | Owners, retention, course view | AUD/ADM owners, assistant | D4, D8 | Proposed | | Course view only; no delete or export. |
| S11-11 | Team 12 row in `PROJECT_COMPONENTS.md` | Repository maintainer | | In this pull request | | |

## Requests to send

Short texts that member 9 can send. Each answer is linked in the status table.

**Team 3 (Access):**
> Tim 12 (AUD-01/AUD-06) povezuje `IAccessAuditWriter` sa audit modulom (S-13). Predlog je u
> `docs/components/audit/INTEGRATION.md`, tačke S11-01 do S11-05. Molimo potvrdu ili izmenu za:
> (1) `IAccessDecision` i `IAccessAuditWriter` postaju scoped zajedno kada stigne pravi writer;
> (2) javni način registracije akcije i politike pre `Build()` umesto `InMemory*` klasa;
> (3) zaštitu oko `auditWriter.WriteAsync` u `DefaultDenyAccessDecision` tako da greška writera ne
> zameni već izračunatu odluku; (4) da je `SubjectRef.Id` GUID naloga; (5) poseban razlog za
> uobičajeno odbijanje politike, odvojen od `PolicyError`. Izmene u `Platform.Access` radimo samo
> uz vaš review.

**Teams 1 and 2 (Identity) and the IDN-05 owner:**
> Za AUD-01 predlažemo da prijava, odjava i promena statusa naloga koriste zajednički audit ugovor
> (`docs/components/audit/CONTRACTS.md`), sa mapiranjem ishoda u `INTEGRATION.md`, tačka S11-07.
> Konkretno: `account.status.changed` umesto `account.status.change`, kod razloga umesto slobodnog
> teksta administratora, zapis prijave u `LoginAccount`, bez upisanog emaila. Molimo potvrdu ili
> izmenu, i odluku šta prijava radi kada audit upis ne uspe.

**Owners of ACC-03/ACC-07 and AUD/ADM (through the assistant if unknown):**
> Ko je vlasnik ACC-03, ACC-07, AUD-02..05, AUD-07 i ADM-01/05/07? Audit modul tima 12 treba
> čitač aktuelnog članstva i delegacije za AUD-06 i dogovor o jednom audit skladištu, korelaciji i
> zadržavanju podataka (`INTEGRATION.md`, S11-08 do S11-10).

## Change history

| Date | Change |
|-|-|
| 2026-11-03 | Initial findings, proposals and status table (S-11) |
