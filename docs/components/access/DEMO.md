# Repeatable KT1 demonstration

Run from the `security-lab` repository. Requires .NET SDK 10 and the Node version documented in the root README. No database or Docker is required for these synthetic fixtures.

## Automated tests

```powershell
dotnet test .\backend\SecurityLab.slnx --configuration Release --logger "trx;LogFileName=access-kt1.trx" --results-directory .\TestResults
```

## API (PowerShell window 1)

```powershell
cd "$env:USERPROFILE\Desktop\security-lab"
dotnet run --project .\backend\Platform.Api --launch-profile AccessKt1
```

Keep this process running. `AccessKt1` is an explicit Development profile with AccessDemo enabled. The normal profile leaves the routes disabled; Production/Staging always leave them disabled.

## Frontend (PowerShell window 2)

```powershell
cd "$env:USERPROFILE\Desktop\security-lab\frontend"
npm.cmd ci
npm.cmd run dev
```

Open http://localhost:5173. In the Access panel:

1. Set test identity `ana`, select Ana's submitted attempt and check access: HTTP 200 with synthetic answer.
2. Set `marko`, request the same attempt: HTTP 404 with no answer.
3. Request the missing test ID: same public 404 body.
4. Set `ana`, select the attempt with expired context: HTTP 404 despite ownership.
5. Set `teacher`: submitted attempt in own course is readable. `foreign-teacher`, `assistant` without delegation and `admin` without membership are denied.

The test persona selector exists only to make the current initial flow demonstrable. It is not a login feature and cannot be enabled in production.

## Direct API demonstration (PowerShell window 3)

```powershell
cd "$env:USERPROFILE\Desktop\security-lab"
.\scripts\access-demo.ps1
```

If the local policy blocks running the repository script, invoke this one trusted script in a child process:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\access-demo.ps1
```

The script expects 12 scenarios, asserts status and synthetic answer, compares foreign/missing public bodies and writes `TestResults/access-http-demo.json`. It exits on failure. It does not change your system execution policy or install dependencies.

## Two-minute explanation

- Show the two component documents and their separate architecture/DFD diagrams.
- Trace one request from client to opaque cookie identity, fresh account/membership/attempt snapshots, base checks and context checks.
- Show the allow, foreign-resource deny and expired-context deny.
- Show the automated negative test and actual result.
- Explain remaining production authentication, persistence, contract integration and broader contextual policy work for KT2.

## Current public API

| Route | Method | Purpose |
| --- | --- | --- |
| /api/health | GET | Existing scaffold health (preserved) |
| /api/access-demo/info | GET | Explicit demo's synthetic persona and resource labels |
| /api/access-demo/session/{persona} | POST | Select a fixed synthetic identity; X-Access-Demo: 1 required |
| /api/access-demo/logout | POST | Clear current demo cookie; custom header required |
| /api/access-demo/attempts/{guid} | GET | Protected server read with generic public denial |

The public read never returns internal reason codes. Those are in the internal audit writer, with no audit API exposed. Clearing the demo cookie is not a claim of server-side revocation for copied cookies; real authentication/session lifecycle belongs to integration work.
