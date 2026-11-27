# Contract: `IIdentityContext`

| | |
|-|-|
| **Owner** | Identity team (IDN-02, IDN-06) |
| **Version** | 1.0 (implemented; MFA behavior is not yet implemented) |
| **Spec reference** | Specification 3.1: identification of the subject and account status; must not expose passwords, secrets, access tokens or session identifiers |
| **Requirements** | IDN-02, IDN-06, ACC-05, SEC-01 |

## Purpose

Provides the caller identity established by the server for the current HTTP request. Consumers use `IIdentityContext.Current`; they must not read cookies, tokens, or identity headers themselves.

## Members

| Member | Type | Meaning |
|-|-|-|
| `Current` | `CallerIdentity?` | Identity from the session validated by `SessionCheckMiddleware`. `null` means no session was validated for this request. |
| `CallerIdentity.IsAuthenticated` | `bool` | `true` only when `Level` is `Active`. |
| `CallerIdentity.UserId` | `Guid?` | User id associated with the validated session; also populated for `MfaPending`. |
| `CallerIdentity.Level` | `AuthenticationLevel` | `None`, `MfaPending`, or `Active`. |
| `CallerIdentity.CurrentSessionHandle` | `Guid?` | Public session handle (not the cookie token); available for the current session. |

The implementation is `SessionIdentityContext`. It reads `ValidateSession` from the current `HttpContext.Items`, populated by `SessionCheckMiddleware` after successful session validation. It does not independently read the cookie or query the store.

### Authentication-level rule

An identity with `MfaPending` has a `UserId` and a session handle but `IsAuthenticated == false`. Consumers requiring an authenticated caller must check `IsAuthenticated`. The middleware currently validates that a session exists, is not revoked, and has not expired; it does **not** restrict `MfaPending` sessions to MFA endpoints. MFA endpoints and the policy that routes selected users through MFA have not been implemented.

## Errors

The contract does not throw to represent an unauthenticated caller. `Current` is `null` if there is no validated session; a validated `MfaPending` session has `Current` set with `IsAuthenticated == false`. Callers decide the HTTP response, commonly `401`.

## Not exposed

The identity does not contain a password or hash, session token or token hash, TOTP secret, or cookie value. Account status is not part of this contract.

## Consumers

| Team / module | Use |
|-|-|
| IDN-03 account status | Caller identity for the administrator check |
| ACC-* | Subject for access decisions |
| AUD-* | Actor for audit events |
| Controllers and use cases | Check caller identity and authentication level |

## Implementations

- `SessionIdentityContext` is registered by `Program.cs` and is the implementation used by the API.
- `StubIdentityContext` and `AnonymousIdentityContext` were removed (#261 step 1): neither was registered.

## Change history

| Version | Date | Change |
|-|-|-|
| 0.1 | 2026-10 | Initial draft |
| 1.0 | 2026-11 | Updated to the implemented `Current`/`CallerIdentity` contract and documented current MFA limits |
