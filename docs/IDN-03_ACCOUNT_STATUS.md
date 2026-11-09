# Project component: IDN-03, Account status management

**Team:** 2

**Student:** Igor Cvijić / PR 21/2023

**Specification requirements:** IDN-03 (primary), ADM-01, AUD-01, AUD-03, ACC-05, SEC-01, SEC-03, UC-05. Depends on IDN-02 (sessions), ADM-02 (database) and the Audit module (`ITransactionalAuditWriter`).

**User story:** As an administrator, I want blocked and archived accounts to be unable to sign in, so that unauthorized access is prevented.

An administrator sets the status of another account to `ACTIVE`, `BLOCKED` or `ARCHIVED`
(`PUT /api/admin/accounts/{id}/status`). The status is written to `identity.accounts`, the same table login reads,
so the next login and the next request of an existing session see it.

## Responsibilities and boundaries

* Owned by Team 2: the use-case `ChangeAccountStatus`, the store `EfAccountStatusStore`, the session check. Team 12 (Audit) owns the Audit contract; we only call it.
* One successful change is **one database transaction**: new status, revocation of all sessions (for `BLOCKED` and `ARCHIVED`) and the Audit record. If any step fails, nothing is stored.
* Refusals (unauthenticated, not an administrator, invalid status, self-change, unknown account, same status) change nothing and are recorded in Audit in their own short transaction. A failed Audit write does not change the answer to a refusal.
* If the Audit record of a successful change cannot be written, the change is rolled back and the endpoint returns `503 unavailable`.

|Contract|Implementation|
|-|-|
|`IIdentityContext`|`SessionIdentityContext` (server-side session, IDN-02)|
|`IAccessDecision`|`AdminOnlyAccessDecision`, administrator only, deny by default|
|`IAccountStatusStore`|`EfAccountStatusStore` over `PlatformIdentityDbContext` (`identity.accounts`, `identity.sessions`)|
|`ISessionManager`|`SessionManager` (single registration; revocation runs inside the status transaction)|
|`ITransactionalAuditWriter`|Audit module, receives the open `DbTransaction`; event type `account.status.changed`|

## Component architecture

```mermaid
flowchart LR
  Client[Admin client] -->|PUT status| Ctrl[AdminAccountsController]
  Ctrl --> UC[ChangeAccountStatus]
  UC --> Ctx[IIdentityContext]
  UC --> Acc[IAccessDecision]
  UC --> Store[IAccountStatusStore]
  UC --> Aud[ITransactionalAuditWriter]
  Store --> DB[(PostgreSQL: identity.accounts, identity.sessions)]
  Aud -->|same DbTransaction| AuditDB[(PostgreSQL: audit.audit_events)]
  Login[Login] --> Cred[ICredentialVerifier]
  Cred --> DB
  Mw[SessionCheckMiddleware] --> SM[SessionManager]
  SM --> DB
```

`identity` and `audit` schemas must be in the same database, otherwise the Audit record cannot join the
identity transaction. In local development both live in the `securitylab` database (see `docker-compose.yml`);
`Program.cs` migrates the audit schema into the identity database at startup in Development.

## Data flow diagram

```mermaid
flowchart LR
  subgraph untrusted[Untrusted zone]
    A[Administrator or other caller]
  end
  subgraph app[Application zone: server enforces every check]
    P1((1 Identify caller))
    P2((2 Authorize: administrator only))
    P3((3 Validate status value))
    P4((4 Lock account, apply status))
    P5((5 Revoke sessions))
    P6((6 Write Audit record))
  end
  subgraph data[Data zone, one PostgreSQL database]
    D1[(identity.accounts)]
    D2[(audit.audit_events)]
    D3[(identity.sessions)]
  end
  A -->|"id, status"| P1
  P1 --> P2 --> P3 --> P4
  P4 -->|"FOR UPDATE, write status"| D1
  P4 -->|Blocked or Archived| P5
  P5 -->|revoke all| D3
  P5 --> P6
  P6 -->|same transaction| D2
  P1 -->|401| A
  P2 -->|403| A
  P4 -->|"200 / 404 / 409 / 503"| A
```

Order of checks: authenticated, then administrator, then status value valid, then not self-change, then account exists, then not the same status.
Authorization comes before the account lookup on purpose, so a non-administrator cannot learn which accounts exist.

## Security requirements

* Server-side access checks: `IAccessDecision` runs inside the use-case, not only in the controller (ACC-05, SEC-01).
* Session check on every request (SEC-03): `SessionManager.ValidateAsync` refuses a revoked or expired session and also a session whose account is not `ACTIVE`. This is defense in depth only: it is not enough alone, because a session stored behind the revocation would work again after reactivation.
* Session issuing (SEC-03): `ISessionStore.CreateForActiveAccountAsync` locks the account row with `SELECT ... FOR SHARE` in the same transaction that stores the session. A login that passed its status check before a block either waits for the block and stores nothing (`AccountNotActiveException`, login answers like any refused login), or commits first and is then revoked by the block. No token can outlive a block, so reactivation never revives one.
* The status is accepted only by name (`ACTIVE`, `BLOCKED`, `ARCHIVED`, any letter case). Numbers such as `1` or `99` are rejected.
* Audit record (AUD-01, AUD-03): actor, event type, resource, time, outcome and correlation ID, details only `oldStatus`/`newStatus` or a `reasonCode`. No free text, tokens, cookies or session ids. The `reason` field of the request body is accepted for compatibility but **not stored**, because the Audit contract allows no free text.
* An administrator cannot change their own status, to avoid locking out the last administrator.
* Concurrent changes of the same account are serialized with `SELECT ... FOR UPDATE`.

|Misuse|Control|Test|
|-|-|-|
|Student or non-admin blocks an account|403, nothing changes, denial audited|`Student_IsForbidden_NothingChanges_DenialIsAudited`, `Student_CannotChangeStatus_NothingChanges_DenialIsAudited` (integration)|
|Request without a caller|401, nothing changes|`Anonymous_IsUnauthenticated_NothingChanges_DenialIsAuditedWithoutActor`|
|Invalid or numeric status|strict parsing, 400|`InvalidStatusValue_IsRejected_NothingChanges` (7 cases)|
|Unknown account|404, failure audited|`UnknownAccount_ReturnsNotFound_AndIsAudited`, `UnknownAccount_ReturnsNotFound_AndLeavesNoOtherRows`|
|Administrator blocks themselves|409, audited|`Admin_CannotChangeOwnStatus`|
|Same status again|409, sessions stay|`SameStatus_IsInvalidTransition_SessionsStay`|
|Blocked account keeps a session|sessions revoked in the same transaction|`Block_RevokesActiveSession_RefusesNextRequestAndLogin_AndRecordsAudit`, `Archive_RevokesAllSessionsOfTheAccount`|
|Blocked account signs in|login reads `identity.accounts`|same integration test (login returns 401)|
|Session created behind the revocation|refused at issue (row lock), and status checked on every request|`LoginThatStartedBeforeTheBlock_IsRefusedAtIssue_SoReactivationCannotRevive_ItsToken`, `LoginsRacingWithABlock_NeverLeaveATokenThatWorksAfterReactivation`, `SessionOfAccountBlockedBehindTheRevocation_IsStillRefused`, `AccountBlockedAfterTheStatusCheck_IsRefused_LikeAnyBlockedLogin_AndNoSuccessIsAudited`|
|Audit unavailable|whole change rolled back, 503|`AuditNotRecorded_RollsBackStatusAndSessions`, `AuditUnavailable_RollsBackStatusAndSessionRevocation`|
|Database failure|nothing stored, 503|`DatabaseFailure_ReturnsUnavailable_AndStoresNothing`|
|Secrets or session ids in the Audit record|details limited to status names|`AuditRecord_HasNoFreeTextOrIdentifiersOfSessions`|
|Reactivation wrongly revokes sessions|revoke only for Blocked and Archived|`Admin_ActivatesBlockedAccount_DoesNotTouchSessions`|

## Evidence of work

* Branch: `fix/idn-03-persistent-status-change`.
* Changed: `Services/Identity/ChangeAccountStatus.cs`, `EfAccountStatusStore.cs`, `SessionManager.cs`; `Interfaces/Identity/IAccountStatusStore.cs`; `Controllers/Identity/AdminAccountsController.cs`; `Program.cs`. Removed: `IAccountRepository`, `InMemoryAccountRepository`, the duplicate `ISessionManager` registration.
* Tests: `Platform.Identity.Tests/Services/ChangeAccountStatusTests.cs` (unit, 23 cases) and `Platform.Identity.Tests/Integration/AccountStatusChangeIntegrationTests.cs` (real PostgreSQL, 6 cases).
* Run (PowerShell, repository root): `dotnet test backend\Platform.Identity.Tests --filter "Category=IDN-03"`.

## Implementation status

|Acceptance criterion|Status|
|-|-|
|Administrator can set `ACTIVE`, `BLOCKED` or `ARCHIVED`|Done|
|`BLOCKED` and `ARCHIVED` accounts cannot log in|Done, status stored where login reads it (integration test)|
|System stores the new status in the database|Done, `identity.accounts`|
|Only an authorized administrator can change status|Done, negative tests|
|The change is recorded|Done, persistent Audit record in the same transaction|
|Blocking revokes all sessions at once|Done, in the same transaction; next request with an old session returns 401|

## Known limitations and residual risk

* Deployment must create the `audit` schema in the identity database (`AuditPersistenceExtensions.MigrateAuditDatabaseAsync`); the web host migrates it only in Development. If the database user used by the identity module cannot write to `audit`, every status change returns `503`.
* `identity` and `audit` must stay in one database. Moving Audit to a separate database requires an outbox or a different integration.
* The `reason` text is not stored (see above). If a reason is required, ask Team 12 for an allowed structured field.
* Any status can change to any other different status, including `ARCHIVED` back to `ACTIVE`. This is a conscious simplification.
* No rate limiting on the administrative endpoint (SEC-04).
* Selective revocation and listing of own sessions (IDN-07) is the next task; it reuses `SessionManager.RevokeSession` and `ListForUser`.
