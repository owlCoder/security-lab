# ACC-04 — Concrete-resource authorization (J)

Specification §4.2: a student must not read another student's attempt even via a direct URL. The selected resource is an attempt; its owner and course are resolved server-side.

| Use case derived from the requirement | Current flow | Evidence / prerequisite |
|---|---|---|
| Read own attempt | Cookie subject → current active account → attempt metadata → current course membership → shared decision → snapshot-bound answer read | `ConcreteReadChecksOwnershipMembershipAndTeachingRole`; trusted account/membership/ATT adapters |
| Reject direct URL/ID substitution | Foreign and missing attempt return the same public 404 JSON | `ForeignAndMissingAreIdenticalEvenWithMembershipOutage` |
| Read submitted attempt as course teacher | Active Teacher membership in the same course; unsubmitted answer remains private | `UnsubmittedAnswerStaysPrivateFromTeacher`, read matrix |
| Update own unsubmitted draft | Student owner, active membership, InProgress, nonblank ≤4000 characters, expected version; one atomic write | `DraftUpdatePersistsAndStaleVersionFailsWithMetadataOnlyAudit`, CAS test |
| Preserve historical states | Submitted and Completed attempts cannot be overwritten | `SubmittedAndCompletedAttemptsAreImmutable` |
| Reject missing/blocked identity, missing membership, provider outage | Fail closed; denial has no answer or internal reason | `DependencyOutageDeniesOwnerReadAndWriteWithoutMutation` |

The update operation is an explicit starter use case for demonstrating a second primary operation. It is not an implementation of submissions, evaluation, grading, or all ATT features. Teacher membership is consumed from its provider; this code does not assign teaching roles. Assistants have no implicit review power. Administrative grants do not grant answer access.

Actions: `attempt.read`, `attempt.draft.update`; resource type `attempt`; policy `acc04.ownership`, version `acc04:v3`.

Architecture and DFD: `diagrams/acc04-architecture.svg` and `diagrams/acc04-dfd.svg`. See `BACKLOG.md` for order/prerequisites and `CONTRACTS.md` for provider ownership.


KT2 runtime progression is in `KT2.md`: real Identity sessions, the existing shared
membership contract, central read policies, an ATT transport adapter and snapshot/CAS
checks. Actual ATT/persistent ACC-02 providers are not on the current main revision;
positive transport tests explicitly use contract doubles. Do not treat them as a
completed external-provider integration. Current diagrams are
`diagrams/acc04-kt2-architecture.svg` and `diagrams/acc04-kt2-dfd.svg`.
