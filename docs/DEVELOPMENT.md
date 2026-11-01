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
3. Restore the EF Core tool and the packages: `dotnet tool restore`, then `dotnet restore backend/SecurityLab.slnx`
4. Apply migrations:

   `dotnet ef database update --project backend/Platform.Identity --startup-project backend/Platform.Identity`

Docker Compose, the API in the Development environment, and `dotnet ef` all read the same `.env` from the repository root. `ConnectionStrings__Identity` is built from the `POSTGRES_*` values, so a password changes in one place. Keep `;` out of `POSTGRES_PASSWORD`, because it ends a value in the connection string. If you change the password after the database was created, the old one stays in the Docker volume until you recreate it with `docker compose down -v`, which deletes the local data.

Precedence: `dotnet user-secrets`, if you still have any, override `.env`. Delete an old value with `dotnet user-secrets remove "ConnectionStrings:Identity" --project backend/Platform.Identity`. A variable set in the real environment also wins over `.env`.

After changing the identity model, add a migration:

`dotnet ef migrations add <Name> --project backend/Platform.Identity --startup-project backend/Platform.Identity --output-dir Migrations`

The `Migrations_MatchTheModel` test fails when a model change has no migration. The API starts without a connection string, so the database steps are only needed for work that uses the identity database.

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

In the Development environment, the API seeds four accounts (admin, teacher, student, and blocked) when both the identity connection string and the seed password are set. The password is `Seed__DevelopmentPassword` in your local `.env`, at most 128 characters.

All four accounts share this password. The seeder is idempotent: it adds missing accounts and sets a password only on accounts that do not already have one. To change the password, delete the accounts or run `scripts/reset-identity-db.ps1`, then restart the API. Roles are not stored yet because the account model has no role.

## Development accounts (US-09)

In the Development environment the API seeds four accounts in `identity.accounts` when both the identity connection string and a seed password are set: `admin@example.com`, `teacher@example.com`, `student@example.com` and `blocked@example.com` (status Blocked). All use the reserved `example.com` domain.

The password is `Seed__DevelopmentPassword` in your local `.env`. Only a placeholder is in the repository, in `.env.example`.

Behaviour:

- All four accounts share this password. It is stored only as a BCrypt hash.
- The seeder is idempotent: it adds missing accounts and sets a password only on accounts that have none. An existing hash is never overwritten.
- Without the connection string or the password the seed is skipped and the API still starts.
- It never runs outside the Development environment.
- To change the password, delete the four accounts or run `scripts/reset-identity-db.ps1`, then start the API again.
- Roles (admin, teacher, student) are not stored yet because the account model has no role.

For integration tests use `TestUsers.Create(accounts, role, status, password)` (`Platform.Tests/Identity/TestUsers.cs`). Without a password the account has no hash and cannot log in.
