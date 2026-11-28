# Platform.Identity: where files go

These rules apply to every file in this project, whether a person or a coding agent writes it. Claude Code reads
this file through `CLAUDE.md` in the same folder. Codex, Cursor and other agents read `AGENTS.md` directly.

Before you finish a change, check every file you added or moved against the checklist at the end.

## 1. Identity or Api

`Platform.Identity` holds the Identity logic. `Platform.Api` holds the HTTP layer and the glue between modules.

| Goes into `Platform.Identity` | Stays in `Platform.Api` |
|---|---|
| services (`LoginService`, `SessionManager`, `SessionCookie`, `CsrfToken`) | controllers (`Controllers/Identity/AuthController.cs`) and endpoints (`Endpoints/Identity/AccountEndpoints.cs`) |
| interfaces (`ISessionStore`, `ILoginService`) | middleware (`Middleware/SessionCheckMiddleware.cs`, `Middleware/CsrfProtectionMiddleware.cs`) |
| models, enums, exceptions | request/response models of controllers (`Contracts/Identity/LoginRequest.cs`) |
| entities, DbContexts, EF configurations, EF stores, migrations | rate limiting (`Modules/Identity/RateLimiting/`) |
| options (`IdentityLockoutOptions`) | OIDC handler (`Modules/Identity/Federation/OidcFederationExtensions.cs`) |
| DI registration (`IdentityModule`) | Access registration of Identity actions (`Security/IdentityAccess/`) |

CSRF and `SessionCookie` go into Identity even though they touch HTTP: they are services, not middleware. Identity
already has `FrameworkReference Microsoft.AspNetCore.App`.

Identity must never reference `Platform.Api`. If an Identity service needs something from Api, the thing moves to
Identity. Example: the `HttpContext.Items` key for the session is `Services/Sessions/SessionHttpContextItems`, and
`SessionCheckMiddleware` in Api reads it from there.

## 2. Folders: `<Layer>/<Feature>/<File>.cs`

The namespace follows the folder. MFA is the model:

```text
Platform.Identity/Services/Mfa/MfaService.cs                namespace Platform.Identity.Services.Mfa;
Platform.Identity/Interfaces/Mfa/IMfaCredentialsStore.cs    namespace Platform.Identity.Interfaces.Mfa;
Platform.Identity/Persistence/Mfa/EfMfaCredentialsStore.cs  namespace Platform.Identity.Persistence.Mfa;
Platform.Identity/Options/Mfa/MfaLockoutOptions.cs          namespace Platform.Identity.Options.Mfa;
```

Layers:

- `Composition`: DI registration (same idea as `Platform.Audit/Composition`)
- `Development`: dev-only seeding and database setup
- `Entities`
- `Enums`
- `Exceptions`
- `Helpers`
- `Interfaces`
- `Models`
- `Options`
- `Persistence`
- `Services`

Features:

| Feature | Covers |
|---|---|
| `Access` | subject resolvers, Access policies of Identity actions |
| `Accounts` | registration, status change, unlock, admin directory |
| `Audit` | Identity audit producer |
| `Credentials` | password, hashing, credential check, password change |
| `Csrf` | CSRF token and its binding to the session |
| `Development` | dev accounts and dev subject resolver (`Models/Development/`, `Services/Development/`) |
| `Federation` | OIDC and external logins |
| `Login` | login, logout, lockout after failed attempts |
| `Mfa` | TOTP, backup codes, MFA lockout |
| `PasswordReset` | reset token request and redemption |
| `Sessions` | sessions, session token, cookie, caller identity |

Exceptions to `<Layer>/<Feature>/`. These stay flat, because every feature uses them:

- `Entities/` (`Entities/Session.cs`, `Entities/UserAccount.cs`);
- shared enums `Enums/AccountRole.cs`, `Enums/AccountStatus.cs`, `Enums/AuthLevel.cs`;
- DbContexts in `Persistence/` (`Persistence/PlatformIdentityDbContext.cs`, `Persistence/AccountDbContext.cs`);
- EF configurations in `Persistence/Configurations/`;
- `Migrations/`. Do not move or rename migrations. EF finds them by the `Platform.Identity.Migrations` namespace.

A feature folder that does not exist yet is created by the first file that needs it. Do not add placeholder files.

## 3. If you add X, it goes to Y

| You add | Folder | Example |
|---|---|---|
| a service | `Services/<Feature>/` | `Services/Sessions/SessionManager.cs` |
| an interface | `Interfaces/<Feature>/` | `Interfaces/Sessions/ISessionStore.cs` |
| an EF store (implements a store interface) | `Persistence/<Feature>/` | `Persistence/Credentials/EfCredentialStore.cs` |
| a model, result or command | `Models/<Feature>/` | `Models/Login/LoginResult.cs` |
| a feature enum | `Enums/<Feature>/` | `Enums/Login/LoginOutcome.cs` |
| an enum every feature uses | `Enums/` | `Enums/AuthLevel.cs` |
| an exception | `Exceptions/<Feature>/` | `Exceptions/Sessions/AccountNotActiveException.cs` |
| options | `Options/<Feature>/` | `Options/Login/IdentityLockoutOptions.cs` |
| a helper | `Helpers/<Feature>/` | `Helpers/Mfa/TotpHelpers.cs` |
| an entity | `Entities/` | `Entities/MfaCredentials.cs` |
| an EF configuration | `Persistence/Configurations/` | `Persistence/Configurations/SessionConfiguration.cs` + `ApplyConfiguration` in the DbContext |
| a migration | `Migrations/` (generated by `dotnet ef migrations add`) | `Migrations/20261101185858_AddSessions.cs` |
| DI registration | `Composition/` | `Composition/IdentityModule.cs` |
| dev-only seeding | `Development/` | `Development/DevelopmentAccountSeeder.cs` |
| a request or response body of a controller | **Api**, `Platform.Api/Contracts/Identity/` | `Contracts/Identity/LoginRequest.cs` |

## 4. Registration goes into `IdentityModule`

Every Identity service, store and option is registered in `IdentityModule.AddIdentityModule(configuration)`.
**Never in `Platform.Api/Program.cs`.** `Program.cs` only calls `builder.Services.AddIdentityModule(builder.Configuration);`.

Today `Program.cs` still has Identity registrations and calls two `AddIdentityModule` overloads
(`IdentityModule.cs` and `IdentityServiceCollectionExtensions.cs`). Both are being merged into
`Composition/IdentityModule.cs` through #261. When you move a service from Api, move its `AddScoped<...>` line from
`Program.cs` into `IdentityModule` in the same PR.

What stays in `Program.cs`: Api glue such as the keyed `AuthoringSessionSubjectResolver`, rate limiting, OIDC and
the middleware pipeline.

## 5. Transition: files are being moved (#261)

The project is in the middle of a reorganisation. Many files are still flat (`Services/SessionStore.cs`,
`Interfaces/ICredentialStore.cs`, `Models/CredentialRecord.cs`) or still in `Platform.Api`
(`Platform.Api/Services/Identity/`, `Platform.Api/Interfaces/Identity/`, `Platform.Api/Models/Identity/`,
`Platform.Api/Enums/Identity/`). Issue #261 moves them, and each team member moves their own files.

- **A new file goes straight to its target folder**, even if its neighbours are still flat or in Api.
- Do not copy a file you are not moving into its target folder. A move is never "create the new one, delete the
  old one".

**Moving an existing file:**

1. Use `git mv` in the terminal, in a commit that changes nothing else.
2. Change the namespace (and the `using` lines that follow from it, in the whole solution) in the next commit.
3. Check `git diff -M --stat --summary origin/main...HEAD`: the file must show as a rename (`R`), not as
   delete + create.
4. `dotnet build backend/SecurityLab.slnx` and `dotnet test` pass.
5. The PR is merged with a merge commit, **not squash**. Squash merges both commits and loses the rename.

VS Code and Visual Studio do not run `git mv` when you move a file in their explorer. JetBrains Rider does.

## 6. Decisions already made (do not add a duplicate)

| Topic | Decision | State |
|---|---|---|
| `IIdentityContext` | The main one is Api's (`CallerIdentity? Current`). It moves to `Interfaces/Sessions/IIdentityContext.cs`. The unused `Platform.Identity/Interfaces/IIdentityContext.cs` (`FindByIdAsync`) is deleted. Do not add another one. | deletion in progress (#265), move through #261 |
| `AuthLevel` / `AuthenticationLevel` | The main enum is `Platform.Identity.Enums.AuthLevel`. Api `AuthenticationLevel` goes away; `None` becomes `null` / `IsAuthenticated: false`. Use `AuthLevel` in new code. | decided, not done yet |
| `IAccessDecision` (Api, account administration) | Renamed to `IAccountAdministrationDecision`, implementation `AdminOnlyAccountAdministrationDecision`. It collided with `Platform.Access.Contracts.IAccessDecision`. | in progress (#265) |
| session key in `HttpContext.Items` | `Services/Sessions/SessionHttpContextItems.Session`. Do not read `SessionCheckMiddleware.SessionItemKey` from Identity. | in progress (#265) |

Before you add a new interface, enum or model, search the whole `backend/` for the name. If it exists in another
module (`Platform.Access`, Laboratories), pick a name that does not collide.

## 7. Tests

Test rules are in [`../Platform.Identity.Tests/AGENTS.md`](../Platform.Identity.Tests/AGENTS.md). Tests follow the
same `<Layer>/<Feature>/` layout: `Services/Mfa/MfaService.cs` is tested in
`Platform.Identity.Tests/Services/Mfa/MfaServiceTests.cs`.

## 8. Checklist before you finish

- [ ] Every new file is in `<Layer>/<Feature>/` (or in one of the flat exceptions in section 2) and its namespace
      matches the folder.
- [ ] Nothing in my change belongs in Api (controller, endpoint, middleware, request/response body, rate limiting,
      OIDC handler, `Security/IdentityAccess`).
- [ ] Nothing in Identity references `Platform.Api`.
- [ ] New registrations are in `IdentityModule`, not in `Program.cs`.
- [ ] Moved files were moved with `git mv` in a separate commit, and
      `git diff -M --stat --summary origin/main...HEAD` shows them as `R`.
- [ ] No new type duplicates one from section 6.
- [ ] `dotnet build backend/SecurityLab.slnx` and `dotnet test backend/Platform.Identity.Tests` pass.
