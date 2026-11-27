# Project component: IDN-02 Secure login and logout, IDN-06 MFA for selected user groups

**Team:** 1
**Student:** Denis Đuriš / PR 14/2023
**Specification requirements:** IDN-02 (primary), IDN-06, ACC-05, SEC-01, SEC-03, AUD-01, AUD-03. Consumers: IDN-03, IDN-07. Depends on IDN-01 (user entity), IDN-05 (attempt limiting), AUD (Audit module contract, AUD-01) and ADM-02 (migrations).

## Responsibilities and current status

- **Login (IDN-02): implemented.** The API accepts e-mail and password, checks credentials and account status, creates a database-backed session, and sends the opaque session token only in an HttpOnly cookie.
- **Logout (IDN-02): implemented.** The API revokes the session identified by the presented cookie and deletes the cookie. Calling logout without a usable session is idempotent.
- **Session validation (IDN-02): implemented.** Protected endpoints validate the cookie against the session store; revoked, expired, unknown, or missing sessions receive `401`. Last activity is updated on successful validation.
- **Identity context: implemented.** `IIdentityContext.Current` is populated from the session validated by middleware. A `MfaPending` identity is not authenticated, but middleware itself does not restrict it to MFA endpoints.
- **MFA (IDN-06): enrollment and verification implemented (US-17, US-18), mandatory MFA policy not (US-19).** A user with an `Active` session can enroll TOTP (`POST /api/auth/mfa/enroll`, `.../enroll/confirm`). Login of an account with MFA enabled creates a `MfaPending` session, which `POST /api/auth/mfa/verify` replaces with a new `Active` session (new token, IDN-11) after a valid code. Replay protection and the MFA lockout are enforced with conditional `UPDATE`s. Details: [IDN-02_TOTP_MFA.md](IDN-02_TOTP_MFA.md).

### Contracts and integrations

| Contract / module | Direction | Current state |
|-|-|-|
| `IIdentityContext` | we provide | Implemented by `SessionIdentityContext`; see [contract](../../contracts/IIdentityContext.md). |
| `ISessionManager` | we provide | Database-backed validation, revocation, and listing; see [contract](../../contracts/ISessionManagement.md). |
| `ISessionCreator` | internal dependency | Implemented by `StoreSessionCreator`; creates sessions through `ISessionStore`. |
| Audit (`ITrustedAuditWriter`, `ITransactionalAuditWriter`) | we use, owned by AUD | `IdentityAuditProducer` maps login, logout and MFA outcomes to the Audit catalog (`auth.login`, `auth.logout`, `auth.mfa.*`) in `audit.audit_events`. MFA enabled and MFA verified are written in the identity transaction: no record, no commit. No secrets are included. |
| Account status | we use, owned by IDN-03 | `LoginService` denies non-active accounts. IDN-03 writes the status to `identity.accounts` and revokes all sessions of a blocked or archived account in the same transaction (`EfAccountStatusStore`). |
| Access control | we use, owned by ACC-05 | Every endpoint declares an action (`RequireAccess`); MFA uses `identity.mfa.enroll`, `identity.mfa.enroll.confirm` and `identity.mfa.verify`. |
| Attempt limiting | we use, owned by IDN-05 | Per-account lockout runs inside `CredentialVerifier`, see [IDN-05 lockout](IDN-05-lockout.md). |
| User credentials | we use, owned by IDN-01 | `ICredentialVerifier`/`EfCredentialStore` read the account and password hash; passwords are checked with the configured BCrypt hasher. ASP.NET Core Identity is not used. |

Not our responsibility: user registration (IDN-01), account status rules (IDN-03), session list screen (IDN-07), shared attempt limiting (IDN-05), and audit persistence (AUD).

### Implementation status

| Part | Story | State |
|-|-|-|
| Identity/session contracts and API wiring | US-01 | Implemented; contracts have evolved from the initial proposals. |
| Identity DbContext, schema, and migrations | US-02 | Implemented. |
| Session entity, token generation, and database store | US-03 | Implemented. Raw tokens are returned only for the cookie; SHA-256 hashes are stored. |
| Login, logout, and request session check | US-04, US-05, US-06 | Implemented. |
| MFA enrollment and verification | US-17, US-18 | Implemented (`MfaService`, `identity.mfa_credentials`). |
| Mandatory MFA policy for selected groups | US-19 | Not implemented. |

## Implemented component architecture

```mermaid
flowchart LR
  Browser[Browser] -->|POST login| Auth[AuthController]
  Auth --> Login[LoginService]
  Login --> Cred[ICredentialVerifier]
  Cred --> Accounts[EfCredentialStore]
  Accounts --> DB[("PostgreSQL: accounts and sessions")]
  Login --> Creator[StoreSessionCreator]
  Creator --> Store[ISessionStore / SessionStore]
  Store --> DB
  Auth -->|HttpOnly session cookie; no token in body| Browser

  Browser -->|protected request with cookie| Middleware[SessionCheckMiddleware]
  Middleware --> Manager[SessionManager: ValidateAsync]
  Manager --> DB
  Middleware -->|validated session in HttpContext.Items| Context[SessionIdentityContext]
  Context --> Consumers[IIdentityContext consumers]

  Browser -->|POST logout| LogoutController
  LogoutController --> Logout[LogoutAccount]
  Logout --> Store
  Logout --> DB

  Consumers -->|status change revocation| Manager

  Browser -->|POST mfa/enroll, enroll/confirm, verify| Auth
  Auth --> Mfa[MfaService]
  Mfa --> MfaStore[IMfaCredentialsStore / EfMfaCredentialsStore]
  MfaStore --> DB
  Mfa --> Store
  Login -->|mfa_enabled?| MfaStore

  Login --> Audit[IdentityAuditProducer]
  Logout --> Audit
  Mfa --> Audit
  Audit --> AuditDB[("PostgreSQL: audit.audit_events")]
```

`SessionCookie` centralizes cookie name and options: HttpOnly, Secure, SameSite Strict, path `/`, and the session expiration. `StoreSessionCreator` currently issues sessions with an eight-hour absolute lifetime. `SessionStore` generates a new token and stores its SHA-256 hash with the session. `SessionCheckMiddleware` reads the cookie, bypasses endpoints marked `AllowAnonymous`, and otherwise calls `ISessionManager.ValidateAsync`.

The middleware places a `ValidateSession` in `HttpContext.Items`; `SessionIdentityContext` maps it to `CallerIdentity`. `IsAuthenticated` is true only for `Active`. Middleware accepts a valid `MfaPending` session as a validated session and does not apply endpoint-specific MFA policy.

`Program.cs` registers a single `ISessionManager`, `SessionManager`. The older `StoreSessionManager` stub (no-op list/revocation) has been deleted. `SessionManager.ValidateAsync` refuses a session whose account is not `Active`, so a blocked or archived account is refused on its very next request (IDN-03, SEC-03).

## Data flow and trust boundary

Everything received from the browser, including credentials and cookies, is untrusted. Identity and account status are established server-side.

1. Login validates request lengths/presence, normalizes e-mail, verifies the password, and rejects non-active accounts using the same public `invalid_credentials` response as other credential failures.
2. On success, the session store creates a fresh random token and stores only its hash, user id, level, timestamps, and public handle. The token is returned in the cookie, never the response body.
3. On a protected request, middleware validates the token against the database, rejects missing/unknown/revoked/expired sessions with `401`, updates last activity, and makes the validated session available to the identity context.
4. Logout looks up and revokes the session presented in the cookie before deleting the cookie. The old token is rejected on the next protected request.
5. Session revocation/listing for other modules uses `ISessionManager`; logout uses `ISessionStore.RevokeByTokenAsync`.

The cookie token and its hash, passwords, and password hashes are not included in login/logout audit events. The public session handle is not a token.

## Security behavior and gaps

### Implemented

- Login fails with the same public credential response for unknown e-mail, wrong password, missing password hash, and inactive account. Credential verification checks the password even for an unknown e-mail to reduce timing-based account discovery.
- A new token is created on login; cookie flags are configured centrally as HttpOnly, Secure, and SameSite Strict.
- Only a SHA-256 token hash is stored in the database. Session validation checks revocation and expiry on each protected request.
- `MfaPending` maps to `IsAuthenticated == false` in `IIdentityContext`.
- Session revocation by public handle checks both owner id and handle; `false` is returned for both unknown and foreign handles.
- Login, logout and MFA audit records do not include passwords, tokens, hashes, TOTP secrets or codes.
- MFA (US-17, US-18): TOTP secrets are stored protected with ASP.NET Core Data Protection; a step is accepted once (also under parallel requests); wrong codes are counted atomically and lock the second factor at the threshold; a locked second factor refuses even a correct code with the same `401 invalid_mfa`.
- A `MfaPending` session gives no Access subject, so ACC-05 denies it everywhere except `POST /api/auth/mfa/verify` (and the public login/logout actions).

### Not implemented or not enforced by this component yet

- IDN-06 mandatory MFA for selected groups (US-19) is not implemented: MFA is opt-in.
- `SessionCheckMiddleware` validates the session but does not enforce authentication level itself; endpoint authorization is the central ACC-05 middleware (`RequireAccess`).
- CSRF (SEC-03) is in US-15: every state-changing request needs a token bound to the session, see [`IDN-02_CSRF.md`](./IDN-02_CSRF.md).
- The session lifetime is currently an eight-hour absolute lifetime; configurable lifetime and inactivity expiration are not implemented.
- `ISessionManager.RevokeAllForUser` accepts `reason` but currently does not use or audit it.

## Test evidence and local verification

Identity tests cover login, logout, credential checking, session validation and middleware behavior, identity mapping (including `MfaPending` being unauthenticated) and MFA (unit tests of `MfaService` in `Services/Mfa/`: enroll, confirm, verify, lockout and audit; the PostgreSQL tests in `Integration/Mfa/`: enrollment, verification, lockout, audit, writes racing a block or revocation, and the end-to-end flow). Tests are in `backend/Platform.Identity.Tests`.

From the repository root:

```powershell
dotnet build backend\Platform.Api\Platform.Api.csproj
dotnet test backend\Platform.Identity.Tests\Platform.Identity.Tests.csproj
```

## Next steps

1. Implement the mandatory MFA policy for selected groups (US-19).
2. Align session lifetime and inactivity behavior with US-14.
3. Keep this document and the contract docs synchronized with code and tests.
