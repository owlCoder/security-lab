# Recorded KT1 validation and actual evidence

Prepared update date: **2026-10-31**. Target repository: `owlCoder/security-lab`, branch `feature/acc-04-authorization`, [PR #18](https://github.com/owlCoder/security-lab/pull/18).

## Baseline already verified on GitHub

Published baseline commit: `f3d9d1ccb9b679c47bdfe98eb812362f0f33edb6`.
The [Actions run 41132593252](https://github.com/owlCoder/security-lab/actions/runs/41132593252) completed successfully: backend build/test/upload and frontend install/build steps were successful. Artifact `access-kt1-test-results` exists. Baseline local results were 43/43 NUnit cases and 12/12 HTTP scenarios, also executed on the user's Windows machine; frontend build succeeded there.

This is **baseline evidence**, not a new GitHub run of the draft-write extension. The ZIP has not been pushed by this preparation, so its new CI result is still pending.

## Executed checks of this prepared update

| Evidence | Check actually executed | Result |
| --- | --- | --- |
| E1 | Standard NUnit/VSTest solution build and test | **91 total, 91 passed, 0 failed, 0 skipped** |
| E1 | Existing AccessPolicyTests / AccessHttpTests | 24 + 19 passed |
| E1 | DraftPolicyTests / AccessDraftHttpTests / AttemptPortContractTests | 16 + 27 + 5 passed |
| E2 | Actual PowerShell script against running Kestrel | **25/25 passed** |
| E3 | npm ci, TypeScript/Vite production build | Passed |
| E3 | Chromium with actual React/Vite proxy/Kestrel | Own read/update; foreign read and direct PUT denied without mutation; expired draft denied; 420px layout passed; no page errors |
| E4 | Published baseline Actions jobs | backend SUCCESS; frontend SUCCESS; artifact present |
| E5 | Real update audit emitted by an HTTP integration test and extracted from TRX | UTC/correlation/actor/action/target/outcome/policy and resource versions present; no answer/session secret |
| E6 | External KT1 evidence | Tapiz card creation, neighboring-team acceptance and updated-code CI still need real references |

The update was executed in Linux with .NET SDK 10.0.403, Node 24.22.1, PowerShell 7.6.9 and Chromium Headless Shell. Temporary SDK/browser/diagram tools are not project dependencies and are not included in the source ZIP. New Windows execution is still to be recorded; earlier Windows results apply to the published baseline.

The preparation environment disallows MSBuild out-of-process named pipes. Its successful solution test used `--disable-build-servers -m:1` in addition to the normal command. No authorization or project rule was weakened. The CI continues to use the ordinary test command.

## Evidence files in the delivery

The delivery's top-level `evidence/` folder contains the actual `access-kt1.trx`, parsed NUnit counts, 25-scenario HTTP JSON/log, browser-check JSON, extracted metadata-only audit sample and packaging/source checksums. These are delivery evidence, not an assertion that ignored `TestResults/` files are committed.

The test `UpdateAuditContainsMetadataWithoutAnswerOrSessionSecret` emits `KT1_AUDIT_JSON` in its NUnit output. The shipped sample is extracted from that actual output, not manually invented. The sample comes from a fresh HTTP test fixture (version 1 -> 2); a separately running UI demo may already have a higher synthetic version.

## Reproduce on Windows

```powershell
dotnet test .\backend\SecurityLab.slnx --configuration Release --logger "trx;LogFileName=access-kt1.trx" --results-directory .\TestResults
```

Stop a running Vite process before `npm.cmd ci`, then build the frontend. Start API with the explicit `AccessKt1` profile and run the [25-scenario demo](DEMO.md). A fresh API instance restores the seed; the HTTP demo also works on a previously updated fixture by reading its current version.

## Finish actual external evidence

1. Create the [prepared cards](BACKLOG.md) in the real Tapiz project and record their references.
2. Obtain an actual neighboring-team review of [access-attempt-v1](CONTRACTS.md), including the positive/negative semantics; record the real reviewer and confirmation URL.
3. Commit/push this update to the existing feature branch. Wait for the new Actions run, inspect backend/frontend and the test artifact, and record that run/commit separately from the baseline.
4. Use `scripts/record-access-evidence.ps1` to record actual references in [EVIDENCE.json](EVIDENCE.json). It does not perform remote actions or certify acceptance.
5. Update the PR description to Read + UpdateDraftAnswer, 91/91 local tests and 25/25 local demo; only call new CI successful after the actual new run succeeds.

`python scripts/check-access-kt1.py` validates prepared documentation consistency and reports external gaps. `--require-external` requires recorded task, accepted-contract and updated-CI references; it does not replace the actual reviewer or course assessment.
