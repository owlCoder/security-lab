# Threat model for ACC-04 and ACC-08

Scope includes both implemented actions, provider snapshots, context and audit. Real identities, production provider integrations and durable storage are planned separately. The model below explicitly links asset -> misuse/threat -> control -> test -> evidence.

Evidence references E1-E6 are defined in [VALIDATION.md](VALIDATION.md). E1/E2/E3 are recorded checks of this prepared update; E4 is the successful baseline CI only. A baseline green run is not a new run of changed code.

| ID / component | Asset | Misuse / threat | Implemented control | Named automated test(s) | Evidence |
| --- | --- | --- | --- | --- | --- |
| TM01 / ACC-04 | Confidential attempt answer | Change ID to read another student's answer | Current subject, ownership and membership checks before answer retrieval | StudentCannotReadAnotherOwner; ForeignAndMissingHaveIdenticalPublicResponses | E1; E2 |
| TM02 / ACC-04 | Course-scoped review authority | Teacher or assistant reads outside course/delegation | Match course and active membership; explicit reviewer rule | TeacherCannotReadAnotherCourse; AssistantRequiresExplicitReviewDelegation | E1 |
| TM03 / ACC-04 | Identity and authoritative role | Spoof actor/role/scope in request | Protected test cookie; server-resolved current snapshots; ignore supplied authority fields | ClientSuppliedActorRoleAndScopeDoNotChangeIdentity | E1 |
| TM04 / ACC-04 | Current authorization | Reuse a still-present cookie after account or membership becomes inactive | Re-read account/membership per request; deny before content/write | CurrentAccountStateIsRecheckedForEveryRequest; RevokedMembershipIsRecheckedBeforeWrite | E1 |
| TM05 / ACC-04 | Draft integrity | Another student or reviewer overwrites a draft | Owner + Student membership + InProgress guard | UnauthorizedDraftWriteDoesNotChangeState; ReviewPermissionDoesNotGrantDraftWrite | E1; E2 |
| TM06 / ACC-04 | Ownership, course and resource state | Mass assignment through extra JSON fields | Input DTO accepts answer/version; writer retains all metadata except Version | ExtraInputFieldsCannotChangeOwnershipCourseOrStatus | E1 |
| TM07 / ACC-04 | Versioned draft content | Stale or concurrent writer loses an update | Expected version and atomic comparison of the full authorized snapshot | StaleVersionReturnsConflictWithoutLosingFirstWrite; CompareAndSwapCommitsExactlyOneCompetingUpdate | E1; E2 |
| TM08 / ACC-04 | Historical submitted/completed answer | Owner or direct adapter call rewrites history | Both policy and adapter reject non-InProgress write | SubmittedAndCompletedAttemptsStayImmutable; SubmittedSnapshotCannotBeWrittenEvenByDirectAdapterCall | E1; E2 |
| TM09 / ACC-04 | Valid request/state | Empty/oversized content or invalid ID reaches storage | GUID, nonblank 1-4000 character answer, positive version; mutation follows validation | MalformedResourceIdIsRejected; InvalidDraftInputDoesNotChangeState; OversizedAnswerIsRejected | E1; E2 |
| TM10 / ACC-08 | Context restrictions | Valid role/window alone grants foreign access | Base authorization AND context; no context-based escalation | ContextCannotGrantForeignResource; ValidContextCannotGrantForeignDraftWrite | E1 |
| TM11 / ACC-08 | Time-bounded access | Access before opening/after expiry or at incorrect edge | Server TimeProvider UTC; inclusive start, exclusive end | WindowStartIsInclusive; WindowEndIsExclusive; ExpiredContextPreventsDraftMutation | E1; E2 |
| TM12 / ACC-08 | Policy identity and integrity | Missing/wrong/unversioned policy treated as allow | Require matching ID, version and valid interval; fail closed | MissingContextIsDenied; WrongContextIdentityIsDenied; UnversionedPolicyIsDenied; InvalidWindowFailsClosed | E1 |
| TM13 / ACC-08 | Current policy | Reuse a cached allow after policy closes | Query the context provider for each operation | ChangedContextIsRecheckedBeforeWrite | E1 |
| TM14 / both | Resource existence and answer | Provider outage becomes an ID enumeration oracle or permits write | Known unavailable/missing replies fail closed with identical public 404; internal reason stays distinct | DependencyOutageDoesNotRevealExistence; DraftOutageDoesNotRevealExistence; UnavailableWriterFailsClosedWithoutMutation | E1 |
| TM15 / both | Browser-triggered mutation | Cross-site page selects identity or mutates draft | Non-simple custom header, no CORS, Strict cookie; state-changing route checks header | DemoSessionRequiresCustomHeader; DraftWriteRequiresCustomHeader | E1; E2 |
| TM16 / both | Safe demonstration boundary | Synthetic identity picker is deployed as real login | Entire demo requires Development AND explicit flag; routes absent otherwise | DemoRoutesAreAbsentOutsideExplicitDevelopmentMode | E1 |
| TM17 / both | Audit confidentiality and accountability | Write event leaks answer/session data or lacks actor/version trace | Structured metadata DTO, UTC, correlation and before/after versions; no public audit API | UpdateAuditContainsMetadataWithoutAnswerOrSessionSecret | E1; E5 |
| TM18 / both | Repeatable demonstration state | Prior mutation invalidates the seed or demo's hardcoded version | Fresh adapter recreates seed; HTTP demo reads current version before CAS | NewFixtureRestoresOriginalSyntheticState; OwnerUpdatesDraftAndReadsNewContent | E1; E2 |

## Trust boundaries and data classification

- **Client -> API:** GUID, content and expectedVersion are untrusted. Actor, course membership, policy, resource ownership/status and clock are resolved on the server. The separate component DFDs and shared [flow](DOMAIN_MODEL.md) draw this boundary.
- **Access consumer -> provider module:** future identity/course/ATT/policy/audit adapters own their data. Access consumes versioned DTOs through proposed ports; it never reads or writes another module's tables. This is a logical module boundary in a monolith, not a claimed network service.
- **Demo -> production:** fixed synthetic personas and labeled sample IDs are deliberately public within the explicit local demonstration; no real student records are seeded. The persona endpoint is not institutional authentication.
- **Protected answer -> audit:** only decision metadata crosses into audit. User content, password and cookie/token values do not.

## Limits and remaining work

The public-response equality claim concerns HTTP status/body for the tested missing/foreign cases, including membership/context unavailability; it does not claim timing equivalence. Ports must normalize known transport failures to Unavailable; the services handle the documented timeout. Production adapters need their own agreed error mapping and integration tests.

The local writer guarantees atomic CAS in one simulator instance. Production transaction guarantees, durable/protected audit, actual identity/session revocation and further contextual attributes remain KT2 integration work. Clearing a demo cookie is not proof of server-side revocation of a copied cookie.

External risks to close at KT1 are real backlog traceability and a neighboring-team contract review. See [KT1.md](KT1.md) and [CONTRACTS.md](CONTRACTS.md).
