# ACC-04 / ACC-08 backlog and implementation order

This is the complete prepared backlog for the two assigned requirements. **No card has been created in a live Tapiz instance by this package.** Record actual project/card URLs in [EVIDENCE.json](EVIDENCE.json).

The UC/task identifiers below are local decomposition identifiers. The specification requirement IDs remain ACC-04 and ACC-08. Existing work was already committed in [PR #18](https://github.com/owlCoder/security-lab/pull/18); do not invent earlier task creation dates.

## Use cases from the specification and their decomposition

| Use case | Requirement | Scenario | Preconditions | Existing test / plan |
| --- | --- | --- | --- | --- |
| ACC04-UC01 | ACC-04 | Read own attempt | Active account and matching active membership; real attempt snapshot; valid context. | OwnerReceivesAnswerThroughHttp |
| ACC04-UC02 | ACC-04 | Reject direct foreign-resource access | A second student and an existing foreign attempt, plus a missing ID. | ForeignAndMissingHaveIdenticalPublicResponses |
| ACC04-UC03 | ACC-04 | Apply scoped reviewer permissions | Course membership with teacher or explicit assistant delegation. | TeacherCannotReadUnsubmittedAnswerOfAnotherOwner |
| ACC04-UC04 | ACC-04 | Recheck current account and membership | Identity adapter and current membership snapshots. | RevokedMembershipIsRecheckedBeforeWrite |
| ACC04-UC05 | ACC-04 | Update own draft answer | Student owns an InProgress attempt; valid input, version and context. | OwnerUpdatesDraftAndReadsNewContent |
| ACC04-UC06 | ACC-04 | Prevent stale or historical mutation | Versioned attempt adapter with atomic compare-and-swap. | CompareAndSwapCommitsExactlyOneCompetingUpdate |
| ACC04-UC07 | ACC-04 | Fail closed on missing/unavailable dependency | Explicit NotFound/Unavailable provider replies or a timeout. | DraftOutageDoesNotRevealExistence |
| ACC08-UC01 | ACC-08 | Combine base authorization and context | ACC-04 base decision and an identified/versioned context policy. | ContextCannotGrantForeignResource |
| ACC08-UC02 | ACC-08 | Enforce server time boundaries | Server TimeProvider; NotBefore/NotAfter window. | WindowEndIsExclusive |
| ACC08-UC03 | ACC-08 | Reject missing or invalid policy data | Policy snapshot with matching ID and nonempty version. | UnversionedPolicyIsDenied |
| ACC08-UC04 | ACC-08 | Recheck context before the next operation | Context reader evaluated for each request. | ChangedContextIsRecheckedBeforeWrite |
| ACC08-UC05 | ACC-08 | Apply context to both protected actions | ReadAttempt and UpdateDraftAnswer are explicit actions. | ValidContextCannotGrantForeignDraftWrite |
| ACC08-UC06 | ACC-08 | Extend agreed contextual attributes in KT2 | Agreement on attribute providers and policy ownership/version rules. | KT2 plan |

## Ordered tasks

Dependencies refer to task IDs in this table. The JSON list is in a valid dependency order. A local implementation status is not a Tapiz completion state or acceptance by a reviewer.

| Task | Requirements | Title | Phase | Current evidence | Depends on |
| --- | --- | --- | --- | --- | --- |
| ACC04-01 | ACC-04 | Read own attempt and reject IDOR | KT1 | VERIFIED_BASELINE | - |
| ACC04-02 | ACC-04 | Course-scoped review and current account checks | KT1 | VERIFIED_BASELINE | ACC04-01 |
| ACC08-01 | ACC-08 | Versioned time-window restriction after base checks | KT1 | VERIFIED_BASELINE | ACC04-01 |
| ACC04-03 | ACC-04, ACC-08 | Authorize and validate own draft update | KT1 | VERIFIED_LOCAL | ACC04-01, ACC08-01 |
| ACC04-04 | ACC-04 | Atomic draft version check and history protection | KT1 | VERIFIED_LOCAL | ACC04-03 |
| ACC04-05 | ACC-04 | Normalize dependency failure without resource enumeration | KT1 | VERIFIED_LOCAL | ACC04-01, ACC04-03 |
| ACC08-02 | ACC-08, ACC-04 | Recheck context for reads and draft writes | KT1 | VERIFIED_LOCAL | ACC08-01, ACC04-03 |
| KT1-01 | ACC-04, ACC-08 | Domain model, statuses, ownership and decision matrix | KT1 | PREPARED | ACC04-01, ACC08-01 |
| KT1-02 | ACC-04, ACC-08 | Threat model and metadata-only change audit | KT1 | VERIFIED_LOCAL | ACC04-03, ACC08-02 |
| KT1-03 | ACC-04, ACC-08 | Review one ATT boundary contract with the neighboring team | KT1 | EXTERNAL_PENDING | KT1-01, ACC04-04 |
| KT1-04 | ACC-04, ACC-08 | Record Tapiz task -> branch -> PR and update CI evidence | KT1 | EXTERNAL_PENDING | KT1-01, KT1-02 |
| KT1-05 | ACC-04, ACC-08 | Demonstrate Read and UpdateDraftAnswer with negative tests | KT1 | VERIFIED_LOCAL | ACC04-04, ACC04-05, ACC08-02 |
| ACC04-06 | ACC-04 | Integrate real identity, membership and ATT providers | KT2 | PLANNED | KT1-03, ACC04-05 |
| ACC08-03 | ACC-08 | Extend contextual attributes and policy lifecycle | KT2 | PLANNED | KT1-03, ACC08-02 |

## Put the prepared tasks into Tapiz

1. Use the team's real Tapiz instance; an existing shared instance is sufficient.
2. Create project `Security Lab - ACC-04 / ACC-08` and add existing active team accounts through project settings/members.
3. Create the cards using the descriptions below. `backlog.json` is a portable preparation file, **not a claim that Tapiz supports JSON/CSV import**.
4. Run `scripts/prepare-access-task.ps1 -List` to list tasks. Use `-TaskId ACC04-01` to copy that card to the Windows clipboard; paste the title/description into Tapiz.
5. Put KT1 cards in the KT1 sprint and start the sprint when appropriate. This OSS board shows only the active sprint; KT2 cards may stay in the backlog.
6. Link actual card URLs to `EVIDENCE.json` with `scripts/record-access-evidence.ps1`. Link the cards to branch `feature/acc-04-authorization` and PR #18. Record earlier implementation as earlier work, without backdating tasks.
7. Future work follows task -> branch -> PR. Keep implementation, review and course approval as distinct states.

In the current OSS UI, opening a story may not change the URL. If there is no direct card link, record its normal project/backlog URL as TaskUrl and keep the prepared task ID in the actual card title. This lets a reviewer locate the specific card; do not invent an unsupported deep-link route.

## Ready-to-paste card descriptions

### ACC04-01 - Read own attempt and reject IDOR

Requirements: ACC-04. Phase: KT1. Implementation evidence: VERIFIED_BASELINE.

Use cases: ACC04-UC01, ACC04-UC02.

Prerequisites: Confirmed ACC-04 / ACC-08 assignment and specification.

Acceptance: Normal owner read succeeds; changing the ID cannot reveal another student's answer.

Evidence: AccessHttpTests.cs; PR #18; baseline CI success.

### ACC04-02 - Course-scoped review and current account checks

Requirements: ACC-04. Phase: KT1. Implementation evidence: VERIFIED_BASELINE.

Use cases: ACC04-UC03, ACC04-UC04.

Prerequisites: ACC04-01

Acceptance: Course/role/status rules are enforced on the server and inactive accounts are denied.

Evidence: AccessPolicyTests.cs; CurrentAccountStateIsRecheckedForEveryRequest.

### ACC08-01 - Versioned time-window restriction after base checks

Requirements: ACC-08. Phase: KT1. Implementation evidence: VERIFIED_BASELINE.

Use cases: ACC08-UC01, ACC08-UC02, ACC08-UC03.

Prerequisites: ACC04-01

Acceptance: Base AND context is enforced; exact window boundaries and malformed/missing policies are tested.

Evidence: AccessPolicyTests.cs; ACC-08.md.

### ACC04-03 - Authorize and validate own draft update

Requirements: ACC-04, ACC-08. Phase: KT1. Implementation evidence: VERIFIED_LOCAL.

Use cases: ACC04-UC05.

Prerequisites: ACC04-01; ACC08-01

Acceptance: Only an active owning student can update an InProgress answer; malformed input and foreign writes do not mutate it.

Evidence: AccessDraftHttpTests.cs; 25-scenario direct HTTP demo.

### ACC04-04 - Atomic draft version check and history protection

Requirements: ACC-04. Phase: KT1. Implementation evidence: VERIFIED_LOCAL.

Use cases: ACC04-UC06.

Prerequisites: ACC04-03

Acceptance: Exactly one competing update succeeds; stale writes conflict; Submitted and Completed remain unchanged.

Evidence: AttemptPortContractTests.cs; StaleVersionReturnsConflictWithoutLosingFirstWrite.

### ACC04-05 - Normalize dependency failure without resource enumeration

Requirements: ACC-04. Phase: KT1. Implementation evidence: VERIFIED_LOCAL.

Use cases: ACC04-UC07.

Prerequisites: ACC04-01; ACC04-03

Acceptance: Read/write foreign and missing replies are equal when membership/context is unavailable; unavailable writer does not mutate.

Evidence: DependencyOutageDoesNotRevealExistence; DraftOutageDoesNotRevealExistence; UnavailableWriterFailsClosedWithoutMutation.

### ACC08-02 - Recheck context for reads and draft writes

Requirements: ACC-08, ACC-04. Phase: KT1. Implementation evidence: VERIFIED_LOCAL.

Use cases: ACC08-UC04, ACC08-UC05.

Prerequisites: ACC08-01; ACC04-03

Acceptance: Context change is observed on the next write; neither a valid context nor reviewer role grants foreign draft write.

Evidence: DraftPolicyTests.cs; ChangedContextIsRecheckedBeforeWrite.

### KT1-01 - Domain model, statuses, ownership and decision matrix

Requirements: ACC-04, ACC-08. Phase: KT1. Implementation evidence: PREPARED.

Use cases: Shared KT1 obligation.

Prerequisites: ACC04-01; ACC08-01

Acceptance: Present the common model and separate architecture/DFD diagrams for both components, with actual and planned boundaries.

Evidence: DOMAIN_MODEL.md; ACC-04.md; ACC-08.md; diagrams/.

### KT1-02 - Threat model and metadata-only change audit

Requirements: ACC-04, ACC-08. Phase: KT1. Implementation evidence: VERIFIED_LOCAL.

Use cases: Shared KT1 obligation.

Prerequisites: ACC04-03; ACC08-02

Acceptance: Every listed threat maps asset -> misuse -> control -> named test -> evidence; an actual update audit contains versions without answer/session data.

Evidence: THREAT_MODEL.md; UpdateAuditContainsMetadataWithoutAnswerOrSessionSecret.

### KT1-03 - Review one ATT boundary contract with the neighboring team

Requirements: ACC-04, ACC-08. Phase: KT1. Implementation evidence: EXTERNAL_PENDING.

Use cases: Shared KT1 obligation.

Prerequisites: KT1-01; ACC04-04

Acceptance: A real neighboring-team reviewer confirms the proposed snapshot/CAS/error contract; record a real review URL and version.

Evidence: CONTRACTS.md; status PROPOSED, external review pending.

### KT1-04 - Record Tapiz task -> branch -> PR and update CI evidence

Requirements: ACC-04, ACC-08. Phase: KT1. Implementation evidence: EXTERNAL_PENDING.

Use cases: Shared KT1 obligation.

Prerequisites: KT1-01; KT1-02

Acceptance: Create these cards in the real Tapiz project, record their URLs, link PR #18, and record the new successful CI after this update is pushed.

Evidence: EVIDENCE.json; VALIDATION.md.

### KT1-05 - Demonstrate Read and UpdateDraftAnswer with negative tests

Requirements: ACC-04, ACC-08. Phase: KT1. Implementation evidence: VERIFIED_LOCAL.

Use cases: Shared KT1 obligation.

Prerequisites: ACC04-04; ACC04-05; ACC08-02

Acceptance: Show normal read, allowed draft mutation, denied foreign mutation, stale conflict, expired context denial and the actual NUnit/demo output.

Evidence: DEMO.md; scripts/access-demo.ps1.

### ACC04-06 - Integrate real identity, membership and ATT providers

Requirements: ACC-04. Phase: KT2. Implementation evidence: PLANNED.

Use cases: ACC04-UC01, ACC04-UC04, ACC04-UC05, ACC04-UC06.

Prerequisites: KT1-03; ACC04-05

Acceptance: Use agreed provider adapters without direct foreign-table access; add integration, revocation and actual persistence/seed tests.

Evidence: Planned; no production integration is claimed.

### ACC08-03 - Extend contextual attributes and policy lifecycle

Requirements: ACC-08. Phase: KT2. Implementation evidence: PLANNED.

Use cases: ACC08-UC06.

Prerequisites: KT1-03; ACC08-02

Acceptance: Agree attributes and ownership, then test changed/missing attributes and policy versions; preserve mandatory resource checks.

Evidence: Planned; no generic policy language or policy CRUD is claimed.
