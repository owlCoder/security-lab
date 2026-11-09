# S-16: runtime protection and transaction evidence

Owner: Aleksandar Petrović, Team 12. Requirements: AUD-01, AUD-03; plan checks T05 and T06.
This branch starts from PR #113 commit `92cd7b8`, which includes Team 2's IDN-03 work in
PR #148. The shared transaction and session issuance locking belong to that implementation;
S-16 reuses them and extends their database-state tests.

## Database protection (T05)

The existing bootstrap script creates `securitylab_audit_runtime` with SELECT/INSERT only
on `audit.audit_events`. It is not the owner, a superuser, or a member of another role.
The permission fixture checks these properties, allowed reads/inserts, and direct SQL
denial of UPDATE, DELETE, TRUNCATE, DROP TABLE, schema/table DDL, migration-history reads,
disabling triggers and changing `session_replication_role`. Rejected statements are run
inside transactions that are rolled back; the original event is then read again.

`ProtectAuditHistory` adds an ALWAYS statement trigger rejecting UPDATE and DELETE with
SQLSTATE `55000`, even when the caller has table modification privileges. The function
uses a fixed search path and has no PUBLIC execution grant. INSERT/SELECT are unchanged.
The trigger tests use the table-owning test account, so they prove a second layer beyond
the runtime ACLs. Runtime denial tests expect `42501` from the ACLs instead.

TRUNCATE and DDL remain protected by privileges, not by that trigger. A table owner or
superuser can disable/drop protection or truncate data: these credentials are migration/
maintenance credentials and must not be used by the API. An authorized archive or restore
procedure must be agreed with the platform owner before production maintenance. S-16 does
not grant additional rights to Identity or other modules.

The migration adds objects only in the Audit schema. Its Down removes the trigger and
function without deleting events. The upgrade test migrates an isolated database back to
the initial schema, inserts an event, upgrades again and verifies both the preserved event
and the enabled ALWAYS trigger. Existing installations must apply the new migration using
their migration account; merely restarting Docker does not upgrade a populated volume.

## Shared transaction (T06)

The real `PUT /api/admin/accounts/{id}/status` endpoint calls Team 2's `ChangeAccountStatus`.
The Identity status update and session revocations share a transaction with
`PostgresTransactionalAuditWriter`. No additional demo table, outbox or parallel producer
is introduced.

`AccountStatusChangeIntegrationTests.AuditFailure_RollsBackStatusSessionRevocationAndAuditRow`
checks three failure points against a real PostgreSQL database:

- Audit reports Unavailable before writing.
- The real writer inserts an Audit row, visible inside the transaction, then the test
  adapter reports Unavailable.
- The real writer inserts an Audit row, then a real SQL error aborts the transaction.

Every case expects HTTP 503. Fresh service scopes/connections must see the account still
Active, its session unrevoked, and no `account.status.changed` row. The existing session
must still work. Existing positive tests prove that a successful block commits the status,
revocation and one Audit event, and the session issuance race tests prevent old tokens
from becoming valid after reactivation. Test adapters are registered only in the test host.

The Identity integration fixture uses a privileged disposable database account. It proves
atomicity, not production Identity permissions. The separate Audit runtime fixture proves
the restricted Audit role. For a shared transaction, the producer's database identity must
have its own required business permissions plus Audit INSERT/SELECT on the same database;
deployment grants remain the responsibility of the owning modules and platform owner.

## Reproduce

From the repository root with Docker running:

```powershell
pwsh ./scripts/audit/test-postgres.ps1
```

The runner builds the solution, uses an isolated PostgreSQL container, applies both Audit
migrations, bootstraps the restricted runtime role, runs the full solution and checks that
the Audit PostgreSQL fixtures actually executed. Identity integration tests use their own
Testcontainers database. The runner removes its container and restores its temporary
environment variables. TRX reports remain under ignored `TestResults/audit-s09-*`.

T13 backup/restore and final Team 1 login failure-policy confirmation are separate from
these S-16 checks; this change does not claim to complete them or the entire AUD-01 module.

## Local verification, 2026-11-09

`scripts/audit/test-postgres.ps1` completed with exit code 0. The Release build had no
warnings or errors. TRX totals: 1,685 passed, 0 failed, 12 skipped in other test projects.
Audit: 219 passed, none skipped. Identity: 280 passed, none skipped. Both trigger cases,
the initial-schema upgrade, all nine forbidden runtime SQL cases and all three failure
points of the status-change rollback test passed.

Evidence directory: `TestResults/audit-s09-845ee0b8ed6749f291e2821094f5188c` (local, ignored).
These results apply to this branch based on `92cd7b8`; repeat the runner after updating
the branch to the version of main that receives PR #113.
