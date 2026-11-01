# Data flow diagram: IDN-02 Secure login and logout, IDN-06 MFA

**Team:** 1 (IDN-02 + IDN-06)

**Student:** Nikola Jovanović / PR3-2023 (story US-10, reviewed by the whole team)

**Specification requirements:** IDN-02, IDN-06 (primary), SEC-02, SEC-03, SEC-04, AUD-01, AUD-03, AUD-04, ACC-05, UC-05. Depends on IDN-03 (account status), IDN-05 (attempt limiting) and AUD (audit).

## Responsibilities and boundaries

- Processes and user scenarios:
  - A user logs in with e-mail and password and receives a server-side session (IDN-02).
  - A user with MFA enabled or required receives a `MfaPending` session first, which is promoted to `Active` only after a valid TOTP code (IDN-06).
  - Every protected request is checked against the session store; logout and revocation take effect on the next request (IDN-02, UC-05).
  - A user enrolls a TOTP authenticator by scanning a QR code and confirming the first code (IDN-06).
- Contracts with other modules:
  - Provided: `IIdentityContext`, `ISessionManagement` (code name `ISessionManager`), `IMfaPolicy` (proposed).
  - Used: `IAuditWriter`, account status from IDN-03, attempt limiting from IDN-05.
- Implementation status: see [Implementation status](#implementation-status). On this branch the contracts, fakes and stubs exist and P1 Login (US-04), P3 Session check (US-06), P4 Logout (US-05) and D2 Sessions (US-03) are implemented; P2, P5 and the `ISessionManagement` part of P4 are planned.

This file covers only the data flow diagram (US-10). The component architecture diagram is a separate story (US-11).

## Data flow diagram

```mermaid
flowchart LR
  E1["E1 User - browser"]
  E2["E2 Authenticator app - user's phone"]

  subgraph APP["Application zone: server enforces every check - TB1 is its outer edge"]
    subgraph IDM["Identity module - IDN-02 and IDN-06 - TB3"]
      P1(("1 Login"))
      P2(("2 MFA verify"))
      P5(("5 MFA enroll"))
      P3(("3 Session check"))
      P4(("4 Logout and revoke"))
    end
    OM["E3 Other modules: ACC, IDN-03, IDN-07 - in-process, via contracts"]
  end

  subgraph DATA["Data zone: PostgreSQL and log - TB2 is its outer edge"]
    D1[("D1 Accounts: password hash, status, MFA state, TOTP secret")]
    D2[("D2 Sessions: token hash, level, expiry, revoked")]
    D3[("D3 Audit log - via IAuditWriter")]
  end

  E1 <-->|"F1 email, password / session cookie or generic error"| P1
  E1 <-->|"F2 TOTP code / promotion or error"| P2
  E1 <-->|"F3 enroll request / otpauth URI once, confirm code"| P5
  E1 <-->|"F4 every protected request with cookie / 401 or result"| P3
  E1 <-->|"F5 logout request / confirmation, cookie cleared"| P4
  E1 -.-|"F14 QR scan and typed code, out-of-band"| E2

  P1 <-->|"F6 verify password, read status and MFA state"| D1
  P2 <-->|"F7 read TOTP secret, attempts, last used step"| D1
  P5 <-->|"F8 read MFA state, write TOTP secret"| D1

  P1 -->|"F9 create session"| D2
  P2 -->|"F10 promote to Active or revoke"| D2
  P3 <-->|"F11 lookup by token hash, update last activity"| D2
  P4 <-->|"F12 revoke or list"| D2

  P1 & P2 & P4 & P5 -->|"F13 audit event, no secrets"| D3

  P3 -->|"F15 IIdentityContext: UserId, Level"| OM
  OM -->|"F16 ISessionManagement: revoke all, revoke one, list"| P4

  classDef planned stroke-dasharray: 5 3,fill:#f5f5f5,color:#000;
  classDef stub fill:#fff3cd,color:#000;
  classDef implemented fill:#d4edda,color:#000;
  class P2,P5,D1 planned;
  class P1,P3,P4,D2 implemented;
  class D3,OM stub;
```

Legend: circle = process, rectangle = external actor or module, cylinder = data store, double-headed arrow = request and response of one named flow, dotted line = out-of-band channel (not a server flow). Dashed grey = planned, not implemented yet. Yellow = stub or fake exists in the repository. Green = implemented.

## Elements

### External actors

| ID | Actor | Notes |
|-|-|-|
| E1 | User (browser) | Student, teacher or administrator. Untrusted input. Teacher and administrator have mandatory MFA (proposed, to be confirmed). |
| E2 | Authenticator app | On the user's phone. Receives the TOTP secret from the QR code, shows a 6-digit code. Outside our control. |
| E3 | Other modules (ACC, IDN-03, IDN-07) | In-process consumers. They use only our contracts, never our tables. |

### Processes

| ID | Process | Entry point | Status |
|-|-|-|-|
| P1 | Login | `POST /api/auth/login` (US-04) | implemented, see `docs/IDN-02_LOGIN.md` |
| P2 | MFA verify | `POST /api/auth/mfa/verify` (proposed name, US-18) | planned |
| P5 | MFA enroll | `POST /api/auth/mfa/enroll` and `POST /api/auth/mfa/enroll/confirm` (proposed names, US-17) | planned |
| P3 | Session check | middleware on every protected request, `GET /api/auth/me` (US-06) | session check implemented (`SessionCheckMiddleware`). `GET /api/auth/me` planned |
| P4 | Logout and revoke | `POST /api/auth/logout` (US-05), `ISessionManagement` (US-16) | logout implemented, see `docs/IDN-02_LOGOUT.md`. `ISessionManagement` planned |

Process numbers follow the team work plan (P5 is listed after P2 only for layout).

### Data stores

| ID | Store | Content | Status |
|-|-|-|-|
| D1 | Accounts | Password hash (ASP.NET Core Identity), account status, MFA enabled flag, TOTP secret, failed-attempt counter, last used TOTP step | Table `identity.accounts` exists (US-02) with `id`, `email`, `status`. The API still uses `UserAccount` in memory (`IAccountRepository`, owned by IDN-03). Column `password_hash` (BCrypt) exists since US-04 and login reads it straight from `identity.accounts`; the IDN-03 in-memory repository does not use it yet. MFA columns are planned, physical names pending US-12 |
| D2 | Sessions | Hash of the token (never the token), `UserId`, created, expiry, last activity, revoked time, authentication level (`MfaPending` or `Active`), public `SessionHandle` | Table `identity.sessions` exists (US-03), used through `ISessionStore` |
| D3 | Audit log | Actor, action, target, time, outcome, correlation ID | `LoggerAuditWriter` writes to the application log. A real store is owned by the AUD team |

## Data flows

| Flow | Direction | Data | Endpoint or contract | Store / table | Status |
|-|-|-|-|-|-|
| F1 | E1 <-> P1 | E-mail and password in. Session cookie (`HttpOnly`, `Secure`, `SameSite`) or one generic error out. The token is never in the response body | `POST /api/auth/login` | none | implemented |
| F2 | E1 <-> P2 | 6-digit TOTP code in, requires a `MfaPending` cookie. Promotion or generic error out | `POST /api/auth/mfa/verify` (proposed) | none | planned |
| F3 | E1 <-> P5 | Enroll request. `otpauth://` URI shown once. Confirmation code | `POST /api/auth/mfa/enroll`, `.../enroll/confirm` (proposed) | none | planned |
| F4 | E1 <-> P3 | Cookie on every protected request, CSRF token on state-changing requests (US-15). `401` without reason or the normal response | any protected endpoint, e.g. `GET /api/auth/me` and the existing `PUT /api/admin/accounts/{id}/status` (IDN-03) | none | session check implemented (`SessionCheckMiddleware`). `IIdentityContext` is still the stub |
| F5 | E1 <-> P4 | Logout request. Confirmation and cleared cookie. Idempotent | `POST /api/auth/logout` | none | implemented |
| F6 | P1 <-> D1 | Password hash check, account status (blocked and archived are refused), MFA state | BCrypt via `IPasswordHasher`, `ICredentialVerifier` | D1 Accounts | implemented for password and status, MFA state planned |
| F7 | P2 <-> D1 | TOTP secret read, attempt counter and last used step read and updated | MFA service | D1 Accounts | planned |
| F8 | P5 <-> D1 | MFA state read, TOTP secret written after confirmation. Existing secret is never overwritten | MFA service | D1 Accounts | planned |
| F9 | P1 -> D2 | New session with a fresh identifier (session fixation protection), level `MfaPending` or `Active` | `ISessionCreator` (proposed), `StoreSessionCreator`, `ISessionStore` | D2 Sessions | implemented. Level is always `Active` until MFA exists |
| F10 | P2 -> D2 | `MfaPending` to `Active`, or revoke after too many wrong codes | session service | D2 Sessions | planned |
| F11 | P3 <-> D2 | Lookup by token hash on every request, last activity updated, expired or revoked means `401` | `ISessionManager.ValidateAsync` | D2 Sessions | implemented, last activity is not updated yet |
| F12 | P4 <-> D2 | Revoke one or all sessions, list active sessions (public handle only) | `ISessionStore.RevokeByTokenAsync` (logout), `ISessionManagement` | D2 Sessions | logout implemented, `ISessionManagement` planned |
| F13 | P1, P2, P4, P5 -> D3 | Login, logout, MFA success and failure, enrollment, revocation. Never passwords, tokens, TOTP secrets or codes. Correlation ID from `HttpContext.TraceIdentifier` | `IAuditWriter.Write(AuditEvent)` | D3 Audit log | writer implemented (application log), login writes `auth.login` and logout writes `auth.logout` events |
| F14 | E1 - E2 | QR code scanned with the phone, 6-digit code typed back. Outside the server | none | none | not a server flow |
| F15 | P3 -> E3 | `CallerIdentity`: `IsAuthenticated`, `UserId`, `Level`, `CurrentSessionHandle`. Never token, hash or cookie | `IIdentityContext.Current` | none | contract implemented, stub in Development |
| F16 | E3 -> P4 | Revoke all sessions of a user (IDN-03 block or archive), revoke one by handle and list (IDN-07) | `ISessionManager` (`ISessionManagement` contract) | D2 Sessions | interface implemented, `StoreSessionManager` registered, its revoke and list methods do nothing yet |

## Trust boundaries

| ID | Boundary | Crossed by | Rule |
|-|-|-|-|
| TB1 | Internet / browser and server | F1 to F5 | Everything from E1 is untrusted and validated on the server. The cookie carries only an opaque random token. Hiding screens in the UI is not a control. |
| TB2 | Server and database / log | F6 to F13 | Only the Identity module touches its own tables. Other modules go through contracts (F15, F16). Secrets come from user-secrets or environment, never from code. |
| TB3 | Identity module and other modules (logical, in-process) | F15, F16 | Contracts only. `IIdentityContext` never exposes passwords, tokens or session identifiers. `ISessionManagement` does not decide who may call it, the caller checks permission first. |

The phone (E2) is outside all boundaries. The TOTP secret leaves the server once, in F3, over HTTPS, and the QR code is drawn locally in the browser (no third-party service).

## Negative flows

| Scenario | Flow | Expected result |
|-|-|-|
| Wrong password or unknown account | F1 | Same status and message in both cases, no session, audit event with failure |
| Blocked or archived account | F1, F16 | No new session. Existing sessions already revoked by IDN-03 |
| Old cookie after logout, expired or revoked session, unknown token | F4 | `401` without the reason (IDN-02 criterion) |
| `MfaPending` cookie sent to a protected endpoint (UI bypassed) | F4 | Rejected, `IsAuthenticated` is false until `Active` (IDN-06 criterion) |
| Wrong TOTP code | F2 | Session stays `MfaPending`. After a limited number of attempts it is revoked and temporarily blocked |
| Same TOTP code sent twice in one time window | F2 | Second use rejected (replay) |
| Enrollment when MFA is already enabled | F3 | Rejected, existing secret is not overwritten |
| State-changing request without a valid CSRF token | F4 | Rejected |
| Logout without a valid session | F5 | Idempotent, does not reveal session state |
| Revoking another user's session handle | F16 | Returns false, indistinguishable from an unknown handle |

## Sensitive data

| Data | Where it travels | Handling |
|-|-|-|
| Password | F1 only | HTTPS only, BCrypt hash check, never logged or stored in plain text |
| Session token | cookie, F1 to F5 | Generated with a cryptographically secure generator (at least 128 bits). Only its hash is stored in D2. Never in a response body, `IIdentityContext` or logs |
| `SessionHandle` | F12, F15, F16 | Public identifier, not a secret, safe to list |
| TOTP secret | F3 once, D1 | Shown once at enrollment, stored protected (US-22), never logged or returned again |
| TOTP code | F2, F3 | Never logged. Replay and guessing limited |
| CSRF token | F4 | Sent automatically by the frontend, `SameSite` is an additional layer |

## Security requirements

- Server-side access checks: every protected request goes through P3. Endpoints without an explicit policy are denied by default (ACC-05). Privileged roles cannot reach any protected endpoint with a `MfaPending` session.
- Input validation and sensitive data: generic error messages, no secrets in logs or API responses (SEC-02, AUD-03).
- Negative scenarios: see [Negative flows](#negative-flows). Tests are planned in US-13, US-24 and US-25.

## Implementation status

Present in the repository now:

- Contracts: `IIdentityContext` (`CallerIdentity`), `ISessionManager`, `IAuditWriter`, `IAccessDecision`, `AuthenticationLevel` (`None`, `MfaPending`, `Active`).
- Stubs and fakes: `StubIdentityContext` (Development, header `X-Stub-User`), `AnonymousIdentityContext` (other environments), `LoggerAuditWriter`, `FakeIdentityContext`, `InMemorySessionManagement`.
- Consumer of our contracts: `PUT /api/admin/accounts/{id}/status` (IDN-03).
- Contract documents: `docs/contracts/IIdentityContext.md`, `docs/contracts/ISessionManagement.md`.
- Identity module skeleton: `backend/Platform.Identity` with `PlatformIdentityDbContext`, `AddIdentityModule()` and the initial migration for `identity.accounts`.
- US-04 login: `POST /api/auth/login` (`LoginController`, `LoginAccount`), BCrypt `IPasswordHasher`, `ICredentialVerifier`, column `password_hash` (migration `AddAccountPasswordHash`), session cookie `SecurityLab.Session`. Details in `docs/IDN-02_LOGIN.md`.
- US-03 session store: table `identity.sessions` (migration `AddSessions`), `ISessionStore`, `SessionStore`, `SessionTokenGenerator`. Login creates sessions through `StoreSessionCreator`.
- US-05 logout: `POST /api/auth/logout` (`LogoutController`, `LogoutAccount`), `ISessionStore.RevokeByTokenAsync`. Details in `docs/IDN-02_LOGOUT.md`.
- US-06 session check: `SessionCheckMiddleware` and `StoreSessionManager.ValidateAsync` answer `401` for a missing, unknown, revoked or expired session. Revoke and list methods of `StoreSessionManager` are not implemented yet.

Planned (this DFD describes them, but the code does not exist yet): P2, P5, the `ISessionManagement` part of P4, the real `IIdentityContext`, last activity update, MFA columns in D1, the remaining endpoints under `/api/auth`.

Open decisions that can change this diagram (to confirm with the assistant): which groups have mandatory MFA, physical table names and DbContext layout, exact MFA endpoint names, how IDN-05 attempt limiting is shared.

## Evidence of work

- Changed files / commit / pull request: `docs/IDN-02_IDN-06_DFD.md` ([PR link])
- Run instructions: open this file on GitHub, the Mermaid diagram renders in the page.
- Tests and results: not applicable to documentation. Each flow is linked to an endpoint, contract or table in [Data flows](#data-flows).
- Team review: [ ] reviewed by the whole team
- Next steps: update flow status and endpoint names as US-03 to US-06 land, add MFA details after US-12 (ADR), final update in US-28 for KT2.