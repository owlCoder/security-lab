# Repeatable KT1 demonstration: Read and UpdateDraftAnswer

Requires .NET SDK 10 and the Node version from the root README. The synthetic demo does not require Docker or a database. Use an extracted complete project or apply the accompanying incremental patch to the existing feature branch; do not apply the old initial patch again.

## Automated tests and frontend build

From the repository root:

```powershell
dotnet test .\backend\SecurityLab.slnx --configuration Release --logger "trx;LogFileName=access-kt1.trx" --results-directory .\TestResults
```

In `frontend`, stop a running Vite process with Ctrl+C before reinstalling dependencies:

```powershell
npm.cmd ci
npm.cmd run build
```

## API, PowerShell window 1

```powershell
dotnet run --project .\backend\Platform.Api --launch-profile AccessKt1
```

Keep the process running on http://localhost:5080. The normal API profile leaves the demonstration disabled. Production/Staging do not expose any demo route even if the flag is set.

## Frontend, PowerShell window 2

```powershell
cd .\frontend
npm.cmd run dev
```

Open http://localhost:5173:

1. Choose `ana`, click **Postavi testni identitet**, choose her submitted attempt and **Proveri pristup**: HTTP 200.
2. Set `marko`, read the same ID: safe 404 with no answer. The missing ID gives the same public body.
3. Set `ana`, choose **Anin nepredati pokusaj**, read it, edit **Sadrzaj nacrta** and **Sacuvaj nacrt**: HTTP 200 and an increased version. Read it again to show storage.
4. A teacher can review a submitted attempt in the matching course but cannot change a student's draft.
5. Ana's draft with expired context is denied even though she owns it. Submitted/Completed answers cannot be overwritten.

The interface also uses Serbian diacritics in the actual labels. It only displays the server verdict; direct HTTP negative tests prove the guard does not depend on hiding buttons.

## Direct HTTP demonstration, PowerShell window 3

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\access-demo.ps1
```

Expected: **DEMO PASS: 25/25**. Evidence: `TestResults/access-http-demo.json`.

The demo reads the current draft version before updating, so it can run again against the same process. A fresh server/adapter restores all original synthetic records. It never overwrites submitted/completed answers. A single child-process ExecutionPolicy switch does not change system policy.

## Public development endpoints

| Method/path | Purpose / checks |
| --- | --- |
| GET /api/health | Existing starter health |
| GET /api/access-demo/info | Fixed synthetic persona/resource labels |
| POST /api/access-demo/session/{persona} | Select a known test identity; X-Access-Demo: 1 required |
| POST /api/access-demo/logout | Clear current demo cookie; custom header required |
| GET /api/access-demo/attempts/{guid} | Protected ReadAttempt; safe generic denial |
| PUT /api/access-demo/attempts/{guid}/answer | Protected UpdateDraftAnswer; header, nonblank answer <=4000 chars and expectedVersion >=1 required |

Example update JSON:

```json
{"answer":"Sinteticki izmenjen odgovor.","expectedVersion":1}
```

HTTP 200 returns the authorized answer/version. Invalid input is 400; anonymous is 401; missing/foreign/unavailable is the same safe 404. A stale version from an otherwise authorized owner is 409 `RESOURCE_CHANGED` and requires reloading and a fresh authorization decision.

## Change audit demonstration

NUnit `UpdateAuditContainsMetadataWithoutAnswerOrSessionSecret` prints a real `KT1_AUDIT_JSON` entry to the TRX output. The delivery includes the extracted metadata sample. Show actor, action, target, UTC, correlation, outcome, policy version and before/after resource versions. There is no public audit endpoint and no answer/cookie/token in this event.

## External KT1 evidence

Use [BACKLOG.md](BACKLOG.md) to create real Tapiz tasks, and [CONTRACTS.md](CONTRACTS.md) to request the real neighboring-team review. Those external activities are not performed by the local demonstration.
