# Contract: `IIdentityContext`

| | |
|-|-|
| **Owner** | Identity team (IDN-02, IDN-06) |
| **Version** | 0.1 (draft, pending assistant confirmation) |
| **Spec reference** | Specification 3.1: identification of the subject and account status; must not expose passwords, secrets, access tokens or session identifiers |
| **Requirements** | IDN-02, IDN-06, ACC-05, SEC-01 |

## Purpose

Tells any module who the caller of the current request is. The value is derived on the server from the session cookie. Consumers must never read cookies or tokens themselves.

## Members

| Member | Type | Meaning |
|-|-|-|
| `IsAuthenticated` | `bool` | `true` only when the session is valid **and** its level is `Active`. |
| `UserId` | `Guid?` | Id of the user. `null` when there is no valid session. |
| `Level` | `AuthenticationLevel` | `None`, `MfaPending`, `Active`. |
| `CurrentSessionHandle` | `Guid?` | Public handle of the current session (not the token). Used by IDN-07 to mark "this session". |

### Rule that matters for IDN-06

A session in `MfaPending` has `UserId` set but **`IsAuthenticated == false`**. Consumers that check `IsAuthenticated` first (as the IDN-03 use-case already does) therefore reject half-logged-in sessions without any extra code. Only the MFA endpoints look at `Level`.

## Errors

The contract does not throw for "not logged in". It returns `IsAuthenticated == false`. The API layer turns that into `401` with no detail about the reason.

## Not exposed (on purpose)

Password hash, session token or its hash, TOTP secret, cookie values, account status (a blocked account has all sessions revoked, so a valid session implies an active account).

## Consumers

| Team / module | Use |
|-|-|
| IDN-03 account status | caller identity for the administrator check |
| ACC-* | subject for `IAccessDecision` |
| AUD-* | actor for audit events |
| all controllers | 401 vs allowed |

## Fake

`FakeIdentityContext` (settable `UserId` and `Level`, `IsAuthenticated` computed by the rule above). Use it in tests until the real implementation exists.

## Change history

| Version | Date | Change |
|-|-|-|
| 0.1 | 2026-10 | Initial draft |
