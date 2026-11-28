# KT2 — demonstration of the AUT-01 frontend (drafts list and creation)

Scope: the drafts page and its data access layer. The commands run from the repository root with Node.js 22.12 or later.

## Preparation

```powershell
cd frontend
npm ci
```

Sign-in uses the real backend and cookie session, so the API and the local identity database must run as described in [DEVELOPMENT.md](../../DEVELOPMENT.md) (`docker compose up -d db`, then `dotnet run --project backend/Platform.Api --urls http://localhost:5080`). The automated checks below need neither the API nor Docker.

## Automated checks

```powershell
cd frontend
npm run build
npm test
```

Scenario groups (each can be run alone):

```powershell
npx vitest run src/api/drafts.test.ts
npx vitest run src/components/drafts/CreateDraftForm.test.tsx
npx vitest run src/__tests__/DraftsPage.test.tsx
npx vitest run src/__tests__/DraftsPageDemo.test.tsx
npx vitest run src/__tests__/DraftsRoute.test.tsx
```

- **Data access layer:** the request goes through the shared session request, the body has only `title`, `goal`, `type`, `401`, `403`, `400`, `422`, `409`, `503` and network failures become local messages without reading the server text, malformed lists and drafts are rejected.
- **Form and page:** validation, disabled button while sending, one request on double click, `401`, `403`, `422` keep the entered values, list reloads after create, load error with retry.
- **Demo and source selection:** the demo notice stays visible and never claims that a draft was saved, no request is sent in demo mode, `VITE_DRAFTS_API=true` loads and creates through the shared request and reloads the list.

## Demonstration in the browser

```powershell
cd frontend
npm run dev
```

1. Sign in with a development account and open `http://127.0.0.1:5173/drafts`.
2. Show the demo notice, then open **Novi nacrt** and submit an empty title. The error message appears and nothing is created.
3. Enter a title, a goal and a type and submit. The draft appears in the list, the form closes.
4. Open the draft. Its title, goal and type are shown.
5. Reload the page. The list is empty again, which the notice announces.

Server mode (requires the draft routes on the backend):

```powershell
$env:VITE_DRAFTS_API = "true"
npm run dev
```

The page then has no demo notice, loads the list with `GET /api/authoring/drafts` and creates drafts with `POST` on the same path using the session cookie. If the routes are not exposed by the running API, the page shows the load error and a retry button.

## Evidence of this check

| Group | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Data access layer (`drafts.test.ts`) | 24 | 0 | 0 |
| Demo gateway | 4 | 0 | 0 |
| Form | 12 | 0 | 0 |
| Page | 16 | 0 | 0 |
| Route and source selection | 3 | 0 | 0 |
| Demo mode | 4 | 0 | 0 |
| List | 5 | 0 | 0 |
| **Whole frontend (26 files)** | **241** | **0** | **0** |

`npm run build` passes. Date of the check: [add date]. A new GitHub run must be confirmed on the pull request.

## For the individual defence

Show `src/api/drafts.ts` (`createHttpDraftsGateway`, `toDraft`, error mapping), `src/components/drafts/useDrafts.ts` (load, create, refetch, double-submit guard), `src/components/drafts/CreateDraftForm.tsx` (clears the fields only after success), `src/pages/DraftsRoute.tsx` (the single place where the source is chosen) and the tests listed above. Be ready to explain why the page depends on an interface, why the client does not send ID and status, why server texts are not displayed, and what depends on the backend routes.
