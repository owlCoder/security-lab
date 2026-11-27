# Audit module: dependency injection lifetimes

**Status:** proposal by the Audit component (AUD-01, AUD-06). It becomes a rule only after
it is agreed with the owners of `Platform.Access` and the Identity/Membership modules.
Nothing here is claimed as already accepted by them.

## Why this matters

Audit and authorization run on every request and will soon touch scoped services
(`DbContext`, the current identity, course membership). A singleton that captures a scoped
service reads one request's state on behalf of another request. In an authorization or
audit path that is a security defect, not only a bug.

## Lifetime rules

| Kind of service | Lifetime | Notes |
| --- | --- | --- |
| Read-only catalogs and registries, filled once during composition | Singleton | No per-request state. Safe for concurrent use. Registration happens before the first request; a duplicate fails at startup. |
| Anything that uses `DbContext`, the current identity, membership or a per-request policy | Scoped | Includes the audit writer once it persists to PostgreSQL. |
| Stateless helpers and validators | Singleton or transient | Only if they hold no request data. |
| Background work (outbox worker, delegation expiry) | Own scope per unit of work | Create it with `IServiceScopeFactory`; never use the root provider for scoped services. |

Forbidden:

- Injecting a scoped service into a singleton (captive dependency).
- Resolving a scoped service from the root provider.
- Relying on "last registration wins" to replace an adapter. Replace it explicitly.

## Current state (checked on `main`, commit `e7474c7`)

- `AddAccessControl()` registers `IAccessDecision` and `IAccessAuditWriter` as singletons.
  This is valid today because their dependencies are singletons too.
- It stops being valid when a real audit adapter backed by a scoped `DbContext` replaces
  `NullAccessAuditWriter`. `IAccessDecision` and `IAccessAuditWriter` must then become
  scoped together, otherwise the host will not start under validation. This change belongs
  to the Access integration and needs review by the Access team.
- `Platform.Api` now also registers the Identity services. Identity has its own `IAccessDecision`
  in the `Platform.Api.Services.Identity` namespace, separate from the Access contracts.
  *Update (IDN-06, PR #196):* Identity no longer has its own `IAuditWriter`; the singleton
  `LoggerAuditWriter` is removed. Login, logout and MFA write through the scoped
  `ITrustedAuditWriter` (via `IdentityAuditProducer`, itself scoped), and status change, unlock,
  MFA enabled and MFA verified through the singleton `ITransactionalAuditWriter` in the caller's
  transaction.
- *Update:* `AddAudit()` registers the correlation context and the AUD-06 read policy;
  `AddAudit(configuration)` adds the PostgreSQL store and the scoped `AuditWriter` behind
  `IAuditWriter` and `ITrustedAuditWriter`, plus the singleton `ITransactionalAuditWriter`.
  It is the only entry point `Platform.Api` calls for the Audit module.

## Proposed registration order

`AddAccessControl()` first, then `AddAudit()`. A later adapter that replaces
`NullAccessAuditWriter` must do so with an explicit, documented registration, not by
accident of ordering.

## How this is verified

`backend/Platform.Audit.Tests` runs with `ValidateScopes` and `ValidateOnBuild` enabled:

- `CompositionScopeTests` builds the Access and Audit registrations and resolves
  `IAccessDecision` inside a scope.
- `HostStartupScopeTests` starts the real `Platform.Api` host in the Development
  environment (strict validation is the default there) and calls `/api/health`.
- Each level has a negative test with a deliberate captive dependency, which proves the
  validation is actually active. Without it a passing test would prove nothing.

Run from the repository root:

```bash
dotnet test backend/SecurityLab.slnx --configuration Release
```

## Open questions

- Should the audit writer be scoped, or a singleton that opens its own short-lived
  `DbContext` per call through `IDbContextFactory`? Decide when the storage and the
  writer are designed.
- Explicit replacement of `NullAccessAuditWriter` and public registration of actions and
  policies are proposed in [INTEGRATION.md](INTEGRATION.md), S11-01 to S11-03.
- Who owns the final registration order in `Program.cs`? Proposal: the Audit component
  owner, with review by the Access team for any change to Access composition.
