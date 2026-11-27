# IDN-02 CSRF protection (US-15)

**Team:** 1 (IDN-02). **Specification requirements:** SEC-03 (primary), IDN-02, SEC-01, AUD-03, OPS-02. Depends on US-04 (login cookie). Data flow: process P7, flows F1, F4, F21 and F22 in [`IDN-02_IDN-06_DFD.md`](./IDN-02_IDN-06_DFD.md).

## The rule

A request that changes state (any method except `GET`, `HEAD`, `OPTIONS`, `TRACE`) must carry a valid CSRF token when:

- it carries a valid session (cookie `SecurityLab.Session`), on any route of any module, or
- it is the login (`POST /api/auth/login`), which has no session yet (login CSRF).

Without a valid token the request is refused before anything else runs, and nothing changes: no endpoint, no Access decision, no rate-limit permit, no audit record, and the session's last activity is not updated (US-14):

| Result | Status | Body |
|-|-|-|
| Token missing, empty, forged, from another browser, issued for another session, or its cookie is missing | `403` | `{ "code": "csrf_invalid" }`, `Cache-Control: no-store` |

The token is bound to the session: it carries the SHA-256 of the session cookie it was issued with (empty without a session), inside the encrypted token (`CsrfSessionBinding`). A pair from before login, after logout, or of another session in the same browser is refused.

Deliberately not checked:

| Request | Why |
|-|-|
| `GET`, `HEAD`, `OPTIONS`, `TRACE` | They must not change state, so there is nothing to forge. |
| State-changing request without a valid session: no session cookie, or a revoked, expired or unknown one | No ambient credential to abuse. The request continues, and the session check answers it as before (`401 session_invalid` on Identity routes). Whether the session is valid is read without recording activity (`ISessionManager.PeekAsync`), and only for a request whose token failed. Development callers that use a header (`X-Test-User`) cannot be forged, because a foreign page cannot add headers. |
| `POST /api/auth/logout` | A documented exception. A stale or missing token must never keep a session alive (IDN-02), and a forged logout only signs the user out. |
| OIDC callback `/api/auth/oidc/callback` | A documented exception. The OIDC handler answers it itself (nothing is mapped there); it is a form POST from the provider, protected by `state`, `nonce` and PKCE. With a provider on the same site (local Keycloak) it would carry our session cookie, so it is exempt by path, compared as the handler compares it. |

`SameSite=Strict` on the session cookie stays as a second layer; it is not a replacement, because a sibling subdomain counts as the same site.

## Place in the pipeline

The check runs right after routing and correlation (`UseRouting`, `UseCorrelationContext`), before every session check, the ADM-08 response middleware, KT1, Access, rate limiting and authentication. Decided in the review of PR #228:

- A refused request must have no side effect. The session check, Access subject providers (Identity, ADM-08, PRG-04, revisions) and rate limiting all run later, so a forged request no longer updates `LastActivityAt` (US-14 inactivity), spends a login permit or writes an Access denial.
- An invalid session still gets `401 session_invalid` first, on every route: without a valid session the request is not refused here (see above), so the session check further on answers it.
- The request order on Identity routes is: CSRF check, session check (`401`), Access (`403` or `404`), endpoint. A caller with a valid session but no token gets `403 csrf_invalid` before Access, so the answer does not depend on the resource and tells nothing about it.
- The check runs before authentication, so it always sees an anonymous user, and the session is not an authentication scheme. That is why the token is bound to the session cookie, not to the user, and why `CsrfToken.Issue` issues every token for an anonymous user. Otherwise, with the KT1 demo on and OIDC off (the KT1 cookie scheme is then the only, default scheme), the token endpoint and login would bind the token to the KT1 user and every check would refuse it.

## Getting a token

`GET /api/auth/csrf` (public, `identity.csrf.token`):

| Cookie / body | Flags | Content |
|-|-|-|
| `__Host-SecurityLab.Csrf` | `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/` | Antiforgery cookie token (the secret half). Issued only when the browser has no valid one, so other tabs keep working. |
| `__Host-SecurityLab.CsrfToken` | `Secure`, `SameSite=Strict`, `Path=/`, readable by JavaScript | The request token. |
| body `{ "token": "..." }` | `Cache-Control: no-store` | The same request token. |

The `__Host-` prefix makes the browser accept the cookies only with `Secure`, `Path=/` and no `Domain`, so another subdomain cannot plant its own pair.

The client sends the request token back in the header `X-CSRF-TOKEN`. The server checks it against the cookie token with ASP.NET Core antiforgery (Data Protection), so there is no custom cryptography (specification 3.3). A foreign page cannot read either cookie or the response, so it cannot build the header.

The token is read only from the header. Antiforgery is registered with `SuppressReadingTokenFromFormBody = true`, so it never reads a form body to look for a token there. The check runs before the ADM-08 16 KiB body limit and every other module limit, so reading the form would buffer a body that those limits have not yet seen. A token placed in a form field is ignored, and the request gets `403 csrf_invalid`.

The request token is issued for the session cookie sent with `GET /api/auth/csrf`. When the session changes, the server issues a new one:

| Moment | What the server does |
|-|-|
| Successful login | Sets a new readable `__Host-SecurityLab.CsrfToken` for the new session in the login response, so the next request works without a retry. The cookie token stays. |
| Logout | Removes both CSRF cookies together with the session cookie. The frontend then fetches a new pair for the next login. |

## Code

| Part | File |
|-|-|
| Names, cookie options, issuing and clearing | `backend/Platform.Api/Services/Identity/CsrfToken.cs` |
| Binding to the session | `backend/Platform.Api/Services/Identity/CsrfSessionBinding.cs` (`IAntiforgeryAdditionalDataProvider`) |
| Check | `backend/Platform.Api/Middleware/CsrfProtectionMiddleware.cs`, registered in `Program.cs` right after `UseCorrelationContext` |
| Session validity without activity | `ISessionManager.PeekAsync`, implemented by `SessionManager` |
| Token endpoint, login and logout | `AuthController.Csrf` (`GET /api/auth/csrf`, response `CsrfTokenResponse`), `AuthController.Login`, `AuthController.Logout` |
| Access action and policy (ACC-05) | `IdentityAccessActions.CsrfToken`, `CsrfTokenPolicy` |
| Frontend | `frontend/src/auth/csrf.ts` (`csrfFetch`), used by `apiFetch`, `apiRequest` and the direct calls listed below. `AuthProvider` fetches the cookies when the app starts and after logout. |
| Test support | `backend/Platform.Identity.Testing` (`CsrfTokenHandler`, `CreateCsrfClient`, `CsrfPair`) |

The middleware never logs the token or the cookie (AUD-03). If it cannot tell whether the session is valid (for example the database is down), it refuses the request.

### Frontend

`csrfFetch(input, init, send?)` is a drop-in replacement for `fetch`. For a state-changing method it copies the readable cookie into `X-CSRF-TOKEN` synchronously, so requests leave at once and in the same order. On `403 csrf_invalid` it calls `GET /api/auth/csrf` and repeats the request exactly once. Any other `403` is returned unchanged.

If the repeated login still gets `403`, `LoginForm` shows "Security check failed. Refresh the page and try again.", not the message for a wrong password: the password was never checked.

Calls that change state and use `csrfFetch`: `auth/apiClient.ts` (login, logout, `request`), `api/http.ts` (`apiRequest`), `api/accounts.ts`, `api/labs.ts` (`createLab`), `services/courseService.ts`, `components/labs/EditTaskForm.tsx`, `components/CourseworkKt1.tsx`. A new call that changes state must use `csrfFetch` or one of these clients.

### Tests for other modules

A test client that calls the API with a session cookie and changes state needs a token, as the frontend does:

```csharp
using Platform.Identity.Testing;

using var client = factory.CreateCsrfClient();   // instead of factory.CreateClient()
```

`CsrfTokenHandler` fetches one pair per session cookie the client sends, because the token is bound to the session. For a request built on `TestServer.SendAsync`, fetch a pair for the same session, `CsrfPair.FetchAsync(client, sessionToken)`, and call `pair.ApplyTo(context.Request)`. A host that refuses every Access decision also refuses `GET /api/auth/csrf`; there `CsrfPair.Issue(factory.Services, sessionToken)` makes the pair inside the host. Tests that simulate an attack use a plain client.

## Tests

`backend/Platform.Identity.Tests/Integration/CsrfProtectionIntegrationTests.cs` (categories `US-15`, `SEC-03`), real pipeline and PostgreSQL:

- Attack: revoking a session with the cookie but without a token (`Origin: https://evil.example`) returns `403 csrf_invalid` and the session stays valid.
- The same request with a valid token returns `204` and revokes the session.
- A refused request does not change the session's `LastActivityAt`.
- A token from another browser, a token issued for another session, a forged or empty token, and a token without its cookie are refused.
- Login without a token returns `403` and sets no session cookie; with a token it returns `200` and issues a token for the new session, and the old token stops working.
- Logout removes both CSRF cookies; the pair of the ended session is refused on the next login.
- Logout without a token still revokes the session; `GET` with the cookie and no token is not checked; a state-changing request without the session cookie, or with a revoked or expired one, gets `401 session_invalid` (cookie removed), not the CSRF answer.
- `PUT /api/admin/accounts/{id}/status` without a token: an administrator gets `403 csrf_invalid` and no status or session changes; a non-administrator gets the same answer for an existing and an unknown account.
- The token endpoint sets both cookies with the right flags and keeps an existing cookie token.
- With the KT1 demo cookie signed in, the token from `GET /api/auth/csrf` and the one login issues both work.
- `POST /api/admin/accounts/{id}/unlock` (no body, a "simple" request) by an administrator without a token gets `403 csrf_invalid`; `lockout_end`, `failed_login_count` and the Audit log stay unchanged.

`backend/Platform.Coursework.Tests/AdministrationHttpTests.cs` (ADM-08, PostgreSQL): `PUT /api/course-administration/{courseId}/settings` with a valid session and a valid token pair, but with the token in a form field instead of `X-CSRF-TOKEN`, gets `403 csrf_invalid` with `no-store`. The request body is never read (a probe placed first in the pipeline watches it), and the course settings stay unchanged.

Frontend: `frontend/src/__tests__/csrf.test.ts`, and in `LoginForm.test.tsx` the `403` message and `403` followed by `429`.

```powershell
dotnet test backend\Platform.Identity.Tests --filter "Category=US-15"
cd frontend; npm.cmd test
```

## Limits

- Tokens depend on the Data Protection key ring. Where keys are not kept across restarts (for example a container without a mounted key folder), a restart invalidates existing tokens; the frontend gets one `403 csrf_invalid`, fetches a new token and repeats the request.
- The KT1 demo cookie (`SecurityLab.CourseworkKt1`) is not covered; that demo is Development-only and owned by its team.
