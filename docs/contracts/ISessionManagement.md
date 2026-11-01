# Contract: `ISessionManagement`

| | |
|-|-|
| **Owner** | Identity team (IDN-02, IDN-06) |
| **Version** | 0.1 (draft, pending assistant confirmation) |
| **Note** | Not listed in specification 3.1. It is an additional contract proposed by the team. |
| **Requirements** | IDN-02, IDN-03, IDN-07, UC-05, SEC-03 |

## Purpose

Lets other modules revoke, list and check session data without touching the session store. Revocation takes effect on the very next request (the session is checked in the store on every request).

## Methods

| Method | Behaviour |
|-|-|
| `Task<int> RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct)` | Revokes every active session of the user. Returns the number of sessions revoked (0 if none). Idempotent. Used by IDN-03 when an account becomes `BLOCKED` or `ARCHIVED`, and by IDN-04 after a password change (recommended). |
| `Task<bool> RevokeSessionAsync(Guid ownerUserId, Guid sessionHandle, CancellationToken ct)` | Revokes one session identified by its public handle, **only if it belongs to `ownerUserId`**. Returns `false` both when the handle does not exist and when it belongs to someone else, so callers cannot probe other users' sessions. Other sessions of the user stay valid. Used by IDN-07. |
| `bool IsSessionActive(Guid userId, Guid sessionHandle, CancellationToken ct)` | Checks whether or not session with provided handle is valid. Returns `false` if the session with specified `sessionHandle` does not exist and when `userId` does not match, meaning it belongs to some other user. Used by IDN-03 |
| `Task<IReadOnlyList<SessionInfo>> ListForUserAsync(Guid userId, CancellationToken ct)` | Active (not revoked, not expired) sessions of the user. |

`SessionInfo`: `Handle` (Guid), `CreatedAt`, `LastActivityAt`, `ExpiresAt`, `IsRevoked`. **Never** contains the token, its hash or any cookie value.

## Errors

No exceptions for normal outcomes (unknown user, nothing to revoke). `ArgumentException` for an empty `userId` or `sessionHandle`. The `reason` is logged through `IAuditWriter` by the caller, control characters are stripped and it is limited to 200 characters.

## Authorization

This contract is called **by trusted server-side modules**. It does not decide who may call it. The caller must already have checked permission (for example IDN-03 checks the administrator role, IDN-07 passes the current user's own id as `ownerUserId`). Controllers must never pass a user id taken from the request body for `RevokeSessionAsync`.

## Consumers

| Team | Use |
|-|-|
| IDN-03 account status | `RevokeAllForUserAsync`, `IsSessionActive` (replaces the no-op `ISessionRevoker`) |
| IDN-07 session overview | `ListForUserAsync`, `RevokeSessionAsync` |
| IDN-04 password reset | `RevokeAllForUserAsync` (recommended, not required by the spec) |

## Fake

`InMemorySessionManagement` with `Seed(userId)` to create a session and `IsActive(handle)` for assertions. Registered by `AddSessionsAndMfaModule()` until the real store exists.

## Contract tests (to be written in US-16)

1. Three sessions, `RevokeAllForUserAsync` returns 3 and the list is empty.
2. `RevokeSessionAsync` with the wrong owner returns `false` and the session stays active.
3. Calling `RevokeAllForUserAsync` twice returns 0 the second time.
4. `ListForUserAsync` never exposes a token or hash (type has no such member).

## Change history

| Version | Date | Change |
|-|-|-|
| 0.1 | 2026-10 | Initial draft |
