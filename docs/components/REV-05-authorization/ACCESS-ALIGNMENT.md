# IDN-05 / REV-01 / REV-02 / REV-05: ACC-05 alignment

Validation date: 12 November 2026.

This is the historical PR #188 report for main. The stricter
`feature/ACC-05-deny-action-by-default` branch replaces all auth public/legacy
markers with registered RequireAccess actions; see [STRICT-ACCESS-ALIGNMENT.md](STRICT-ACCESS-ALIGNMENT.md).

## Scope and behavior

`POST /api/auth/login` now declares `[AllowPublic]` for ACC-05 and retains
`[AllowAnonymous]` for the Identity session middleware. It no longer inherits
`DelegatedAuthorization` from the controller. The existing logout and session-info
actions retain their previous delegation individually, outside this IDN-05 fix.
No Access module rules or other teams' endpoints are changed.

The request passes ACC-05's explicit public-endpoint check and then reaches the
IP limiter before password verification. IP throttling returns 429 with Retry-After
and never calls the credential verifier. Ordinary failures return 401; successful
login returns 200 with the token only in the HttpOnly session cookie.

The two previously ignored account-lockout scenarios now run in
`Platform.Identity.Tests.Controllers.LockoutEndpointTests`, against the real
Identity credential verifier, persistent store, session creation and isolated
PostgreSQL. They send attempts from different client IP addresses with a high IP
limit, then verify that a locked account cannot obtain a session even with the
correct password. The persisted lockout and absence of session rows are checked.
Unknown email and locked-account responses are identical. Account lockout uses
the existing generic 401 contract; it does not expose account existence through
429 or Retry-After. No unknown-email rows or new limiter contract are introduced.

REV-01/REV-02/REV-05 routes already require `revision.entry` through the central
Access middleware. Their services also request operation-specific decisions on
the actual course/revision. No legacy delegate is used on these seven routes;
no revision business-logic change was needed. New endpoint-metadata tests assert
the central requirement and absence of public, anonymous or delegated access.
Existing HTTP/PostgreSQL tests exercise successful workflows, missing/revoked/
expired/wrong-course permissions, inactive accounts, independent approval,
idempotency, durable denial audit and rollback on audit-storage failure.

## Validation

| Command | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| `dotnet test backend/Platform.RateLimiting.Tests/Platform.RateLimiting.Tests.csproj --no-restore` | 32 | 0 | 0 |
| `dotnet test backend/Platform.Identity.Tests/Platform.Identity.Tests.csproj --filter "TestCategory=IDN-05"` | 31 | 0 | 0 |
| `dotnet test backend/Platform.Tests/Platform.Tests.csproj --filter "FullyQualifiedName~RevisionAuthorization"` | 87 | 0 | 0 |
| `dotnet test backend/Platform.Laboratories.IntegrationTests/Platform.Laboratories.IntegrationTests.csproj` | 48 | 0 | 0 |
| Total | 198 | 0 | 0 |

The PostgreSQL test runs created and removed their own Testcontainers databases.
Machine-local TRX results are in `TestResults/access-alignment` for the Identity
and revision integration runs. The Identity test project emits a pre-existing
NU1504 warning for a duplicate MVC testing package reference; it does not prevent
the build or tests. This is targeted validation, not a full solution test run.

The earlier skip explanation about an unimplemented `ILoginAttemptLimiter` and
expected account-level 429 responses was stale. IDN-05 documentation now describes
the implemented `CredentialVerifier` / `EfCredentialStore` lockout instead.
