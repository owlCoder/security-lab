> Current Access integration verification (2026-11-14): see [ACCESS-185.md](ACCESS-185.md).
> The PR #36 results below are historical; they do not certify the current integration branch or its CI.

# Validation after the second current-main integration

Reviewed source: PR #36 head `3d751274493141202f85c3d686378aedada012c2` merged with main `0a4f3d1325ef340f19dea25d230209105dfa67b0`. Scope remains **ACC-04 / ADM-08**, Team 8.

## Actual local results

The Release solution build and tests passed on Linux with .NET SDK 10.0.403. All current main solution entries are retained and the KT1 project is included once.

| Test project | Passed / total | Failed |
|---|---|---|
| laboratories.tests | 31/31 | 0 |
| platform.audit.tests | 95/95 | 0 |
| platform.authoring.tests | 55/55 | 0 |
| platform.kt1.tests | 65/65 | 0 |
| platform.ratelimiting.tests | 24/24 | 0 |
| platform.tests | 47/47 | 0 |
| **Whole solution** | **317/317** | **0** |

The component has **65** test cases: its previous 63 plus two composition regressions. The complete solution also includes other teams' tests; 317 is not this team's own test count. All 317 cases executed; none were skipped.

The real PowerShell/Kestrel demonstration passed **41/41**, including the exact pre-revocation delegate cookie without another sign-in. Frontend clean install and TypeScript/Vite build passed. Existing real HTTP composition smoke checks also passed.

The new tests verify that an enabled KT1 simulator preserves laboratory routes and access checks without sharing its synthetic cookie identity, and that invalid main rate-limit configuration still prevents startup. These checks do not claim production identity integration or add new login/rate-limit endpoints.

Reports are in the delivery's `verification/` directory. Chromium was not rerun after this merge; previous browser evidence is historical.

## CI status

The existing combined workflow is preserved: whole-solution build/test, nonzero-test gate, live KT1 demonstration, both result artifacts and frontend build.

Previous successful CI for `3d751274493141202f85c3d686378aedada012c2`: https://github.com/owlCoder/security-lab/actions/runs/41637786694

**A new remote CI run for this second integration is pending the merge commit being pushed to the same PR branch. The previous green run does not certify this source.** Record the actual new SHA, run link and artifacts after completion.

## Scope preservation

The shared `backend/Platform.Access` implementation and `docs/components/ACC-05-ACC-08.md` remain identical to current main. Main's laboratory, rate-limit, assignment, audit and identity modules are retained. Legacy ACC-08/old PR #20/#21 files are not restored. Real Tapiz links and neighboring-team contract review remain separate external evidence.
