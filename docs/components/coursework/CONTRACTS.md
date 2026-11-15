# Neighboring-module contracts and review record

**Review status: PENDING — no neighboring-team approval is fabricated.** Record at least one actual review before claiming the full external KT1 obligation is closed.

| Boundary | Provider / consumer ownership | Positive response | Negative / unavailable response | Proof |
|---|---|---|---|---|
| `Platform.Access.Contracts.IAccessDecision.DecideAsync` | Existing shared access component → both consumers | Explicit Allow with policy ID/version | Deny for unknown action, missing/blocked subject, absent/failed policy | `SharedDefaultDenyContractHandlesUnknownActionAndNoSubject`; HTTP uses same engine |
| `IAccountReader.FindAccount` | Identity provider → both consumers | Current matching active account | NotFound, inactive, Unavailable; mismatched identity must not grant access | HTTP blocked/current-account/outage checks |
| `IMembershipReader.FindMembership` | Membership provider → ACC-04 | Active matching account + course + role | Missing, inactive, mismatched or Unavailable | Cross-course teacher and membership-outage tests |
| `IAttemptStore.FindAttempt` | ATT metadata → ACC-04 | Immutable metadata snapshot without answer | Missing, Unavailable | `ProviderContractSupportsFoundMissingAndUnavailableWithoutAnswerInMetadata` |
| `IAttemptStore.ReadAnswer/UpdateDraft` | ATT content/simulator → ACC-04 | Answer bound to the exact authorized snapshot; atomic draft CAS | Snapshot mismatch denies; concurrent/stale write Conflict; Unavailable denies | `ConcurrentDraftWritesWithSameVersionHaveExactlyOneWinner` |
| `ICourseAdministrationStore` | ADM-08 → shared policy and administration service | Course metadata/current explicit grant; snapshot-bound read and authorized mutation | Missing, revoked, wrong-course, Conflict, Unavailable | HTTP delegation lifecycle and commit-boundary revocation test |
| `ISecurityAudit.Write` | Application change metadata → audit adapter | Metadata-only SecurityEvent | Simulator has no durable sink; production failure/transaction policy must be agreed | Draft audit test; terminal `KT1_AUDIT_JSON` |

The existing shared contract is published in `docs/components/ACC-05-ACC-08.md`; that file and `backend/Platform.Access` remain unchanged. Concrete actions use lowercase dotted IDs accepted by its catalog. Module policies are registered into its existing registry; there is no alternate default-deny implementation.

For KT1, `DemoStore` is a deterministic test double implementing positive, missing and unavailable responses. No caller can toggle outage modes via HTTP. The course administration simulator owns only administrative metadata and grants. It never writes to another module's membership, teaching assignment, student answer ownership or grade tables.

## Proposed ATT review message

Please review the ACC-04 boundary in `Modules/Access/Contracts.cs`: `FindAttempt` returns metadata without Answer; the content operation receives the exact authorized snapshot; `UpdateDraft` atomically checks that snapshot, InProgress state and expected version. Found / NotFound / Unavailable must stay distinct internally; unauthorized public responses stay identical. Confirm ownership of the real adapter and whether this CAS contract fits your ATT module. This is a review request, not a claim that your module is already implemented.

## Record after a real review

- Neighboring team/module and reviewer: PENDING.
- Actual reviewed commit or PR and contract version: PENDING.
- Date of the real review: PENDING.
- Comment/issue/Tapiz URL containing acknowledgement: PENDING.
- Agreed changes and unresolved questions: PENDING.

A request message alone is not an accepted contract. A local simulator alone does not prove external agreement. Real provider transactions must bind authorization-relevant metadata and writes to a consistent version; cross-provider revocation during an in-flight operation requires an agreed integration strategy. KT1 simulator checks administrative revocation atomically in its own store.

## Concrete review packet — proposed ATT contract v1

Review `Modules/Access/Contracts.cs` together with `AttemptService` and the simulator: metadata contains Id, OwnerId, CourseId, State and Version, not the answer. An authorized read is bound to that exact snapshot. A draft mutation checks InProgress state, the same snapshot and expected version atomically; exactly one concurrent same-version write succeeds. Provider replies distinguish found/missing/unavailable internally while unauthorized public replies are normalized. The client never supplies authoritative ownership or membership.

Ask the actual provider owner to confirm: ownership of the real adapter; version/snapshot binding; treatment of transient failure; historical-state protection; and current-identity/revocation consistency at integration. The delivered test double and contract tests can be reviewed without implementing the neighboring module. Record the actual acknowledgement and its reviewed commit/version. See `CLOSURE.md` for evidence commands.


## KT2 increment 2 — Identity consumption

The ADM-08 API now consumes the published `ISessionManager.ValidateAsync` and
`Platform.Identity.Interfaces.IAccountStatusReader.FindAsync` contracts through
fresh scopes. Real PostgreSQL Identity/session/Audit services and normal
`/api/auth/login` are used by `AdministrationHttpTests`; neither credentials nor
identity are supplied by a test header. The production request identity decorator
is restricted to `AdministrationEndpointMetadata`; other module providers remain
in the chain. The four administrative actions register through central Access.

This is technical integration evidence against the current repository contracts,
not a fabricated neighboring-team acknowledgement. External review status above
remains pending until a real reference is recorded. Root provisioning/course
existence/archive rules, ACC-04 ATT/membership adapters and delivery of the
administrative producer outbox still need their corresponding provider agreements.
