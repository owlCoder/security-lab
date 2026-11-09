# Project component: IDN-11 session identifier rotation, IDN-22 personal login history search

**Team:** 09

**Student:** Team 09 (all members)

**Specification requirements:** IDN-11 (core), IDN-22 (extended). Related: SEC-01, SEC-03, SEC-04, SEC-05, AUD-01, AUD-03, AUD-04, AUD-10, AUD-11, AUD-19. Depends on IDN-02 and IDN-06 (Team 1: login, session check, MFA) and AUD-01 (Team 12: audit records of logins).

## Responsibilities and boundaries

- Processes and user scenarios:
  - **IDN-11:** after a login or a privilege change the session gets a new identifier and the previous one is rejected. `SessionRotationService` checks the current session, issues a new token from a CSPRNG, and atomically revokes the old session and stores the new one. The new session keeps the chain ID and the absolute expiry of the old one, so a rotation never extends the session lifetime. Every rotation attempt is audited without any token, token hash or chain ID.
  - **IDN-22:** a logged-in user searches only their own login events by period and outcome, with paging. `LoginHistoryService` takes the subject only from `AuthenticatedSubject` (built by the host from a checked session), validates the filter before any data is read, asks the source for events of that subject only, and masks the source IP address before returning it.
- Contracts with other modules:

  | Contract | Direction | State |
  |-|-|-|
  | `IRotateSessionUseCase` | we provide | Implemented by `SessionRotationService`. To be called by the login flow and MFA of Team 1 (KT2). |
  | `ISearchLoginHistoryUseCase` | we provide | Implemented by `LoginHistoryService`. To be called by an API endpoint (KT2). |
  | `ISessionRotationStore` | we use | `InMemorySessionRotationStore` for tests and the demo. Adapter to `identity.sessions` of Team 1 planned. |
  | `ISessionTokenGenerator` | we use | `CryptoSessionTokenGenerator`: `RandomNumberGenerator`, 32 bytes, Base64Url, SHA-256 hash. |
  | `ISessionSecurityAuditWriter` | we use | Test doubles only. Adapter to `IAuditWriter` of Team 12 planned. |
  | `ILoginHistorySource` | we use | `InMemoryLoginHistorySource` with synthetic events. Adapter to login audit records of Team 12 planned. |

  `Platform.SessionSecurity` references no other `Platform.*` module (checked by a test). The host connects the adapters.
- Implementation status: the module skeleton, all contracts, both use cases, the in-memory store and source, the token generator, the query validator and the address masker are implemented and covered by 121 NUnit tests. Adapters to Team 1 and Team 12, the API endpoint, DI registration and the React page are planned for KT2.

## Component architecture

The architecture diagram was presented at KT1 and will be added here with the KT2 integration.

Layers inside `backend/Platform.SessionSecurity`:

- `Contracts/`: ports and public records (`IRotateSessionUseCase`, `ISearchLoginHistoryUseCase`, `ISessionRotationStore`, `ISessionTokenGenerator`, `ISessionSecurityAuditWriter`, `ILoginHistorySource`, requests and results).
- `Domain/`: pure rules and constants (`RotationTrigger`, `SessionPrivilegeLevel`, `SessionRotationCodes`, `LoginHistoryCodes`, `LoginHistoryLimits`, `LoginHistoryQueryValidator`, `SourceAddressMasker`).
- `Application/`: `SessionRotationService`, `LoginHistoryService`. They depend only on `Contracts` and `Domain`.
- `Infrastructure/`: `InMemorySessionRotationStore`, `CryptoSessionTokenGenerator`, `InMemoryLoginHistorySource`.

## Data flow diagram

The data flow diagram was presented at KT1 and will be added here with the KT2 integration.

Trust boundaries:

| Boundary | Rule |
|-|-|
| Browser and server | The cookie and the history filter are untrusted. The subject never comes from the request; the filter is validated on the server; the new token goes only into the session cookie (`HttpOnly`, `Secure`, `SameSite`, owned by Team 1). |
| Server and stores | Only the SHA-256 hash of a token is stored. The history source is always called with the subject from the session. Audit records hold no token, hash, chain ID or full IP address. |

### Order of checks: rotation (IDN-11)

1. Missing token, subject or correlation ID, unknown trigger, or a new level of `PreAuthentication`: `InvalidRequest`, nothing is read or audited.
2. Session not found by the token hash: `SessionNotFound`.
3. Session revoked: `SessionRevoked`. Session at or past its absolute expiry: `SessionExpired`.
4. Subject check: a `Login` may start from a pre-authentication session (no subject) or a session of the same subject; every other trigger needs a session of the same subject. Otherwise `SubjectMismatch`.
5. New token, then atomic rotation in the store. The store checks the old session again inside the same atomic operation, because it can change after step 2: rotated or revoked by another request gives `RotationConflict`, absolute expiry reached gives `SessionExpired`. In both cases nothing is changed. The replacement must keep the chain ID and the absolute expiry of the old session.
6. Audit of the success. If it fails, the whole chain is revoked (also when the request is cancelled) and the result is `AuditFailed`: the old and the new session are both revoked, the new token never reaches the client, and the user has to log in again. Revoking a chain is idempotent, so it can be retried safely.

Every rejection after step 1 is audited with its code as the outcome. The client gets the same generic answer for every rejection.

### Order of checks: login history (IDN-22)

1. No subject or not authenticated: `NotAuthenticated`. Empty subject ID or no query: `InvalidRequest`.
2. Undefined outcome filter: `InvalidRequest`.
3. Missing bounds are filled in on the server: `ToUtc = now`, `FromUtc = ToUtc - 30 days`.
4. `FromUtc > ToUtc`: `InvalidTimeRange`. Range over 90 days: `RangeTooLarge`.
5. `Page < 1`: `InvalidPage`. `PageSize < 1` or over 50: `InvalidPageSize`.
6. The source is called with the subject from the session; results are newest first; addresses are masked.

In steps 1 to 5 the source is never called.

## Security requirements

- Server-side access checks: the subject of the history search comes only from `AuthenticatedSubject`, and `SearchLoginHistoryQuery` has no user field (checked by a test), so another user's history cannot be requested (SEC-01, IDOR). The subject filter is applied in the source, not after loading. A rotation for another trigger than login requires a session of the same subject.
- Input validation and sensitive data: the history filter is validated against `LoginHistoryLimits` before any read (SEC-04, SEC-05). Tokens come only from `RandomNumberGenerator`; only their SHA-256 hash is stored. Records that hold a token, hash, chain ID or full IP address override `ToString()` to hide them (AUD-03). IPv4 addresses are masked to `203.0.113.*`, IPv6 to the first four groups, and an invalid or non-canonical address to `*` (AUD-19). Audit is fail-closed (AUD-10).
- Negative scenarios: old token reused after login (session fixation); old token after MFA; revoked chain; revoked subject; two parallel rotations with the same token; expired session; audit failure; session of another subject; invalid requests; user A asking for history while user B has events in the same period; invalid ranges and paging; unauthenticated caller; invalid addresses. All are covered by tests.

### Threats, controls and tests

| Threat | Control | Test |
|-|-|-|
| Session fixation | New token after login and privilege change, old one revoked | `RotateAsync_WhenPreLoginTokenIsReusedAfterLogin_ReturnsSessionRevoked`, `RotateAsync_WhenMfaCompleted_IssuesElevatedSessionAndRevokesOld` |
| Reuse of an old token in a race | Atomic rotation, exactly one wins | `RotateAsync_WhenTwoRequestsUseSameOldToken_ExactlyOneSucceeds`, `RotateAsync_WhenManyCallsUseSameOldHash_ExactlyOneRotates` |
| Session that escapes revocation through rotation | Shared chain ID, revoke chain and subject | `RotateAsync_AfterChainIsRevoked_RejectsEveryTokenOfChain`, `RevokeAllForSubjectAsync_RevokesSessionsCreatedByRotation` |
| Endless session through repeated rotation | Absolute expiry copied, expired session not rotated | `RotateAsync_KeepsChainAndAbsoluteExpiryOfOriginalSession`, `RotateAsync_WhenSessionReachedAbsoluteExpiry_ReturnsSessionExpiredAndDoesNotRotate`, `RotateAsync_WhenReplacementChangesChainOrAbsoluteExpiry_ThrowsAndChangesNothing` |
| Session expires between the check and the rotation | Expiry checked again inside the atomic store operation | `RotateAsync_WhenOldSessionReachedAbsoluteExpiry_ReturnsExpiredAndChangesNothing`, `RotateAsync_WhenSessionExpiresBeforeStoreRotation_ReturnsSessionExpired` |
| Rotation without an audit trail | Fail-closed audit, idempotent chain revocation | `RotateAsync_WhenAuditFails_ReturnsAuditFailedAndRevokesOldAndNewSession`, `RevokeChainAsync_WhenCalledAgain_RevokesNothingAndKeepsFirstRevocationTime` |
| Secrets in logs or audit | No token, hash or chain ID in records and results | `RotateAsync_AuditEntriesAndResults_DoNotContainTokensHashesOrChainId`, `ContractsTests` |
| Predictable token | CSPRNG, 256 bits | `Generate_ThousandTokens_AreAllUnique`, `Generate_ReturnsBase64UrlTokenWith256Bits` |
| IDOR on login history | Subject from the session, filter in the source | `SearchAsync_WhenUserBHasEventsInSamePeriod_NeverReturnsThemToUserA`, `SearchLoginHistoryQuery_HasNoUserField` |
| Leak of the full IP address | Masking on the server | `SearchAsync_WhenResultIsReturned_ContainsNoFullAddressOrSubjectId`, `SourceAddressMaskerTests` |
| Query abuse (large ranges, huge pages) | 90 days, 50 per page, validation before reading | `SearchAsync_WhenQueryIsInvalid_RejectsWithCodeAndDoesNotCallSource`, `LoginHistoryQueryValidatorTests` |

### Decisions and open questions

- Privilege change triggers (`RotationTrigger`): login, completed MFA (IDN-06), re-authentication for a sensitive change (IDN-18), role change. The specification does not define a privilege change; this is the team's interpretation.
- Concurrent rotations with the same token: exactly one succeeds; the others get `RotationConflict` or `SessionRevoked`.
- Fail-closed order: rotate first, then audit; on audit failure the whole chain is revoked. The user has to log in again, but a session without an audit record never stays active. If the compensation itself fails, the exception is propagated and no token is returned; the replacement ends at the absolute expiry at the latest. This is part of the `IRotateSessionUseCase` contract.
- History limits: at most 90 days per query (30 by default), at most 50 items per page (20 by default), in `LoginHistoryLimits`.
- Open: where the adapters live, since `Platform.SessionSecurity` must not reference other modules; whether `identity.sessions` gets a chain ID column (Team 1); the event type `session.rotated` and the format of login events (Team 12).

## Evidence of work

- Changed files / commit / pull request: `backend/Platform.SessionSecurity/` (new module); `backend/Platform.Tests/SessionSecurity/`; `backend/SecurityLab.slnx` and `backend/Platform.Tests/Platform.Tests.csproj` (one line each); `docs/components/identity/IDN-11_IDN-22.md` (this file); `docs/PROJECT_COMPONENTS.md` (Team 09 row). Skeleton and contracts: pull request #131 (`feature/IDN-11-IDN-22-skeleton`, merged), including the review fix that defines expiry and audit failure behaviour in the rotation contracts. IDN-11 logic: pull request #180 (`feature/IDN-11-session-rotation`). IDN-22 logic: branch `feature/IDN-22-login-history`, pull request after #180.
- Run instructions:

  ```powershell
  dotnet build backend/SecurityLab.slnx --configuration Release
  dotnet test backend/Platform.Tests --filter "FullyQualifiedName~SessionSecurity"
  ```

- Tests and results: 121 NUnit tests in `backend/Platform.Tests/SessionSecurity/`: IDN-11 55 (`SessionRotationServiceTests` 25, `InMemorySessionRotationStoreTests` 11, `CryptoSessionTokenGeneratorTests` 5, `ContractsTests` 14), IDN-22 66 (`LoginHistoryServiceTests` 20, `LoginHistoryQueryValidatorTests` 18, `SourceAddressMaskerTests` 20, `InMemoryLoginHistorySourceTests` 8). The last local run gave **121 passed, 0 failed** for the filter above, and `Platform.Tests` 375 passed, 0 failed. The tests use in-memory implementations and test doubles; they do not confirm a real database or the integration with Team 1 and Team 12.
- Demo (KT1): run the test command above and show:
  1. `RotateAsync_WhenPreLoginTokenIsReusedAfterLogin_ReturnsSessionRevoked`: the old identifier is rejected after login.
  2. `RotateAsync_WhenTwoRequestsUseSameOldToken_ExactlyOneSucceeds`: only one of two parallel rotations wins.
  3. `RotateAsync_WhenAuditFails_ReturnsAuditFailedAndRevokesOldAndNewSession`: fail-closed audit.
  4. `SearchAsync_WhenUserBHasEventsInSamePeriod_NeverReturnsThemToUserA`: only own events.
  5. `SearchAsync_WhenEventsHaveAddresses_ReturnsOnlyMaskedAddresses`: masked addresses.
- Next steps (KT2) and external dependencies:
  1. Team 1 (IDN-02, IDN-06): agree on calling `IRotateSessionUseCase` from the login flow and after MFA, and on the adapter from `ISessionRotationStore` to `identity.sessions` (chain ID column, absolute expiry, hex or binary hash). Open pull requests #173 (IDN-07, `Session.cs`, `SessionCheckMiddleware`) and #160 (IDN-13, `LoginService`) change the same places.
  2. Team 12 (AUD-01): adapter from `ISessionSecurityAuditWriter` to `IAuditWriter`, and an `ILoginHistorySource` adapter over persisted login events. To agree with Team 12:
     - `session.rotated` is not in their event catalog yet (needs a catalog entry, a detail schema and a test); their reason codes are lower snake case (`session_revoked`), so the adapter maps our codes;
     - `auth.login` events store no source IP address, so IDN-22 masking would always show `*` until an address detail is agreed;
     - a failed `auth.login` has the actor `Anonymous` and an optional `Account` resource, so the history source has to match the user by the account resource, not by the actor.
  3. API endpoint for the login history with an explicit access policy, the subject taken from `IIdentityContext`, and DI registration.
  4. React page for IDN-22: period and outcome filters, paged list, keyboard access, error messages without secrets.
  5. Architecture and data flow diagrams in this file, updated to the implementation.
