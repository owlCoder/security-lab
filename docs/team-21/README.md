# Team 21: ACC-01 and ACC-06

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

Until real authentication is integrated, the API supports the
`X-Test-User` header in the Development environment.

Shared development manager ID:

`00000000-0000-0000-0000-000000000001`

### List courses

```bash
curl -i \
  -H "X-Test-User: 00000000-0000-0000-0000-000000000001" \
  http://localhost:5080/api/courses
```

Returns `200 OK` with a JSON array of `CourseDto` objects,
sorted by course code.

Only courses the current user is authorized to view are returned.
Archived courses remain readable with `status: "Archived"`.

### Get a single course

Replace `<course-id>` with an existing course GUID.

```bash
curl -i \
  -H "X-Test-User: 00000000-0000-0000-0000-000000000001" \
  http://localhost:5080/api/courses/<course-id>
```

Responses:

- `200 OK` — the course exists and the user may view it.
- `401 Unauthorized` — no valid identity was provided.
- `404 Not Found` — the course does not exist or access is denied.

Both missing and forbidden courses return the same
`Course not found.` message to avoid revealing resource existence.

### Development authorization

- Any authenticated development user may view courses.
- Only the shared development manager may create or archive courses.
- An invalid, missing, or ambiguous `X-Test-User` header does not authenticate a user.
- The header is ignored outside Development.
- Outside Development, course permissions currently use a deny-all fallback until real authorization is integrated.

This header is only a development test mechanism and must not
be treated as production authentication.

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

Only the `X-Test-User` header exists so far. The rest are planned: whoever first needs one writes the interface and its fake next to their code, registered in Development only.

| Fake                       | Stands in for                     | Owner                 | Status  |
| -------------------------- | --------------------------------- | --------------------- | ------- |
| `X-Test-User` header       | Login and sessions                | IDN teams             | done    |
| Account status             | Blocked / active accounts         | IDN-03 team           | planned |
| Course membership and role | Enrollment and teacher assignment | ACC-02 / ACC-03 teams | planned |
| Material catalog           | Labs and assignments              | LAB / ASN teams       | planned |
| Audit writer               | Audit log storage                 | AUD team              | planned |

A fake is a test double, not an integration. Member 9 replaces them once the other teams have endpoints.

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
