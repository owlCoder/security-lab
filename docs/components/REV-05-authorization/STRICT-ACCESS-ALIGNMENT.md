# IDN-05 / REV: strict ACC-05 branch integration

Target branch: `feature/ACC-05-deny-action-by-default`, based on `aa34548`.
This report supersedes the public-marker integration described in the historical
PR #188 report (`ACCESS-ALIGNMENT.md`) for this stricter branch.

## Problem and resulting behavior

The strict Access middleware rejects every legacy/public migration marker and
every route without exactly one access requirement. Login's old `[AllowPublic]`
therefore returned 403 before the IP limiter or credential check. Logout and
session-info routes had the same problem with `[DelegatedAuthorization]`.

All three authentication endpoints now declare exactly one `RequireAccess`:

| Endpoint | Action | Scope | Policy |
| --- | --- | --- | --- |
| POST /api/auth/login | identity.login | Public | PublicAuthenticationPolicy |
| POST /api/auth/logout | identity.logout | Public | PublicAuthenticationPolicy |
| GET /api/auth/me | identity.session.read | Authenticated | SessionReadPolicy |

`AddIdentityAuthenticationAccess()` declares the actions and policies before
`AddAccessControl()`. Public scope alone is not permission: the policy must exist,
recognize the requested action and reject unexpected resources. Login keeps
`AllowAnonymous` for the separate Identity session middleware and keeps its
`EnableRateLimiting` attribute. The strict Access middleware is unchanged.

Logout remains idempotent without a valid session, including expired/unknown
cookies. Its existing handler can only revoke the session represented by the
supplied cookie. Session read requires an active, nonempty server-validated
Identity subject. `IdentityAccessActions.SessionBound` includes session read, so
the existing `IdentitySessionSubjectProvider` resolves that subject from the real
session store rather than development headers or an anonymous fallback.

After the Access policy allows login, the unchanged IP limiter runs before
credential verification: wrong credentials return 401 within the limit, followed
by 429 with Retry-After when the IP limit is exceeded. Account lockout remains
the existing persistent generic 401 contract. No lockout, rate limiter or revision
business rules are changed.

## Revision registration and test hosts

The strict catalog/registry freeze during service composition. The old
`RegisterRevisionPolicies(IServiceProvider)` tried to mutate them afterward and
made 27 focused revision tests fail before checking any permission.

`AddRevisionAccessPolicies(IServiceCollection)` now declares revision actions
before the freeze and is shared by Program and the focused tests. Program's
duplicate inline registrations are removed. The existing `RevisionActionPolicy`
handles the resource-less `account.unlock` system action, as in the original
revision registration contract; `RevisionApprovalPolicy` remains specific to
revision approval. The deliberate missing-policy test uses a prebuilt frozen
catalog and still proves that missing policy returns Deny.

Real HTTP factories continue to boot the actual Program registrations, including
`AddCentralAccessAudit`, `AddIdentitySessionAccess`, `AddAccessControl` and
`AddAccessAspNet`. No duplicate Access stack, permissive decider, replacement
authorization policy or legacy fallback is added to those factories. Rate-limit
tests only replace credential verification/session issuance; account-lockout
and revision HTTP tests use the real modules with isolated PostgreSQL databases.

Auth routes are removed from both the old approved-public and approved-delegated
inventories. Metadata tests verify exactly one requirement, a registered policy,
the correct action scope and no migration markers on every authentication route.
Revision routes retain their single `revision.entry` requirement.

## Verification on the target branch

The requested filtered `LoginEndpointRateLimitingTests` run passed all 8 tests.
The final grouped run then passed:

| Command / group | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Platform.RateLimiting.Tests | 32 | 0 | 0 |
| Platform.Identity.Tests: IDN-05, MeIntegrationTests, LogoutIntegrationTests | 44 | 0 | 0 |
| Platform.Tests: RevisionAuthorization (including authentication-policy tests) | 100 | 0 | 0 |
| Platform.Laboratories.IntegrationTests | 48 | 0 | 0 |
| Platform.Access.Tests | 219 | 0 | 0 |
| Total | 443 | 0 | 0 |

The 11 added Access-engine cases cover public access through registered policies,
denial when a Public action has no policy, unexpected resources, absent subject,
and active/blocked/archived subjects. Existing real HTTP session tests cover valid,
missing, expired, unknown, revoked and MFA-pending sessions and idempotent logout.
All tests use the normal deny-by-default engine.

TRX evidence is machine-local in `TestResults/strict-access`. `git diff --check`
passes. Existing duplicate-package and migration-marker warnings elsewhere in
the target branch remain. This is the requested focused validation, not a full
solution CI run or migration of other teams' endpoints.
