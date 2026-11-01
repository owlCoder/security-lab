# IDN-02 Logout (US-05)

**Team:** 1 (IDN-02). **Specification requirements:** IDN-02, SEC-03, AUD-01, AUD-03, AUD-04. Data flow: process P4 in `docs/IDN-02_IDN-06_DFD.md`.

## Endpoint

`POST /api/auth/logout`, no body. The session comes from the `SecurityLab.Session` cookie.

| Result | Status | Body | Cookie |
|-|-|-|-|
| Valid session, no cookie, unknown token, expired session or session already revoked | `204` | none (identical for all of them) | `SecurityLab.Session` is cleared |
| The session could not be revoked (database error) | `500` | none outside Development | kept, so the client can try again |

Responses carry `Cache-Control: no-store`.

## How it works

1. `LogoutController` passes the cookie value to `LogoutAccount`.
2. `LogoutAccount` calls `ISessionStore.RevokeByTokenAsync`, which sets `revoked_at` in `identity.sessions`. Only this session is revoked. Other sessions of the same user stay valid. An expired session is left unchanged.
3. An audit event `auth.logout` is written only when this request revoked the session, with the public session handle as the target. `revoked_at` is a concurrency token, so of two logouts at the same time (double click, two tabs) only one succeeds and the first time is kept. The event never contains the token or its hash.
4. The cookie is cleared with the same attributes it was set with (`SessionCookie.Options`).

The revocation does not use the request cancellation token, so a closed tab or a broken connection cannot leave the session valid.

The endpoint is `[AllowAnonymous]`, so logout without a valid session stays idempotent. It uses the cookie and not `IIdentityContext`, because a session in `MfaPending` must also be able to log out and the session cookie belongs to this module.

## Known limitations

- The check that the old cookie gets `401` was run by hand on a real database. An automated HTTP test is still missing (US-13).
- No CSRF token (US-15) yet. Logout accepts only `POST` and the cookie is `SameSite=Strict`.
- There is no logout button yet (US-08).

## Tests

`Platform.Tests/Identity/LogoutTests.cs` (use case, controller, cookie attributes, audit content) and `SessionStoreTests.cs` (revocation).
