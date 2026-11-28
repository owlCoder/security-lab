# Threat model — both assigned components

Trust boundaries: (1) untrusted browser ↔ server API; (2) application ↔ shared decision engine and trusted identity/membership/ATT adapters; (3) authorized application ↔ data mutation and audit adapter. The KT1 simulator runs providers in one process but preserves their interface boundaries. Its cookie is a synthetic session reference, not authoritative role/membership/capability data.

| Component / asset | Misuse or threat | Implemented control | Automated test | Evidence |
|---|---|---|---|---|
| ACC-04 student answer | Replace URL ID to read another owner | Current ownership + course membership + shared explicit decision; content fetched after authorization | `ConcreteReadChecksOwnershipMembershipAndTeachingRole`, `ForgedActorHeadersDoNotElevateStudent` | NUnit TRX; HTTP IDOR demo |
| ACC-04 answer existence | Compare foreign and missing results, including outage | Identical public 404 JSON; metadata-only internal reason | `ForeignAndMissingAreIdenticalEvenWithMembershipOutage` | NUnit HTTP tests |
| ACC-04 unsubmitted answer | Teacher/assistant/admin uses broad role | Teacher only same-course submitted/completed reads; no admin bypass | `UnsubmittedAnswerStaysPrivateFromTeacher`, read matrix tests | NUnit + demo |
| ACC-04 draft/history | Concurrent overwrite or edit historical state | Input validation + owner student + InProgress + atomic snapshot/version CAS | `ConcurrentDraftWritesWithSameVersionHaveExactlyOneWinner`, `SubmittedAndCompletedAttemptsAreImmutable` | NUnit + demo |
| ACC-04 decision | Stale/inactive identity or failed dependency becomes Allow | Current account and membership checks; fail closed | `DependencyOutageDeniesOwnerReadAndWriteWithoutMutation` | NUnit |
| ADM-08 course isolation | Course A administrator changes B | Explicit grant bound to course; no global role shortcut | `RootIsCourseScopedAndCannotRevokeOtherCourseOrRootGrant` | NUnit + demo |
| ADM-08 delegation | Self-escalation, arbitrary capability, teaching grant or redelegation | Exact capability whitelist, no self-grant, only roots delegate/revoke | `InvalidOrEscalatingCapabilitySetIsRejected`, delegation lifecycle | NUnit + demo |
| ADM-08 authority | Reuse revoked grant or existing cookie | Current grant each request; captured authority/version rechecked under mutation lock | `RevocationRejectsTheSamePreviouslyIssuedCookie`, `RevocationRejectsPreviouslyCapturedAuthorityAtCommitBoundary` | NUnit + immediate-revoke demo |
| ADM-08 separation of powers | Administrative grant becomes teaching membership | Separate stores and capabilities; no answer or membership mutation in admin APIs | `AdministrativeGrantDoesNotCreateTeachingMembership`, admin answer denials | NUnit + demo |
| ADM-08 provider availability | Missing/failed admin dependency reveals course or grants access | Public generic denial, no mutation | `AdminOutageIsFailClosedAndDoesNotRevealMissingCourse` | NUnit |
| Both components / session | Client supplies actor/role, cross-site mutation or production demo exposure | Server-issued HttpOnly Strict cookie; no CORS; mandatory non-simple mutation header; Development + explicit flag | `ForgedActorHeadersDoNotElevateStudent`, `MissingMutationHeaderCannotChangeSessionOrDraft`, `SyntheticRoutesRequireDevelopmentAndExplicitFlag` | NUnit |
| Both components / audit | Answer/secret leaks into change log | Fixed structured SecurityEvent fields; no answer, cookie, token or credentials | `DraftUpdatePersistsAndStaleVersionFailsWithMetadataOnlyAudit` | NUnit + server terminal |
| Shared platform | Unknown action receives implicit access | Existing shared default-deny decision/catalog/registry unchanged | `SharedDefaultDenyContractHandlesUnknownActionAndNoSubject` | NUnit; existing shared demo preserved |

Residual limitations: simulator data and audit are process-local and reset on restart; no production credentials are accepted; all demo routes are off unless explicitly enabled in Development. HTTP 404 normalization does not claim timing equivalence. Database-level audit atomicity, cross-module transactions, production session security, operational monitoring and actual provider review remain KT2 integration work. No production-completeness claim is made.

The HTTP revocation regression now explicitly sends the cookie issued before revocation, using separate administrator and delegate clients and no second delegate login. The live script verifies exactly one delegate sign-in and an unchanged cookie, records only proof booleans/counters, and never stores the cookie value in evidence. Both DFDs include provider/store return data and explicit denial/audit routes.


## KT2 increment 2 — real-session administration boundary

The browser-to-API trust boundary now precedes actual Identity session validation;
the API-to-provider boundary includes separate Identity and administration
transactions. `adm08-kt2-dfd.mmd/.svg` shows these boundaries and rejection paths.

| Asset | Misuse/threat | Control | Regression evidence |
|---|---|---|---|
| Administrative identity | Forged development headers or synthetic KT1 cookie | Endpoint-specific real-session provider, full authentication level, current account | `ForgedHeadersAndSyntheticKt1CookieCannotAuthenticateProductionAdministration`; `MfaPendingSessionIsNotAnAdministrativeIdentity` |
| Current permissions | Reuse pre-revocation cookie or captured grant | Fresh grant reads and serialized mutation recheck; fresh Identity session check before commit | `RealLoginDelegationSettingsRevocationRejectsTheExactPreviouslyIssuedCookie`; `SessionRevokedDuringOperationRollsBackBeforeCommit` |
| Account lifecycle | Use a blocked/archived account or logged-out/expired session | Real current account/session provider on each request | `CurrentAccountStatusRejectsExistingCookie`; `LoggedOutOrExpiredSessionCannotBeReused` |
| Settings and grants | Cross-origin write, oversized body or delegation escalation | Strict Secure cookie, required non-simple header, same-origin validation, body limit, capability allowlist | `WriteRequiresHeaderAndSameOrigin`; `OversizedRequestIsRejectedBeforeItCanChangeSettings`; `HttpDelegationCannotEscalateCapabilities` |
| Course existence | Enumerate IDs during outage | Uniform public denials; dependency failures never grant access | `ForeignAndMissingCourseHaveTheSamePublicResponse`; `DependencyOutageIsFailClosedWithoutResourceEnumeration` |
| Producer change evidence | Successful mutation without durable producer event | Single transaction, matching-event enforcement and rollback | `OwnAuditFailureRollsBackAnHttpMutation`; `HttpMutationAuditContainsServerActorAndCorrelationWithoutSecrets` |

Central middleware denials retain the central Access audit path; the module does
not add a second producer denial for a handler that was never entered. Successful
administrative changes require their own durable producer event. Delivery of
those outbox records to central Audit is still pending. Identity revocation and
the final administration commit are not a distributed transaction; see the
explicit consistency limit in `KT2.md`.


## KT2 increment 3 — provider and operator boundaries

| Asset | Misuse/threat | Control | Test/evidence |
|---|---|---|---|
| Current course scope | Stale grants keep working after archive/delete/provider outage | Fresh ACC-01 read-only lookup in policy and before own commit; rollback on final failure | CatalogChangesDenyExistingCookieWithoutRevealingCourseExistence; CourseArchivedDuringMutationRollsBackBeforeCommit |
| Root authority | Untrusted/missing account/course; duplicate or replacement root | Explicit schema-owner CLI, active provider checks, per-course lock, same-root idempotency, no HTTP bootstrap | BootstrapRejectsUntrustedOrInactiveProviderRecords; BootstrapCannotReplaceAnExistingRoot; ConcurrentBootstrapCreatesOneRootAndOneEvent |
| Root audit | Bootstrap succeeds without producer record | Own metadata/root/operator event in one transaction | BootstrapAuditFailureRollsBackBothRootAndSettings |
| Audit delivery | Central outage/crash causes loss or duplicate events | Immutable outbox, stable IDs, acknowledged-only receipts; retry and no forged type mapping | RealCentralAuditAcknowledgesDelegationAndRevocationAndDeduplicatesCrashRetry; AuditOutageLeavesDeliveryUnacknowledgedAndRetrySucceeds; UnmappedSettingsEventsAreRetainedWithoutInventingCentralTypes |
| Attempt answers | IDOR, role/header injection, missing membership provider | Real session, server metadata, shared membership reader, central read policies plus concrete application checks | Acc04ForeignAndMissingStayIdenticalIncludingAttOutage; Acc04MissingMembershipProviderFailsClosedInsteadOfUsingDemoMembership; Acc04ValidSyntheticCookieDoesNotAuthenticateTheRealSessionApi |
| Draft history | Stale/racing writes; owner changed after authorization | Revalidate current authorized scope, bind entire ATT snapshot; require atomic provider CAS/audit receipt | Acc04ConcurrentSameVersionWritesHaveOneWinner; Acc04OwnerChangedAfterCentralDecisionNeverReceivesTheAnswer; Acc04SubmittedAndCompletedAnswersRemainImmutable |
| Service credential and availability | Provider redirect, metadata with answer, oversized/stalled response | Fixed HTTPS origin, separate server credential, no redirects/cookies, strict DTO/size and full-response timeout | AttemptProviderAdapterTests |
| Session freshness | Existing session revoked while request is in progress | Fresh real session check before provider mutation; before returning a read | Acc04RevokedSessionDuringDecisionDoesNotDispatchAProviderWrite |

Trust boundaries are browser→session/API, consumer→ATT/membership/course providers,
operator→schema-owner bootstrap, and durable outbox→central Audit. ATT/membership
positive tests are contract doubles until their real modules are available; their
CAS/audit guarantees are a provider obligation. No distributed transaction is claimed
across Identity, catalog, remote ATT, membership and administrative storage. Other
teams' shared policies and data are preserved.


## KT2 increment 4 — live demonstration boundary

| Asset | Threat | Control | Evidence |
|---|---|---|---|
| Existing project/provider state | A demonstration migrates or deletes a real database | Default isolated Docker server; explicit dedicated-server opt-in; GUID-named created database; child environment contains only fixture connections; Production content root is a private temporary directory | DemoFixture initialization/cleanup and live report; partial initialization failure smoke check |
| HTTPS credentials | Test client accepts any certificate or prints session/password values | Per-run private PFX, localhost/IP SAN, exact certificate/host/port pin, negative pin check; no OS trust import; JSON evidence contains status/boolean checks only | Live TLS check and session cookie/body checks |
| Revoked administrative grant | A restart or cookie refresh hides stale-authority behavior | Separate real Kestrel restart; unchanged captured cookie; one persisted delegate session; no post-grant/revocation sign-in | Same-cookie live read/write denial and restart/state assertions |
| CI integrity | Green result with no live scenario or stale evidence | Strict fresh JSON validation; minimum HTTP/state checks; requires same-cookie, restart, successful cleanup and zero failed checks | demo-coursework-live.ps1; new focused CI step |

The demo is a separate test executable, not an enabled production seed/status/root
endpoint. Temporary certificate/process/database resources belong only to its run.
It demonstrates real ADM providers on this main; it makes no ATT/membership or
browser/UI deployment claim. No global certificate-validation setting is changed.
