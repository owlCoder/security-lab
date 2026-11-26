# Development workflow

1. Update `main` and create a short-lived branch for the component or task.
2. Reference the relevant requirement IDs from the project specification.
3. Implement clear module responsibilities, server-side checks, and validation.
4. Update the component architecture and data flow diagrams.
5. Include a negative test for access boundaries or invalid input where applicable.
6. Open a pull request describing verification and dependencies.
7. Do not rewrite `main`, commit secrets, or remove another team's work without agreement.

Build the initial API with `dotnet build backend/Platform.Api/Platform.Api.csproj`.

## Local identity database

1. Copy `.env.example` to `.env`. The file is git-ignored; put your own passwords there, never in `.env.example`.
2. Start PostgreSQL: `docker compose up -d db`
3. Start the API: `dotnet run --project backend/Platform.Api --urls http://localhost:5080`

In the Development environment the API applies pending identity migrations (`PlatformIdentityDbContext`, login and sessions) when it starts, then seeds the development accounts. The account registration context from IDN-01 (`AccountDbContext`) is not migrated on startup. If the database is not reachable, the API still starts and logs a warning; start the database and restart the API. Migrations are never applied on startup outside Development.

To apply migrations without starting the API, run `dotnet tool restore` once, then:

`dotnet ef database update --project backend/Platform.Identity --startup-project backend/Platform.Identity --context PlatformIdentityDbContext`

`Platform.Identity` has two contexts, so every `dotnet ef` command needs `--context`. `AccountDbContext` (IDN-01) has no design-time factory and uses `--startup-project backend/Platform.Api`; its migrations are in `Migrations/AccountDb`.

Docker Compose, the API in the Development environment, and `dotnet ef` all read the same `.env` from the repository root. `ConnectionStrings__Identity` is built from the `POSTGRES_*` values, so a password changes in one place. Keep `;` out of `POSTGRES_PASSWORD`, because it ends a value in the connection string. If you change the password after the database was created, the old one stays in the Docker volume until you recreate it with `docker compose down -v`, which deletes the local data.

Precedence: `dotnet user-secrets`, if you still have any, override `.env`. Delete an old value with `dotnet user-secrets remove "ConnectionStrings:Identity" --project backend/Platform.Identity`. A variable set in the real environment also wins over `.env`.

After changing the identity model, add a migration:

`dotnet ef migrations add <Name> --project backend/Platform.Identity --startup-project backend/Platform.Identity --context PlatformIdentityDbContext --output-dir Migrations`

The `Migrations_MatchTheModel` test fails when a model change has no migration. The API starts without a connection string, so the database steps are only needed for work that uses the identity database.

## Local Audit database required by Identity login

Successful login is fail-closed when its mandatory Audit record cannot be persisted. A local API
therefore needs the Audit schema and the restricted runtime role as well as the Identity schema.
The API never runs Audit migrations at startup and must not use the bootstrap account for runtime
writes.

After copying `.env.example` to the ignored `.env`, replace
`AUDIT_RUNTIME_PASSWORD` with at least 32 letters, digits, underscores or hyphens. Then, from the
repository root, restore the pinned EF tool and generate the idempotent Audit migration script:

```powershell
dotnet tool restore
$env:AUDIT_MIGRATION_CONNECTION_STRING = 'Host=127.0.0.1;Port=5432;Database=securitylab'
New-Item -ItemType Directory -Force TestResults | Out-Null
dotnet ef migrations script --idempotent --project backend/Platform.Audit/Platform.Audit.csproj --startup-project backend/Platform.Audit/Platform.Audit.csproj --context AuditDbContext --output TestResults/audit-initial-migration.sql
Remove-Item Env:AUDIT_MIGRATION_CONNECTION_STRING
```

Stop if generation fails. Review the generated SQL, then apply it through the local bootstrap
account and provision the restricted runtime role:

```powershell
Get-Content -Raw TestResults/audit-initial-migration.sql | docker compose --env-file .env exec -T db sh -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
./scripts/audit/bootstrap-runtime.ps1
```

The checked-in `.env.example` builds `ConnectionStrings__Audit` from the ignored runtime password.
Restart the API after provisioning so it reads that setting. A missing or invalid Audit connection
causes successful credential verification to revoke the new session and return the generic
`login_unavailable` response; no usable token is returned. See
[`components/audit/PERSISTENCE.md`](components/audit/PERSISTENCE.md) for the privilege model,
verification tests and production guidance.

## Reset the local identity database

Use the reset script when you want a clean identity schema, for example after testing with throwaway data or when your local schema no longer matches the migrations. It deletes all identity data in your local database and cannot be undone.

Complete steps 1 to 4 above first, then run the script from the repository root:

```powershell
.\scripts\reset-identity-db.ps1
```

Type `reset` in lowercase to confirm. Any other answer cancels the script and changes nothing. To skip the question, add `-Force`:

```powershell
.\scripts\reset-identity-db.ps1 -Force
```

If PowerShell reports that running scripts is disabled, use this command instead. It applies only to this run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\reset-identity-db.ps1
```

The script does the following:

1. Checks that the `db` service from Docker Compose is running.
2. Restores the EF Core tool and builds the identity module. If this fails, nothing is changed.
3. Drops the `identity` schema, including its migration history.
4. Applies all identity migrations again.

Keep the following in mind:

- Only the `identity` schema is changed. Do not use `dotnet ef database drop` for a reset, because it deletes the whole database, including the tables of other teams.
- The schema is dropped in the `db` service, while migrations use `ConnectionStrings__Identity` from `.env`, or your `ConnectionStrings:Identity` user secret if you have one. Keep it pointed at that local database.
- A red `Failed executing DbCommand` message for `__EFMigrationsHistory` is expected. EF Core checks for that table before recreating it. The reset succeeded if the script ends with `Done. Schema 'identity' was recreated from migrations.`
- If migrations fail after the drop, the script reports it and the schema is left incomplete. Fix the problem and run the script again.

## Development accounts (US-09)

In the Development environment the API seeds four accounts in `identity.accounts` when both the identity connection string and seed password are set. All use the reserved `example.com` domain:

| Email | Role | Status |
|---|---|---|
| `admin@example.com` | Administrator | Active |
| `student@example.com` | Student | Active |
| `teacher@example.com` | Teacher | Active |
| `blocked@example.com` | Student | Blocked |

Set `Seed__DevelopmentPassword` in your local, git-ignored `.env` (or user-secrets). Replace the placeholder from `.env.example` locally; never commit the real value. The password is limited to 128 characters and is stored only as a BCrypt hash.

- Seeding is idempotent: missing accounts are added; seed account status and role are reapplied on every start. The seed accounts' password hash is replaced when it is missing or does not match the current seed password.
- Without the identity connection string or seed password, seeding is skipped and the API still starts.
- Seeding never runs outside the Development environment.
- To change the password, change `Seed__DevelopmentPassword` in `.env` and start the API again.


For tests, `TestUsers.Create(accounts, role, status, password)` creates a user in the in-memory account repository. `TestUsers.CreateAsync(context, role, status, password)` saves a user in the identity database used by credential verification and login tests. Without a password either helper creates an account with no hash, which cannot log in.
