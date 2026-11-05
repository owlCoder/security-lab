# Audit integration tests (S-09)

From the repository root, with the .NET 10 SDK, PowerShell 7.4+ and a running Docker
engine in Linux containers mode:

```powershell
./scripts/audit/test-postgres.ps1
```

The same script runs in the backend CI job with `-NoBuild` after the Release build.
It starts a fresh PostgreSQL 17 container on a random loopback port, applies Audit
migrations with the bootstrap account, and uses the S-05 SQL script to grant the
separate runtime login SELECT/INSERT. Passwords are randomly generated per run; no
`.env`, development database, GitHub secrets or production credentials are required.
The container and its anonymous data volume are removed in the script's cleanup.

The solution run writes TRX under `TestResults/audit-s09-*`. CI uploads the results
even if a test fails. The script fails if any of the three PostgreSQL fixtures has
no passing tests in the current run or if a reported PostgreSQL case was skipped.
Plain `dotnet test` without the test connections still runs the unit and production
isolation tests and explicitly skips database-dependent cases.

| Fixture | Evidence |
| --- | --- |
| `PostgresAuditEventStoreTests` | S-05 migrations, persistence, retries and Audit-only rollback in a separate temporary database. |
| `AuditRuntimePermissionsTests` | Actual restricted login can append/read but cannot modify existing rows, truncate the table, perform Audit DDL or read migration history. |
| `AuditHostPostgresTests` | HTTP through `WebApplicationFactory<Program>`, the real Audit writer and restricted PostgreSQL store. Stored actor/time/correlation are server-supplied. Forbidden details are rejected without echo or persistence. |
| `AuditProductionIsolationTests` | The test probe and Access demo are absent in Production; an `X-Stub-User` header on a protected route receives 401. |

Only the test factory registers the probe and synthetic Audit context. The probe
is not an AUD-06 endpoint, and its synthetic event does not perform an account
change. This is not evidence of real Identity/Access integration, course
authorization, or business-change rollback; those belong to S-12 through S-17.

For manual tests against a dedicated non-production PostgreSQL instance, see
[Audit persistence](PERSISTENCE.md) for the migration and test connection settings.
