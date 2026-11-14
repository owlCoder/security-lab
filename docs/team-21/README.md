# Team 21: ACC-01 and ACC-06

> This copy describes **local review branch `feature/acc-integration-review-v2`**, integration `00b3c47` plus parent `fe6507f` and the approved Access/IDN patch. Production and test sources match `TestResults/t21-refresh-fe6507f-9e35c7`; the test/build results below are reused evidence, not new executions on review-v2. The original branch and old review merge remain untouched. KT1 design approval is preserved; completing the local merge commit requires separate final authorization. Publication and deployment remain blocked. See [candidate evidence and transfer blockers](acc-integration/MERGE_CANDIDATE.md).

Our two requirements from the specification:

- **ACC-01** Creating and archiving a course. Archiving stops new enrollments and keeps history.
- **ACC-06** Groups and visibility per group. Members of one group do not see the closed materials of another.

Login, enrollment, materials and audit storage are other teams' work. We use our own interfaces and fake data for them until their endpoints are up.

## Where things live

Our code is in three projects of its own: Core, Infrastructure and Api. Platform.Api references `Team21.Acc.Api` and calls two methods.

```
backend/
  Platform.Api/                     shared api, we touch Program.cs (2 lines) and the csproj (1 reference)
  Team21.Acc.slnx                   open this in Visual Studio or Rider
  Team21.Acc.Core/                  no EF, no ASP.NET
    Common/                         Result, Error, BaseEntity
    Interfaces/ICurrentActor.cs     who is calling
  Team21.Acc.Infrastructure/        EF and Postgres, no ASP.NET
    Common/                         shared by both contexts: TeamDbContextBase, ITeamDbContext, connection string
    Courses/Data/                   CoursesDbContext, ICoursesDbContext, "courses" schema
    Groups/Data/                    GroupsDbContext, IGroupsDbContext, "groups" schema
  Team21.Acc.Api/                   everything HTTP
    Team21AccModule.cs              wires it all into the api
    Courses/CoursesModule.cs        ACC-01 services and endpoints
    Groups/GroupsModule.cs          ACC-06 services and endpoints
    Extensions/ResultHttpExtensions.cs
    Services/HeaderCurrentActor.cs  fake login for Development
  Team21.Acc.Tests/                 NUnit tests
docs/team-21/                       this file and our KT1 / KT2 documents
  acc06-visibility/                 ACC-06 material visibility diagrams
  acc01-acc06-tests/                Member 8 security tests: component description, diagrams, KT1 demo
```

Core knows nothing about EF or HTTP and Infrastructure knows nothing about HTTP. That's the dependency direction the spec checks for (chapter 8.2), and the compiler holds it for us.

## Branches

```
main
 ├─ feature/acc01-courses           ACC-01
 │   ├─ feature/acc01-model
 │   ├─ feature/acc01-api
 │   ├─ feature/acc01-frontend
 │   └─ feature/acc01-tests
 └─ feature/acc06-groups            ACC-06
     ├─ feature/acc06-model
     ├─ feature/acc06-groups-api
     ├─ feature/acc06-visibility
     ├─ feature/acc06-frontend
     └─ feature/acc06-tests
```

Both feature branches start with the same base commits (this setup). Integration with other teams gets `feature/acc-integration` off `main` once both features have something to connect.

- Branch off the feature you work on: `git switch feature/acc06-groups`, then `git switch -c feature/acc06-visibility`.
- Open a pull request into the feature branch when you're done. The team lead reviews it.
- To pick up new work from the feature branch: `git merge feature/acc06-groups`.
- Use merge, not rebase. Rebase rewrites history and can drop someone's commits.
- Sub branches use a hyphen, `feature/acc06-visibility`. Git can't have `feature/acc06-groups` and `feature/acc06-groups/visibility` at the same time.

## First time setup

You need the .NET 10 SDK, Node 22.12+ and Docker.

1. Start Postgres. Copy `.env.example` to `.env` in the repo root, set a password, then:
   ```bash
   docker compose up -d db
   ```
2. Put the connection string in user secrets, with the values from your `.env`:
   ```bash
   dotnet user-secrets set "ConnectionStrings:Team21" "Host=localhost;Port=5432;Database=platform;Username=platform;Password=YOUR_PASSWORD" --project backend/Team21.Acc.Infrastructure
   ```
   The api and `dotnet ef` both read it from there. The environment variable `ConnectionStrings__Team21` works as well.
3. Install the EF tool:
   ```bash
   dotnet tool install --global dotnet-ef
   ```
   Already have it? `dotnet tool update --global dotnet-ef`.
4. Build, apply migrations, run the tests and start the api:
   ```bash
   dotnet build backend/Team21.Acc.slnx
   dotnet ef database update --context CoursesDbContext --project backend/Team21.Acc.Infrastructure --startup-project backend/Team21.Acc.Infrastructure
   dotnet ef database update --context GroupsDbContext --project backend/Team21.Acc.Infrastructure --startup-project backend/Team21.Acc.Infrastructure
   dotnet test backend/Team21.Acc.Tests
   dotnet run --project backend/Platform.Api --urls http://localhost:5080 -- --environment Development
   ```
   Platform.Api has no launch settings, so without `--environment Development` it starts in Production. Production skips our user secrets and the header fake.
5. Check it's up:
   ```bash
   curl http://localhost:5080/api/health
   ```

Without the connection string the api still starts. Only our endpoints fail.

## Calling our endpoints in Development

Platform.Api now authenticates Team 21 requests through the existing IDN session cookie, `SecurityLab.Session`. The host replaces our temporary actor with `SessionCurrentActor`, which reads the validated `IIdentityContext`. `X-Test-User`, `X-Stub-User` and other identity headers do not sign a caller in or override the session user, including in Development.

Use the existing IDN login flow described in [the platform development guide](../DEVELOPMENT.md). Set `API_BASE_URL` to that local API address and use the cookie jar obtained from that flow; do not weaken the existing secure-cookie settings. For example:

```bash
curl --cookie cookies.txt "$API_BASE_URL/api/courses/21000000-0000-0000-0000-000000000001/materials"
```

The demo ids are in `Team21.Acc.Infrastructure/Groups/Fakes/DemoIds.cs`. They describe the ACC-06 authorization fixtures, not provisioned IDN login accounts or sessions. In Development the groups demo data is seeded at startup once the groups migration is applied. The two demo courses use the same ids as the ACC-01 sample courses. A real session still needs an account and course membership recognized by the current readers; their fake/deny-all implementations have not been integrated yet. A valid login alone therefore does not guarantee a 200 response for these demo ids.

If your local database was seeded before the demo courses moved to the ACC-01 ids, reset it once and apply the migrations again:

```bash
docker compose down -v
docker compose up -d db
```

| Id ends in | Who or what |
|---|---|
| `…0001` | Teacher |
| `…0002` | Assistant with review rights |
| `…000a` / `…000b` | StudentA in group A / StudentB in group B |
| `…00bb` | Blocked student |
| `…00ff` | Active account outside the course |
| `21000000-…0001` | Demo course, active (ACC-01 sample OIB-2026) |
| `21000000-…0003` | Archived demo course (ACC-01 sample OIB-2025), teacher and StudentA are members |
| `…d00a` / `…d0cc` | Assignment restricted to group A / open to the whole course |
| `…d0a0` | Assignment in the archived course. StudentA gets 404, the teacher still gets 200 |

```bash
curl --cookie cookies.txt "$API_BASE_URL/api/materials/assignment/00000000-0000-0000-0000-00000000d00a"
```

When the validated session belongs to StudentB and the account/membership fixtures match, the material restricted to group A returns 404, the same answer as for a material that doesn't exist. This is not a way to impersonate StudentB through a header.

The demo teacher manages groups under `/api/courses/{courseId}/groups`:

| Method and path | What it does |
|---|---|
| `GET /` and `GET /{groupId}` | The course's groups, one group |
| `POST /` with `{ "name": "C" }` | Creates a group. Only in an active course, names are unique among active groups |
| `GET /{groupId}/members` | Current members |
| `POST /{groupId}/members` with `{ "userId": "..." }` | Adds an active student of the course |
| `DELETE /{groupId}/members/{userId}` | Removes a member, kept as history. Members of an archived group or course are frozen |

An authenticated caller with an Active account who isn't the course's teacher gets the existing service-level 404. Access rejects a non-active authenticated account with 403 before the service, without disclosing a protected resource.

Missing, invalid, expired or revoked sessions return 401 through the existing IDN validation. MFA-pending identities do not become authenticated Team 21 actors. The Development session bypasses for `/api/courses` and `/api/materials` have been removed; bypasses for other modules are unchanged.

`HeaderCurrentActor` remains available for isolated Development module tests. The integrated Platform.Api host uses the session adapter in every environment. Services retain their own authorization checks; authentication does not grant course membership or group access.

The HTTP integration tests in `Team21.Acc.Tests/Api/SessionCurrentActorHostIntegrationTests.cs` exercise the real host in Testing with real `SessionManager`, Team 21 services and isolated EF InMemory stores. Separate small Development pipelines verify the removed bypasses without starting the full Development host, its automatic migrations or hosted seeders. These tests do not prove PostgreSQL or complete account/membership integration.

### List courses

```bash
curl -i \
  --cookie cookies.txt \
  "$API_BASE_URL/api/courses"
```

Returns `200 OK` with a JSON array of `CourseDto` objects,
sorted by course code.

Only courses the current user is authorized to view are returned.
Archived courses remain readable with `status: "Archived"`.

### Get a single course

Replace `<course-id>` with an existing course GUID.

```bash
curl -i \
  --cookie cookies.txt \
  "$API_BASE_URL/api/courses/<course-id>"
```

Responses:

- `200 OK` — the course exists and the user may view it.
- `401 Unauthorized` — no valid identity was provided.
- `403 Forbidden` — the authenticated account is not Active at the Access boundary.
- `404 Not Found` — the course does not exist or access is denied.

At the course service boundary, both missing and forbidden courses return the same
`Course not found.` message to avoid revealing resource existence.

### Development course authorization

The parent ACC-01 service authorization remains in place. After validated session identity and an Active account pass Access, Development permits viewing courses for a non-empty user ID and permits creating/archiving only for the configured demo manager ID `00000000-0000-0000-0000-000000000001`. This ID is not a login credential; sending it in a header does not authenticate a caller. Outside Development, course permissions retain the parent's deny-all fallback until real permission integration is approved.

## Adding a table

1. Write the entity in Core under your feature, for example `Team21.Acc.Core/Courses/Course.cs`, deriving from `BaseEntity`.
2. Add a `DbSet` to your feature's context and its interface: `CoursesDbContext` and `ICoursesDbContext`, or `GroupsDbContext` and `IGroupsDbContext`.
3. Keys, indexes and lengths go in an `IEntityTypeConfiguration<T>` class in Infrastructure under your feature, for example `Team21.Acc.Infrastructure/Courses/Configurations/`. It gets picked up on its own.
4. Create the migration with the context and folder named:
   ```bash
   dotnet ef migrations add AddCourses --context CoursesDbContext --output-dir Courses/Data/Migrations --project backend/Team21.Acc.Infrastructure --startup-project backend/Team21.Acc.Infrastructure
   dotnet ef migrations add AddGroups --context GroupsDbContext --output-dir Groups/Data/Migrations --project backend/Team21.Acc.Infrastructure --startup-project backend/Team21.Acc.Infrastructure
   ```
5. Read the generated migration before you commit it. A renamed property can come out as drop column plus add column.
6. Don't edit a migration someone already applied, add a new one.

Each feature has its own context, schema and migrations, the two sub teams don't share migration files.

## What every table gets

From `TeamDbContextBase` and `BaseEntity`:

- **CreatedAt and UpdatedAt** are filled on save, sync and async.
- **CreatedBy and UpdatedBy** are set in the service from the current caller: `CreatedBy = userId`.
- **CreatedAt and CreatedBy** can't be changed by an update, a save puts them back.
- **Soft delete** is done in the service. Rows with `IsDeleted` drop out of every query:
  ```csharp
  member.IsDeleted = true;
  member.DeletedAt = DateTime.UtcNow;
  member.DeletedBy = userId;
  await _context.SaveChangesAsync();
  ```
  Deleted rows are still readable with `.IgnoreQueryFilters([TeamDbContextBase.SoftDeleteFilter])`.
- **UTC dates.** Every `DateTime` is stored as UTC. The frontend sends dates with `toISOString()`.

Things to keep in mind:

- `Remove()` really deletes the row. On our tables we soft delete instead.
- A unique index needs the `IsDeleted` filter, otherwise a removed row blocks adding it back. One active member per group:
  ```csharp
  builder.HasIndex(m => new { m.GroupId, m.UserId })
      .IsUnique()
      .HasFilter("\"IsDeleted\" = false");
  ```
- An archived course is a normal row with an archived status. ACC-01 wants the history kept.
- `ExecuteUpdateAsync` skips the stamping, set `UpdatedAt` (or `DeletedAt`) in the setters yourself. `ExecuteDeleteAsync` really deletes, same as `Remove()`.
- The two schemas have no foreign keys between them. ACC-06 keeps `CourseId` as a plain column and asks ACC-01 about the course through an interface.

## Services and endpoints

Service interfaces go in Core (`Team21.Acc.Core/Courses/ICourseService.cs`), the implementation in Infrastructure (`Team21.Acc.Infrastructure/Courses/Services/CourseService.cs`). A service injects the context interface, never the concrete context:

```csharp
public CourseService(ICoursesDbContext context, ICurrentActor actor)
```

Services return `Result` or `Result<T>`:

```csharp
if (!_actor.IsAuthenticated)
    return Error.Unauthorized("Sign in first");

if (course == null)
    return Error.NotFound("Course not found");

return dto;
```

Endpoints are minimal APIs in the Api project, kept in a static class per feature (for example `CourseEndpoints`) and called from the module file:

```csharp
group.MapGet("/{id:guid}", async (Guid id, ICourseService courses, CancellationToken ct) =>
    (await courses.GetAsync(id, ct)).ToHttpResult());
```

`ToHttpResult()` gives 200 or 204 on success and ProblemDetails with the matching status on failure. `ToCreatedHttpResult()` gives 201 with a Location header.

The error message goes to the client as written, keep ids and database details out of it. Access is checked in the service on every request, hiding a button in the frontend doesn't count.

## Access control (ACC-05)

Main denies every route without access metadata, so each of our routes names an action from the central catalog:

```csharp
group.MapGet("/{id:guid}", GetCourse)
    .RequireAccess(AccessActions.CourseRead.Id);
```

The policies in `Team21.Acc.Api/Access` only check for an active account. Who may do what in a course or a group is still decided in our services.

In Development, `/api/courses` and `/api/materials` skip the session check in `SessionCheckMiddleware` so `X-Test-User` keeps working.

## What is faked

Whoever first needs one writes the interface and its fake next to their code, registered in Development only. Outside Development the readers that stand in for other teams deny everyone.

| Fake | Stands in for | Owner | Status |
|---|---|---|---|
| `X-Test-User` header | Isolated Development actor fake | IDN teams own real login/sessions | retained for module tests; Platform.Api uses the validated session adapter |
| `FakeAccountStatusReader` | Blocked / active accounts | IDN-03 team | done, `DenyAllAccountStatusReader` outside Development |
| `FakeCourseMembershipReader` | Enrollment and teacher assignment | ACC-02 / ACC-03 teams | done, `NoCourseMembershipReader` outside Development |
| `LoggingVisibilityAuditWriter` | Audit log storage | AUD team | done, writes to the application log in every environment |
| `DemoGroupsSeeder` | Groups demo data | us | done, Development only |
| Material catalog | Labs and assignments | LAB / ASN teams | not needed while the visibility write side is not ours |

A fake is a test double, not an integration. Member 9 replaces them once the other teams have endpoints.

## ACC-06 visibility diagrams

The KT1 diagrams for server-side material visibility are in [acc06-visibility/](acc06-visibility/).

- **[Component architecture diagram](acc06-visibility/component-architecture.png).** The two material endpoints, `MaterialVisibilityService`, the readers it depends on and who owns each one. Team 21 readers are real, other teams' readers are fakes in Development and deny everyone elsewhere.
- **[Data flow diagram](acc06-visibility/data-flow.png).** Who asks for a material, the checks the request goes through, the tables and modules it reads, where the audit record goes and where the trust boundaries are. Denials, including a swapped id, a direct URL and a removed member, end in 404.

The decision rules behind both diagrams are in `Team21.Acc.Infrastructure/Groups/Services/MaterialVisibilityService.cs`. The group model they read is in [ACC-06-model.md](ACC-06-model.md).

## Member 8 — ACC-01 / ACC-06 security tests (KT1)

- [Component architecture, DFD and test scope](acc01-acc06-tests/PROJECT_COMPONENT.md) — component architecture and data flow diagrams (PNG), interfaces, isolated persistence and verified work.
- [KT1 demonstration](acc01-acc06-tests/DEMO.md) — reproducible commands and explanation of expected security results.

The test component uses real ACC-01/06 services, controlled external interface doubles and isolated test stores. It does not claim completed IDN, ACC-02/03, LAB/ASN, PostgreSQL or platform HTTP integration.

After the approved Part 8 handoff in `3fca69c`, 39 additional cases originally authored in Part 9 were exported for the member responsible for comprehensive security and integration testing. Part 9 retains 40 essential cases; all 118 earlier cases, including other members' Part 5 coverage, remain unchanged. Part 5 owns ACC-06 backend group management, while Part 9 owns integration, contracts and audit. Complete original sources remain in Git history before the reduction (`0c05a16`) and in the external `Team21-Part8-Handoff.zip`, available from the handoff owner. See [the handoff inventory and evidence](acc-integration/KT1_READINESS.md#predaja-testova-delu-8) before integrating the exported cases, to avoid duplicate fixtures.

## Commits

Short, area in brackets:

```
feat(acc06): group members load once per request
fix(acc01): archived course rejects new enrollments
test(acc06): a member of group A cannot read group B material
refactor(team21): result mapping lives in one place
docs(team21): migration commands in the readme
```

## Tests

```bash
dotnet test backend/Team21.Acc.Tests
```

Each rule gets a positive and a negative test (spec chapter 8). For access rules the negative ones are the important part: wrong user, swapped id, removed member.
The tests in `Infrastructure/` use the EF in memory provider. Filtered indexes and real SQL need a test on Postgres.

## Member 9 — integration, contracts and audit (KT1)

The integrated host now uses the validated IDN session identity. The older header actor in the project tree above remains a module test double. The candidate runs session validation before Access only on server-marked Team 21 endpoints. A scoped provider maps the validated actor and account status into the parent's RequireAccess policies; other routes retain the original provider and middleware order. Account status and membership remain Development fakes / non-Development deny-all defaults. The parent already supplies the real scoped CourseStateReader in every environment; a valid session alone does not grant access.

- [Integration plan and ownership](acc-integration/INTEGRATION_PLAN.md).
- [Component architecture](acc-integration/ARCHITECTURE.md) and [request data flow](acc-integration/DFD.md) — exactly two Part 9 Mermaid diagrams, showing current and pending connections.
- [KT1 readiness and demonstration](acc-integration/KT1_READINESS.md) — authorization matrix, threats, reproducible tests, local results and publication blockers.

The current integration branch was explicitly created from `feature/acc06-groups`; its recommended PR target is that parent, rather than the original future integration-from-main plan above. The parent ACC-01 implementation and CourseStateReader are present in this isolated candidate. They remain absent from the actual integration branch; no parent/PoC change has been transferred to it. Do not implement a duplicate course model or reader.

For this milestone use the isolated test commands in the readiness document. The database setup instructions above describe a separate development workflow: no migrations were executed during these checks. The unchanged real branch at `00b3c47` retains the `3fca69c` test reduction and has **158/158 passing Team 21 cases**, with none skipped; the Team 21 Release build passed with zero warnings and errors. The earlier 197/197 result is historical evidence from before the handoff. Neither result proves a passing full CI or an authoritative IDN account-status integration.

The previous 295-case Team 21 selection belongs to a separate temporary merged worktree with an Access/IDN proof of concept. That PoC has not been transferred or merged into the actual integration branch. Its test count and behavior must be assessed separately from the current 158 cases. Full CI, publication and merge readiness remain blocked; reducing passing tests did not resolve those blockers.

The updated `fe6507f` candidate passes **259/259 safe Team 21 cases**: 182 parent cases + 40 retained Part 9 cases + 34 PoC cases + 3 new environment-composition cases. The 39 exported cases have not been restored. Production is still blocked: DenyAllAccountStatusReader returns Inactive, membership is absent, and ACC-01 permissions deny all outside Development. Neither the EF-backed IDN status source nor its synchronization with status writes has owner approval.
