# Recorded validation of this KT1 preparation

Base repository: https://github.com/owlCoder/security-lab
Base commit: `55408a4690e522e62508f9bd45e03cca14476bff`.
Preparation date: 2026-10-30. No GitHub PR, CI run or merge is claimed here.

## Executed results

| Check | Actual result |
| --- | --- |
| API Release build | Passed, 0 warnings, 0 errors |
| NUnit via standard dotnet test / VSTest | 43 total, 43 passed, 0 failed/skipped |
| Policy tests | 24 passed |
| In-process ASP.NET HTTP integration tests | 19 passed |
| Solution build/test | Passed with single MSBuild worker in this preparation environment |
| npm ci and TypeScript/Vite production build | Passed |
| scripts/access-demo.ps1 against running Kestrel | 12/12 scenarios passed |
| Browser, React + Vite proxy + Kestrel | Owner allow, foreign deny without content, expired-context deny passed |
| Responsive browser view | 420px viewport, no horizontal overflow, no page errors |
| Diagram rendering | Four Mermaid definitions rendered to SVG and inspected |

Environment: Linux, .NET SDK 10.0.403 / runtime 10.0.14; NUnit 4.6.1;
Node 24.22.1; PowerShell 7.6.9; Chromium via a temporary Puppeteer runner.
Temporary browser/diagram tooling and the downloaded SDK/PowerShell were not added as project dependencies.
The PowerShell script was executed on Linux PowerShell; Windows PowerShell 5.1 was not directly executed here.

## Reproduce

```powershell
dotnet test .\backend\SecurityLab.slnx --configuration Release --logger "trx;LogFileName=access-kt1.trx" --results-directory .\TestResults
```

The preparation environment prohibits MSBuild's out-of-process named pipes. Its equivalent successful command added `--disable-build-servers -m:1`; no project source or runtime authorization rule was relaxed. The CI tests the actual project using the normal command.

Use [DEMO.md](DEMO.md) to start the explicit development profile and run the browser/API demonstration.
Results from preparation are included in the delivery's `evidence/` directory: the actual TRX, build/test logs, 12-scenario HTTP output and browser-check summary.

## Remaining evidence to record

- A real student-created commit and PR, with actual team/student identifiers.
- Windows execution of the patch and local test/demo commands.
- The new GitHub Actions run for this revision. Existing backend/frontend jobs are retained; a test step and `access-kt1-test-results` artifact are added.
- Review/agreements for production identity, memberships, attempt snapshots and contextual policies.
- Course evaluation/approval of the KT1 presentation.

These pending items do not change the recorded local test results; those results do not imply completed production authentication, persistence or the full N-level component.
