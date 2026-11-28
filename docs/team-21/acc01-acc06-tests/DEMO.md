# KT1 demonstration — Team 21, Member 8

**Objective:** show a verifiable individual contribution, component architecture and DFD, and a short working security-test flow. This is a test-component demonstration, not a complete login/HTTP integration demonstration.

## Suggested 6–8 minute sequence

1. **Contribution (45 seconds).** Show 1 ACC-01 test commit and 8 ACC-06 test commits (102 new NUnit cases in 8 files), and briefly state the ACC-01/ACC-06 requirements.
2. **Architecture (60 seconds).** Open [`PROJECT_COMPONENT.md`](PROJECT_COMPONENT.md) and show its rendered [component architecture diagram](component-architecture.png) (section 2). Identify the real services, interfaces, isolated databases and per-test doubles.
3. **DFD (45 seconds).** In the same file, show the rendered [data flow diagram](data-flow.png) (section 3). Explain setup → service call → persisted state and audit → NUnit assertion, including the controlled-input boundary.
4. **ACC-06 negative tests (2 minutes).** Show a student being denied another group's material; show an archived course restricting list visibility; show cross-course group-member IDOR protection.
5. **ACC-01 negative test (1 minute).** Show that a user who can archive course A cannot archive course B; the denied response conceals existence and course B remains unchanged.
6. **Results and remaining work (1 minute).** Run the project tests or show the test output you just executed. Explain the skipped PostgreSQL test and KT2 tasks (real adapters, full HTTP, PostgreSQL and platform CI).

## Commands (repository root; .NET SDK 10)

```powershell
# ACC-06
git status --short
git switch feature/acc06-tests
dotnet test backend/Team21.Acc.Tests/Team21.Acc.Tests.csproj --filter "Name~CanAccess_StudentInOtherGroup_ReturnsNotFound"
dotnet test backend/Team21.Acc.Tests/Team21.Acc.Tests.csproj --filter "Name~FilterVisible_StudentInArchivedCourse_ReturnsNotFound"
dotnet test backend/Team21.Acc.Tests/Team21.Acc.Tests.csproj --filter "Name~GetMembers_TeacherOfOtherCourse_ReturnsNotFoundAndLeavesDataUnchanged"
dotnet test backend/Team21.Acc.Tests/Team21.Acc.Tests.csproj --logger "console;verbosity=minimal"

# ACC-01
git status --short
git switch feature/acc01-tests
dotnet test backend/Team21.Acc.Tests/Team21.Acc.Tests.csproj --filter "Name~Archive_CourseNotAuthorizedForCaller_ReturnsNotFound"
dotnet test backend/Team21.Acc.Tests/Team21.Acc.Tests.csproj --logger "console;verbosity=minimal"
```

**Safety:** never switch branches while local changes could be lost; a throwaway worktree is an alternative. Never run the fixtures against a production database.

**Expected historical output (verify live, do not promise):** ACC-06 118 passing; ACC-01 94 passing and 1 skipped due to missing PostgreSQL test environment.

## Questions likely at KT1

- **What is real?** The service/reader logic, not the external IDN/membership implementations.
- **Why test doubles?** To isolate a rule and deterministically test security-denial cases before other modules are integrated.
- **What is the security property?** Explicitly authorized teachers/students can act; cross-course IDOR, revoked membership and archived-course requests are denied on the server.
- **What remains for KT2?** Real identity, policies, course state and membership adapters, end-to-end HTTP, PostgreSQL constraints and audit integration.

## Suggested demonstration narration (about 45 seconds)

> I wrote automated security tests for ACC-01 and ACC-06. I test real service and reader implementations, while controlling external identity, course membership and permissions through Core interfaces. A negative test checks the exact error and, when appropriate, that the stored data and audit state remain correct. The diagrams show both the dependencies and the test data flows; full sessions, PostgreSQL and cross-team HTTP integration are deferred to KT2.

## Preserve branch state

The ACC-01 and ACC-06 test branches are separate. Before switching, run `git status --short`; **if the working tree is not clean, do not switch**. If both branches must be open concurrently, use a separate clean clone or a worktree. The documentation is committed on `feature/acc06-tests`, so open the diagram before changing to the ACC-01 test branch.
