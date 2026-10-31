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

1. Copy `.env.example` to `.env` and set the credentials.
2. Start PostgreSQL: `docker compose up -d db`
3. Restore the EF Core tool and the packages: `dotnet tool restore`, then `dotnet restore backend/SecurityLab.slnx`
4. Store the connection string outside the repository, replacing each `<...>` placeholder with its value from `.env`:

   `dotnet user-secrets set 'ConnectionStrings:Identity' 'Host=127.0.0.1;Port=5432;Database=<POSTGRES_DB>;Username=<POSTGRES_USER>;Password=<POSTGRES_PASSWORD>' --project backend/Platform.Identity`

5. Apply migrations:

   `dotnet ef database update --project backend/Platform.Identity --startup-project backend/Platform.Identity`

After changing the identity model, add a migration:

`dotnet ef migrations add <Name> --project backend/Platform.Identity --startup-project backend/Platform.Identity --output-dir Migrations`

The `Migrations_MatchTheModel` test fails when a model change has no migration. The API starts without a connection string, so steps 4 and 5 are only needed for work that uses the identity database.

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
- The schema is dropped in the `db` service, while migrations use your `ConnectionStrings:Identity` secret. Keep the secret pointed at that local database.
- A red `Failed executing DbCommand` message for `__EFMigrationsHistory` is expected. EF Core checks for that table before recreating it. The reset succeeded if the script ends with `Done. Schema 'identity' was recreated from migrations.`
- If migrations fail after the drop, the script reports it and the schema is left incomplete. Fix the problem and run the script again.
