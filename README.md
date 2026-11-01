# Security Lab

Security Lab is a collaborative student project for the Fundamentals of Information Security course. Its goal is to build an interactive laboratory platform as a modular monolith with well-defined module boundaries and contracts.

The repository contains a starter application, not a complete implementation: authentication, laboratories, authorization, evaluation, and persistent storage still need to be developed. The course project specification is available in [its original Serbian version](docs/spec/PROJECT_SPECIFICATION_OIB.pdf).

## Technology

The reference stack is React and TypeScript for the frontend, ASP.NET Core and C# for the API, PostgreSQL for data storage, and NUnit for tests. Alternative components may be used when justified and compatible with the domain rules and security boundaries.

## Getting started

Requirements: .NET SDK 10 and Node.js 22.12 or later (or Node.js 24 or later). Login and the identity module also need Docker.

1. For login and the identity module, copy `.env.example` to `.env` and start PostgreSQL: `docker compose up -d db`
2. Start the API: `cd backend/Platform.Api && dotnet run --urls http://localhost:5080`
3. In another terminal, start the frontend: `cd frontend && npm ci && npm run dev`
4. Open `http://localhost:5173`. The API health endpoint is `http://localhost:5080/api/health`.

Locally, the API applies the identity migrations and seeds the development accounts when it starts. It also starts without the database, but login then does not work. See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) for configuration, migrations, and the database reset.

## Repository structure

- `backend/Platform.Api/` — API entry point.
- `backend/Platform.Identity/` — identity module: domain, `DbContext`, and migrations.
- `frontend/` — React application.
- `docs/ARCHITECTURE.md` — conceptual architecture and module boundaries.
- `docs/DATA_FLOW.md` — platform-level data flow example.
- `docs/PROJECT_COMPONENTS.md` — project component assignments.
- `docs/templates/PROJECT_COMPONENT.md` — component documentation template.

See the course specification for the full requirements. Use short-lived branches and pull requests for changes; keep credentials and other secrets out of the repository.

## ACC-04 / ACC-08 initial implementation

The opt-in development demonstration includes protected attempt reads and own-draft
updates with atomic version checks, synthetic data and an initial context policy. Start it with
`dotnet run --project backend/Platform.Api --launch-profile AccessKt1`, then start the
frontend as above. The normal API profile leaves this demonstration disabled.

Component scope, architecture, DFDs, test commands, and remaining work are documented in
[the access KT1 checklist](docs/components/access/KT1.md).

The checklist covers all eight KT1 obligations, including the prepared Tapiz backlog,
shared domain/security model, threat-to-test traceability and proposed provider contract.
Real Tapiz entries and a neighboring team's contract acceptance must be recorded as actual
external evidence. They are not created by the demo or claimed by the ZIP.
