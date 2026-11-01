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
dotnet build backend/Platform.Api/Platform.Api.csproj --configuration Release
dotnet test backend/Platform.Authoring.Tests/Platform.Authoring.Tests.csproj --configuration Release
cd frontend
npm ci
npm run build
```

The first `dotnet test` run downloads the NUnit packages, so it needs an internet connection. All tests should pass. If you add a new backend module, add its test project to the CI workflow as well.

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

- `backend/Platform.Api/` — API entry point.
- `backend/Platform.Authoring/` — authoring module: laboratory drafts and templates (AUT-01, AUT-06).
- `backend/Platform.Authoring.Tests/` — NUnit tests for the authoring module.
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
