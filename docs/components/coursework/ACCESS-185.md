# Issue #185: KT1 integration with strict central Access

Scope: Team 8, ACC-04 / ADM-08. Reviewed on 2026-11-14.

Final verification base: `feature/ACC-05-deny-action-by-default` at `864690111bbbfa0f09c15eedef542ea108eb542c`.
The original failure reproduction was at `788f264ab99b443dea947279c2153071019f774a`.
The newer shared audit change is preserved.
The fetched `main` at `ec0f71b5e5fddf8547e37ea3345cf0de493e1275` is already an ancestor of this base.
This change targets the Access integration branch, not `main` directly.

## Cause and correction

The stricter central middleware intentionally rejects `AllowPublic` and legacy
self-authorization metadata. The KT1 group still had `AllowPublic`, so every
session creation/read/write was rejected before its cookie or concrete policy
could run. The unmodified base reproduced **53 failed / 12 passed / 65 total**
KT1 NUnit cases.

- Every KT1 route now has exactly one `RequireAccess` action. All nine actions
  and their policies are registered before `AddAccessControl()` freezes the catalog.
- The namespaced `kt1.*` actions avoid replacing the real API's `attempt.read`
  policy or any other team's actions. No ACC-08 implementation is added.
- The former private catalog/decision engine is removed. Route checks and the
  services use the global `IAccessDecision`; a small adapter maps the existing
  logical business actions to the synthetic namespaced actions.
- `Kt1ResourcePolicy` invokes the existing concrete ownership, membership,
  active-account and current administrative-capability rules. It is not an
  authentication-only permit policy.
- The outer KT1 subject provider explicitly validates only the synthetic cookie
  scheme on marked KT1 endpoints, before central evaluation. Non-KT1 endpoints
  delegate to the complete existing Identity/laboratory/revision provider chain.
- Public actions are limited to simulator info, session creation and idempotent
  logout. All registration/mapping remains guarded by Development **and**
  `Kt1Demo:Enabled=true`. The non-simple mutation header remains mandatory.
- A KT1-only contract middleware asks the same global central decision engine
  before generic error serialization: anonymous resource requests remain `401`;
  denied authenticated resource requests return identical `404 / RESOURCE_UNAVAILABLE`,
  including missing IDs, foreign resources and dependency outages. A denial never
  enters the business endpoint. Allowed requests still traverse the central
  middleware and the services' snapshot/current-grant rechecks; no decision is cached.
- Denials retain central decision audit and metadata-only KT1 audit. Draft CAS,
  history protection, cross-course boundaries and exact-cookie revocation are preserved.

The main account-registration endpoint on this target branch still carries its
own legacy metadata and returns central `403`, even with KT1 disabled. The KT1
composition test now compares that baseline to the same request with a synthetic
admin cookie. It verifies no authority transfer; it does not change Identity's route
or relax a KT1 authentication expectation. Other composition checks still require
`401` where the current owning module's contract returns it.

## Actual local verification

Linux, .NET SDK 10.0.403, Release configuration:

| Verification | Actual result |
|---|---|
| Unmodified-base KT1 reproduction | 53 failed, 12 passed, total 65 |
| Fixed KT1 NUnit suite | **72 passed / 72**, failed 0, skipped 0 |
| Central Platform.Access NUnit suite | **224 passed / 224**, failed 0, skipped 0 |
| Existing PowerShell/Kestrel live demo | **41 passed / 41**, including the exact pre-revocation cookie |

The seven additional KT1 cases verify route metadata/policy coverage, denial and
exception behavior of the actual global decider (HTTP and direct service writes),
forged headers/invalid cookies for read/write, logout/unknown actions, and registration
before catalog freeze. Existing negative and composition cases remain enabled.

The whole solution was also exercised. It is **not green in this environment**:
Authoring still has its four assertion failures and LabVisibility its forty,
matching the other teams' issue #185 work. Docker-dependent Identity/laboratory
integration tests additionally fail setup because Docker is unavailable here;
other database-dependent cases skip. These infrastructure results are separate
from the reported authorization failures and are not a KT1 success claim.

No changes were made to `backend/Platform.Access`, shared CI, Authoring, LabVisibility,
other teams' policies, frontend, or database setup. Their unresolved work is not
hidden by excluding tests or weakening assertions about KT1 resource access.

## Reproduction and remote evidence

```powershell
dotnet test .\backend\Platform.Kt1.Tests\Platform.Kt1.Tests.csproj --configuration Release --logger "trx;LogFileName=kt1-185.trx" --results-directory .\TestResults\kt1-185
dotnet test .\backend\Platform.Access.Tests\Platform.Access.Tests.csproj --configuration Release --logger "trx;LogFileName=access-185.trx" --results-directory .\TestResults\access-185
```

For the live demonstration, start a fresh simulator in a separate terminal:

```powershell
dotnet run --project .\backend\Platform.Api --launch-profile AccessKt1
```

Then run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\kt1-demo.ps1`.
The simulator must start fresh for its deterministic initial versions.

The existing CI workflow retains whole-solution build/tests and the live demonstration.
CI's database harness is `scripts/audit/test-postgres.ps1` and requires Docker.
This local report does not certify a remote Actions run. After opening the PR,
record the actual SHA/run URL and test artifacts in issue #185. Full integration
CI also depends on the remaining owning-team fixes.
