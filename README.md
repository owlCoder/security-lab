# Security Lab

Security Lab is a collaborative student project for the Fundamentals of Information Security course. Its goal is to build an interactive laboratory platform as a modular monolith with well-defined module boundaries and contracts.

The repository contains a starter application, not a complete implementation: authentication, laboratories, authorization, evaluation, and persistent storage still need to be developed. The course project specification is available in [its original Serbian version](docs/spec/PROJECT_SPECIFICATION_OIB.pdf).

## Technology

The reference stack is React and TypeScript for the frontend, ASP.NET Core and C# for the API, PostgreSQL for data storage, and NUnit for tests. Alternative components may be used when justified and compatible with the domain rules and security boundaries.

## Getting started

Requirements: .NET SDK 10 and Node.js 22.12 or later (or Node.js 24 or later).

1. Start the API: `cd backend/Platform.Api && dotnet run --urls http://localhost:5080`
2. In another terminal, start the frontend: `cd frontend && npm ci && npm run dev`
3. Open `http://localhost:5173`. The API health endpoint is `http://localhost:5080/api/health`.

The optional local PostgreSQL service can be started with Docker Compose after copying `.env.example` to `.env` and setting the required credentials.

## Repository structure

- `backend/Platform.Api/` — API entry point.
- `frontend/` — React application.
- `docs/ARCHITECTURE.md` — conceptual architecture and module boundaries.
- `docs/DATA_FLOW.md` — platform-level data flow example.
- `docs/PROJECT_COMPONENTS.md` — project component assignments.
- `docs/templates/PROJECT_COMPONENT.md` — component documentation template.

See the course specification for the full requirements. Use short-lived branches and pull requests for changes; keep credentials and other secrets out of the repository.
