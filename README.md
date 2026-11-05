# Security Lab

Security Lab is a collaborative student project for the Fundamentals of Information Security course. Its goal is to build an interactive laboratory platform as a modular monolith with well-defined module boundaries and contracts.

The repository contains a starter application, not a complete implementation: authentication, laboratories, authorization, evaluation, and persistent storage still need to be developed. The course project specification is available in [its original Serbian version](docs/spec/PROJECT_SPECIFICATION_OIB.pdf).

## Technology

The reference stack is React and TypeScript for the frontend, ASP.NET Core and C# for the API, PostgreSQL for data storage, and NUnit for tests. Alternative components may be used when justified and compatible with the domain rules and security boundaries.

## Requirements

| Tool | Version | Check with |
| --- | --- | --- |
| .NET SDK | 10.x | `dotnet --version` |
| Node.js | 22.12 or later (or 24 or later) | `node --version` |
| Git | any recent version | `git --version` |
| Docker Desktop | optional, only for the local PostgreSQL service | `docker --version` |

## Getting started

Run all commands from the repository root unless stated otherwise. The commands below work in PowerShell, Command Prompt and Bash.

### 1. Get the code

```
git clone https://github.com/owlCoder/security-lab.git
cd security-lab
```

### 2. Start the API (terminal 1)

```
cd backend/Platform.Api
dotnet run --urls http://localhost:5080
```

Leave this terminal open. When the API is running, open <http://localhost:5080/api/health>. It should return JSON containing `"status":"ok"`.

### 3. Start the frontend (terminal 2)

```
cd frontend
npm ci
npm run dev
```

`npm ci` is only needed the first time and after `package-lock.json` changes. Then open <http://localhost:5173>. The page shows **API available** under "Development status" when the frontend can reach the API. The frontend forwards every `/api` request to `http://localhost:5080` (see `frontend/vite.config.ts`), so the API must be running first.

To stop either program, press `Ctrl+C` in its terminal.

### 4. Optional: local PostgreSQL

No module uses the database yet. When one does, start the local PostgreSQL service like this:

1. Copy `.env.example` to `.env` (`copy .env.example .env` in PowerShell or Command Prompt, `cp .env.example .env` in Bash).
2. Open `.env` and replace `CHANGE_ME_FOR_LOCAL_DEVELOPMENT` with a password of your choice. Never commit `.env`; it is already in `.gitignore`.
3. Start the service and check that it is healthy:

```
docker compose up -d db
docker compose ps
```

The database listens on `127.0.0.1:5432` only. Stop it with `docker compose down` (add `-v` to also delete its data).

## Running the checks

These are the same checks that run in CI (`.github/workflows/ci.yml`). Run them before opening a pull request.

```
dotnet build backend/SecurityLab.slnx --configuration Release
dotnet test backend/SecurityLab.slnx --configuration Release --no-build
cd frontend
npm ci
npm run build
```

CI builds and tests the whole backend through the solution file `backend/SecurityLab.slnx`. The first run downloads the NUnit packages, so it needs an internet connection. All tests should pass. To run only the authoring tests, use `dotnet test backend/Platform.Authoring.Tests/Platform.Authoring.Tests.csproj --configuration Release`.

If you add a backend module or a test project, add it to the solution so that CI picks it up: `dotnet sln backend/SecurityLab.slnx add backend/<Project>/<Project>.csproj`.

## Troubleshooting

| Problem | What to do |
| --- | --- |
| The page says "API unavailable (start the backend)" | The API is not running or is not on port 5080. Start it as in step 2 and reload the page. |
| `Address already in use` when starting the API or the frontend | Another program uses port 5080 or 5173. Close it, or stop the earlier run of this project with `Ctrl+C`. |
| `dotnet` reports that the target framework `net10.0` is not found | Your .NET SDK is older than 10. Install .NET SDK 10 and check `dotnet --version`. |
| `npm ci` or `npm run dev` reports an unsupported engine or fails on syntax | Your Node.js is too old. Install Node.js 22.12 or later and check `node --version`. |
| `dotnet test` cannot restore packages | Check your internet connection or proxy, then run the command again. |
| `docker compose` complains about `Define POSTGRES_PASSWORD in local .env` | Create `.env` from `.env.example` as in the optional PostgreSQL step. |

## Repository structure

- `backend/SecurityLab.slnx` — backend solution file used by CI.
- `backend/Platform.Api/` — API entry point.
- `backend/Platform.Access/` — access control module (ACC-05, ACC-08).
- `backend/Platform.Assignments/` — assignments module: assignment model, attempt limit and individual deadline extension (ASN-04, ASN-06). Its tests are in `backend/Platform.Tests/Assignments/`.
- `backend/Platform.Audit/` and `backend/Platform.Audit.Tests/` — audit module skeleton and its tests (AUD-01).
- `backend/Platform.Authoring/` — authoring module: laboratory drafts and templates (AUT-01, AUT-06).
- `backend/Platform.Authoring.Tests/` — NUnit tests for the authoring module.
- `backend/Platform.Tests/` — NUnit tests that use the API project (identity tests).
- `frontend/` — React application.
- `docs/ARCHITECTURE.md` — conceptual architecture and module boundaries.
- `docs/DATA_FLOW.md` — platform-level data flow example.
- `docs/DEVELOPMENT.md` — development workflow.
- `docs/PROJECT_COMPONENTS.md` — project component assignments.
- `docs/templates/PROJECT_COMPONENT.md` — component documentation template.
- `docs/components/` — documentation of individual project components.

## Contributing

Use short-lived branches and pull requests for changes (for example `feature/AUT-01-AUT-06-lab-drafts`), and mention the requirement IDs from the specification in commits and pull requests. See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) for the full workflow. Keep credentials and other secrets out of the repository.

See the course specification for the full requirements.

## ACC-04 and ADM-08 KT1 implementation

The started coursework component adds concrete attempt ownership checks and course-scoped administrative delegation through the existing shared `Platform.Access` contract. It includes read/update of an own draft, version checks, two isolated courses, limited delegation/revocation, metadata-only audit, automated negative tests and a React demonstration.

Start the synthetic API with `dotnet run --project backend/Platform.Api/Platform.Api.csproj --launch-profile AccessKt1`, then run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/kt1-demo.ps1` in a second terminal. The frontend starts with the existing commands above. Docker is not needed for this in-memory demonstration.

Build/test: `dotnet test backend/SecurityLab.slnx --configuration Release`. The existing backend/frontend CI jobs are retained; the backend job additionally runs the KT1 tests and live demo and uploads their results. A new remote run must still be confirmed after pushing.

See [the KT1 checklist](docs/components/coursework/KT1.md), [startup and demonstration](docs/components/coursework/DEMO.md), [architecture and DFD diagrams](docs/components/coursework/diagrams), [domain/access matrix](docs/components/coursework/DOMAIN_MODEL.md), [threat model](docs/components/coursework/THREAT_MODEL.md), [provider contracts](docs/components/coursework/CONTRACTS.md), [backlog](docs/components/coursework/BACKLOG.md) and [actual local validation](docs/components/coursework/VALIDATION.md) and [external closure steps](docs/components/coursework/CLOSURE.md).

The synthetic login is enabled only in Development with `Kt1Demo:Enabled=true`; no production authentication, persistent database or actual external contract acceptance is claimed. Other teams' shared components are preserved.
