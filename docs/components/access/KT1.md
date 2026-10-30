# ACC-04 / ACC-08 - KT1 evidence (exercise 4)

This package implements an initial, reviewable flow in the existing modular monolith.
It follows `docs/CHECKPOINT_1.md` and the latest five KT1 criteria, not the older SCUTUM service layout.
Scope: ACC-04 (J), concrete-resource authorization; ACC-08 (N), initial contextual access restriction.

| KT1 criterion | Concrete evidence | State |
| --- | --- | --- |
| 1 Initial implementation | Access module, protected HTTP read, synthetic fixtures, React panel; [demo](DEMO.md) | Implemented initial flow |
| 2 Individual contribution | student-created commit/PR and explanation | Patch prepared; actual student identity and PR still to record |
| 3 Own architecture | [ACC-04](ACC-04.md), [ACC-08](ACC-08.md), separate Mermaid/SVG diagrams | Prepared |
| 4 Own DFD | Actors, processes, stores, labeled flows, client/server trust boundary for each component | Prepared |
| 5 Short demonstration | Browser panel, PowerShell 12-scenario script, NUnit HTTP and policy tests | Executable; see [validation](VALIDATION.md) |

## Start here

1. Read each component document and run [the demo](DEMO.md).
2. Run `dotnet test backend/SecurityLab.slnx --configuration Release` from the repository root.
3. Run `npm.cmd run build` in `frontend` on Windows.
4. Reference ACC-04 and ACC-08 in commits and the PR.
5. Demonstrate the implemented code and explain the limitations. Neither a patch nor generated documentation proves an individual student's understanding or course approval.

## Actual implementation vs remaining work

- ACC-04: current account and course checks, owner access, explicit course review rule, uniform public denial, audit, direct HTTP proof.
- ACC-08: a first context rule uses a versioned time window and server UTC; context can only restrict the base authorization.
- Fixture accounts, memberships, attempts, policy windows and audit are in memory; restarting creates the same synthetic records. No real student records are used.
- Real account authentication/revocation, persistent attempts/memberships, policy administration, multiple contextual attributes and broader action coverage remain for KT2 and agreed integrations.
- No Update, ChangeOwner, enrollment, login/password management, grading or historical-result mutation is claimed.
- Production/staging do not expose the demo routes even if the flag is set. The normal development profile also leaves them off.
- The global assignment table is untouched because a confirmed team roster/student ID was not supplied. The old SCUTUM Txx-to-team mapping is not assumed to apply here.

## Dependencies and plan to KT2

| Step | Owner boundary / work | Evidence to add |
| --- | --- | --- |
| 1 | Agree IIdentityContext/account/membership snapshots with IDN and ACC owners | Real contract review link and canonical IDs |
| 2 | Agree attempt snapshots with ATT owner; preserve historical results | Adapter/contract test using actual owner data |
| 3 | Replace synthetic readers with production adapters and storage migrations | Integration tests and repeatable seed |
| 4 | Extend contextual rules and versions with agreed ownership/review semantics | ACC-08 allow/deny and missing-attribute tests |
| 5 | Integrate actual authentication, revocation, protected audit and API actions | Server tests and updated diagrams |

An unavailable module can be represented by an explicit test adapter at KT1. No external review, deployment, PR or new GitHub run is invented by this package.
