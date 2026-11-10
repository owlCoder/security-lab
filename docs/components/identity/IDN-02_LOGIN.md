# IDN-02 Login with e-mail and password (US-04)

**Team:** 1 (IDN-02). **Specification requirements:** IDN-02, IDN-03 (new logins refused), SEC-02, SEC-03, AUD-01, AUD-03, AUD-04, OPS-07. Data flow: process P1 in [`IDN-02_IDN-06_DFD.md`](./IDN-02_IDN-06_DFD.md).

## Endpoint

`POST /api/auth/login`, body `{ "email": "...", "password": "..." }`

| Result | Status | Body | Cookie |
|-|-|-|-|
| Success | `200` | `{ "userId": "<guid>" }` | `SecurityLab.Session` is set |
| Wrong password, unknown e-mail, account without password, blocked or archived account | `401` | `{ "code": "invalid_credentials" }` (identical for all of them) | none |
| Missing, empty or too long input (e-mail over 320, password over 128 characters), or not valid JSON | `400` | `{ "code": "invalid_request" }` (invalid JSON gets the standard ASP.NET problem details) | none |
| Session could not be stored (database error) | `503` | `{ "code": "login_unavailable" }` | none |

Cookie `SecurityLab.Session`: `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`, expires with the session. The session token is **never** in the response body. Responses carry `Cache-Control: no-store`.

```
curl -i -X POST https://localhost:5080/api/auth/login -H "Content-Type: application/json" -d "{\"email\":\"student@example.com\",\"password\":\"<password>\"}"
```

## How it works

1. `LoginService` (`Platform.Api/Services/Identity`) validates the input and calls `ICredentialVerifier` (`Platform.Identity/Interfaces`, implemented by `CredentialVerifier` in `Platform.Identity/Services`).
2. The verifier trims and lower-cases the e-mail, reads `identity.accounts` and checks the password with BCrypt (`IPasswordHasher`, work factor 12, SHA-384 pre-hash so long passwords are not cut at 72 bytes). The hash check also runs for unknown e-mails (dummy hash), so timing does not show which e-mails exist.
3. Only an `Active` account gets a session. Blocked and archived accounts get the same `401` as a wrong password; the real reason is only in the audit event.
4. `ISessionCreator.CreateAsync(userId, AuthenticationLevel.Active)` creates a new session every time (session fixation protection). `StoreSessionCreator` saves it through `ISessionStore` in `identity.sessions`, with a lifetime of 8 hours until US-14 moves it into configuration.
5. An audit event `auth.login` is written (`success`, `failure` or `denied`) with the correlation id `HttpContext.TraceIdentifier`. It never contains the e-mail, password, hash or token.

## Database

Column `identity.accounts.password_hash` (`varchar(100)`, nullable), migration `AddAccountPasswordHash`. A null hash means the account cannot log in. Accounts must be stored with a **lower-case, trimmed** e-mail (`CredentialVerifier.NormalizeEmail`), because the unique index is case-sensitive and the lookup uses the normalized form.

To create a hash for a seed user or a test: `new BCryptPasswordHasher().Hash(password)` (`Platform.Identity.Services`, also resolvable as `IPasswordHasher` from `Platform.Identity.Interfaces`). Use only invented `example.com` accounts and throwaway passwords.

## What other stories need to know

| Story | What it needs from this change |
|-|-|
| US-03 session store | Done. `StoreSessionCreator` connects login to `ISessionStore`. The two level enums have different numeric values, so `StoreSessionCreator` maps them by name. |
| US-05 logout, US-06 session check | Use `SessionCookie.Name` and `SessionCookie.Options(...)` so the cookie name and attributes stay in one place. Logout must clear the cookie with the same attributes and revoke with `ISessionStore.RevokeByTokenAsync`. `ISessionStore.FindByTokenAsync` also returns revoked and expired sessions; `StoreSessionManager.ValidateAsync` refuses them. |
| US-07, US-08 frontend | Send the request with credentials included. Read only the status code and `code`; show one generic message for `401`. |
| US-09 seed users | Done. `DevelopmentAccountSeeder` seeds four accounts into `identity.accounts` with a BCrypt hash and lower-case e-mails (see [`DEVELOPMENT.md`](../../DEVELOPMENT.md)). |
| IDN-03 | Done. Login reads the status from `identity.accounts`, and `PUT /api/admin/accounts/{id}/status` writes to the same table (`EfAccountStatusStore`), so a blocked or archived account cannot log in. Blocking also revokes its sessions in the same transaction. See [`IDN-03_ACCOUNT_STATUS.md`](../../IDN-03_ACCOUNT_STATUS.md). |
| US-13 tests | Use an `ISessionCreator` mock (Moq) as in `Platform.Identity.Tests/Services/LoginTests.cs`. |

## Known limitations

- No CSRF token (US-15) yet. Per-account lockout (IDN-05) is in `CredentialVerifier`, see [IDN-05 lockout](IDN-05-lockout.md).
- No MFA: every successful login is `Active` until an MFA policy (IDN-06, US-12) exists.
- No registration (IDN-01): accounts exist only through seeding or direct database work.
- The login does not revoke a session that was sent with the request.
- `SessionCheckMiddleware` (US-06) validates the session, but `IIdentityContext` is still the stub.
- The DFD said "ASP.NET Core Identity" for the hash check. This implementation uses BCrypt (`BCrypt.Net-Next`) directly, as decided by the team member working on US-04. Confirm with the team (US-12 ADR).

## Tests

`Platform.Identity.Tests/Services/LoginTests.cs` (login use case and audit content, collaborators replaced by Moq mocks), `StoreSessionCreatorTests.cs` (level mapping, database failure), `SessionStoreTests.cs` (hash only, revocation) and `PasswordAndCredentialTests.cs` (BCrypt, verifier with a mocked `ICredentialStore`). The controller, status codes and cookie flags are not covered by these unit tests; they belong to HTTP-level tests (US-13). `Migrations_MatchTheModel` covers the migration.
