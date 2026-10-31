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
3. Restore the EF Core tool: `dotnet tool restore`
4. Store the connection string outside the repository, replacing each `<...>` placeholder with its value from `.env`:

   `dotnet user-secrets set 'ConnectionStrings:Identity' 'Host=127.0.0.1;Port=5432;Database=<POSTGRES_DB>;Username=<POSTGRES_USER>;Password=<POSTGRES_PASSWORD>' --project backend/Platform.Identity`

5. Apply migrations:

   `dotnet ef database update --project backend/Platform.Identity --startup-project backend/Platform.Identity`

After changing the identity model, add a migration:

`dotnet ef migrations add <Name> --project backend/Platform.Identity --startup-project backend/Platform.Identity --output-dir Migrations`

The `Migrations_MatchTheModel` test fails when a model change has no migration. The API starts without a connection string, so steps 4 and 5 are only needed for work that uses the identity database.
