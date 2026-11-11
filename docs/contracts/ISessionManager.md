# Contract: `ISessionManager`


| | |
|-|-|
| **Owner** | Identity team (IDN-02; consumers include IDN-03 and IDN-07) |
| **Version** | 1.0 (implemented) |
| **Note** | Additional application contract; not listed in specification 3.1. |
| **Requirements** | IDN-02, IDN-03, IDN-07, UC-05, SEC-03 |

## Purpose

Validates a presented session token for the request pipeline, revokes sessions, and lists a user's active sessions without exposing session tokens. The API's `SessionCheckMiddleware` calls `ValidateAsync` for protected endpoints.

## Types

| Type | Members | Meaning |
|-|-|-|
| `SessionInfo` | `Handle`, `CreatedAt`, `LastActivityAt`, `ExpiresAt` | Public session listing data. It has no token or token-hash member. |
| `ValidateSession` | `Handle`, `UserId`, `Level` | Validated session data passed to the request identity context. |

`Level` is `AuthenticationLevel` (`None`, `MfaPending`, or `Active`). A valid `MfaPending` session is returned by validation; authentication policy is applied separately by consumers.

## Methods

| Method | Implemented behavior |
|-|-|
| `Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default)` | Returns `null` for a blank, unknown, revoked, or expired token. Otherwise hashes the token with SHA-256 to find the stored session, updates `LastActivityAt`, persists it, and returns its handle, user id, and level. The raw token is not stored. |
| `Task<int> RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default)` | Revokes every session for the user whose `RevokedAt` is null and returns the number changed; returns `0` if none match. It validates that `userId` is non-empty. The current `SessionManager` does not use or audit `reason`; callers are responsible for any associated audit event. |
| `Task<bool> RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default)` | Finds a session by both handle and owner and revokes it. Returns `false` for an unknown handle or a handle belonging to another user, and `true` when the owned session exists (including if it was already revoked). Both ids must be non-empty. |
| `Task<IReadOnlyList<SessionInfo>> ListForUser(Guid userId, CancellationToken ct = default)` | Returns sessions for the user that are not revoked and have not expired. It validates that `userId` is non-empty. |

The `reason` parameter is retained for the IDN-03 integration but is currently unused by the session manager. Normal missing-session outcomes are represented by `null`, `false`, or `0`; invalid empty identifiers throw `ArgumentException`. Database failures are not converted to successful outcomes.

## Security and authorization

The contract is for trusted server-side callers and does not authorize them. Callers must establish permission first. For `RevokeSession`, the owner id must come from the trusted caller identity, not from an untrusted request body. Matching both owner and handle gives the same `false` outcome for a foreign and unknown handle.

`SessionInfo` exposes only the public handle and timestamps. It never contains the token, token hash, or cookie value. Session tokens are random opaque values; the database stores their SHA-256 hashes.

## Implementation and registration

- `SessionManager` is the database-backed implementation of validation, revocation, and listing.
- `Program.cs` registers only `SessionManager` as `ISessionManager`. The older `StoreSessionManager` stub (no-op revocation and listing) is no longer registered.
- `ValidateAsync` also refuses a session whose account is not `Active` (IDN-03, SEC-03).
- `RevokeAllForUser` is awaited by `EfAccountStatusStore` inside the status-change transaction (IDN-03); it must not be called without `await`.
- Session creation is a separate `ISessionCreator` contract implemented by `StoreSessionCreator`.

## Consumers

| Team / module | Use |
|-|-|
| IDN-02 request pipeline | `ValidateAsync` on protected requests |
| IDN-02 logout | Revokes the session identified by the presented cookie through `ISessionStore` |
| IDN-03 account status | `RevokeAllForUser` when an account becomes blocked or archived |
| IDN-07 session overview | `ListForUser` and `RevokeSession` |
| IDN-04 password reset | `RevokeAllForUser` is a recommended integration, not currently wired here |

## Verification

Identity tests cover validation/revocation/listing, middleware handling of missing and invalid sessions, and the behavior of `SessionInfo`. Relevant tests include `SessionManagerTests`, `StoreSessionManagerTests`, and `SessionCheckMiddlewareTests` in `backend/Platform.Identity.Tests` (`Services/` and `Middleware/`).

## Change history

| Version | Date | Change |
|-|-|-|
| 0.1 | 2026-10 | Initial proposal using `ISessionManagement` |
| 1.0 | 2026-11 | Updated method/type names and behavior to match the implemented `ISessionManager` |
