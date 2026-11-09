# ACC-01 — Course Management API

- **Team:** 21
- **Student:** Luka Nikolic, PR131/2023
- **Specification requirement:** ACC-01 — Creating and archiving courses
- **Work branch:** `feature/acc01-api`

## 1. Responsibilities and boundaries

The ACC-01 backend exposes four ASP.NET Core Minimal API endpoints:

| Method | Route                       | Operation                                           | Success          |
| ------ | --------------------------- | --------------------------------------------------- | ---------------- |
| `POST` | `/api/courses`              | Create a course                                     | `201 Created`    |
| `POST` | `/api/courses/{id}/archive` | Archive a course                                    | `204 No Content` |
| `GET`  | `/api/courses`              | List courses visible to the caller, ordered by code | `200 OK`         |
| `GET`  | `/api/courses/{id}`         | Read one visible course                             | `200 OK`         |

The component is part of the Security Lab modular monolith and follows the `Core / Infrastructure / Api` separation:

- **Core:** `Course`, `CourseStatus`, DTOs, `ICourseService`, `ICourseAuthorization` and shared contracts.
- **Infrastructure:** `CourseService`, authorization implementations, `CoursesDbContext`, EF Core mappings and PostgreSQL persistence.
- **Api:** endpoint routing, Development identity adapter, dependency injection and `Result`-to-HTTP mapping.
- **Platform.Api:** shared host that registers the Team 21 module.

**Scope:** This component handles course creation, reading and archiving. Login/session management, enrollment, materials, group visibility, frontend features and persistent audit storage belong to other components or integration work.

**Current limitations:** Development uses `X-Test-User` and a fake course authorization policy. Outside Development, the currently registered identity adapter is anonymous and course authorization denies access. These are not production-ready identity/permission integrations.

## 2. Component architecture

The diagram groups the actual components by responsibility. Solid arrows represent calls or data access; dashed arrows represent implementation, identity or response relationships. The client-to-API and application-to-database links cross trust boundaries.

```mermaid
flowchart LR
    Client["Frontend / HTTP client"]

    subgraph Api["HTTP boundary — Platform.Api / Team21.Acc.Api"]
        direction TB
        Routes["CourseEndpoints<br/>GET list / GET by ID<br/>POST create / POST archive"]
        Actor["ICurrentActor<br/>HeaderCurrentActor in Development"]
        Mapper["ResultHttpExtensions<br/>Result to HTTP / ProblemDetails"]
    end

    subgraph Contracts["Team21.Acc.Core — contracts and domain"]
        direction TB
        IService["ICourseService<br/>Create / Archive / Get / List"]
        IAuth["ICourseAuthorization<br/>CanCreate / CanArchive / CanView"]
        Domain["Course / CourseStatus<br/>CreateCourseRequest / CourseDto"]
    end

    subgraph Application["Team21.Acc.Infrastructure — implementation"]
        direction TB
        Service["CourseService<br/>Identity, permissions, validation, orchestration"]
        Policy["DevelopmentCourseAuthorization<br/>or DenyAllCourseAuthorization"]
        EF["ICoursesDbContext / CoursesDbContext<br/>EF Core and soft-delete filter"]
    end

    subgraph Storage["Database boundary"]
        PG[("PostgreSQL<br/>courses.Courses")]
    end

    Client -->|"HTTP request"| Routes
    Routes -->|"invoke"| IService
    IService -.->|"implemented by"| Service
    Actor -->|"current caller"| Service
    Service -->|"permission check"| IAuth
    IAuth -.->|"implemented by"| Policy
    Service -->|"domain rules / DTO"| Domain
    Service -->|"query / SaveChangesAsync"| EF
    EF -->|"parameterized SQL"| PG
    Routes -->|"map Result / Error"| Mapper
    Mapper -.->|"HTTP response"| Client

    style Api fill:#f5edff,stroke:#b793df,stroke-width:2px
    style Contracts fill:#eaf3ff,stroke:#80b2e5,stroke-width:2px
    style Application fill:#e8faf3,stroke:#69bfa1,stroke-width:2px
    style Storage fill:#fff3e6,stroke:#d8a166,stroke-width:2px
    classDef box fill:#ffffff,stroke:#64748b,color:#243247
    classDef store fill:#fffaf0,stroke:#b88345,color:#243247
    class Client,Routes,Actor,Mapper,IService,IAuth,Domain,Service,Policy,EF box
    class PG store
```

### Component responsibilities

| Component                              | Responsibility                                                                                                       |
| -------------------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| `CourseEndpoints`                      | Declares four Minimal API routes; delegates operations to `ICourseService`.                                          |
| `ResultHttpExtensions`                 | Maps `Result` / `Result<T>` to success responses or ProblemDetails errors.                                           |
| `ICurrentActor` / `HeaderCurrentActor` | Supplies the caller identity; `X-Test-User` works in Development only.                                               |
| `ICourseService` / `CourseService`     | Performs authentication checks, per-operation authorization, input validation, EF Core reads/writes and DTO mapping. |
| `ICourseAuthorization`                 | Contract for `CanCreateAsync`, `CanArchiveAsync` and `CanViewAsync`.                                                 |
| `DevelopmentCourseAuthorization`       | Shared test manager can create/archive; any authenticated Development user can view courses.                         |
| `DenyAllCourseAuthorization`           | Rejects course permissions outside Development until the real authorization adapter is integrated.                   |
| `Course` / `CourseStatus`              | Domain invariants and archive lifecycle, including `CanEnroll` and preservation of archive metadata.                 |
| `CoursesDbContext`                     | Access to the `courses` PostgreSQL schema, indexes, constraints and soft-delete query filtering.                     |

## 3. Data flow diagram (DFD)

The diagram shows the four request flows and the security denial paths. The external client is untrusted. Identity and permissions are checked by the backend, not the frontend.

```mermaid
flowchart TB
    User["External user / frontend"]

    subgraph Backend["Trust boundary A — ASP.NET Core backend"]
        direction TB
        HTTP["1. CourseEndpoints<br/>Bind HTTP request"]
        Authn{"2. Authenticated<br/>non-empty user ID?"}
        Action{"3. Requested operation"}

        CreatePerm{"CanCreateAsync?"}
        Create["Validate / normalize<br/>Check duplicate / create"]

        SinglePerm{"CanViewAsync(course ID)?"}
        Single["Read course by ID<br/>Map to CourseDto"]

        List["Read non-deleted courses<br/>Order by Code"]
        Filter["CanViewAsync for every course<br/>Keep visible CourseDto items"]

        ArchivePerm{"CanArchiveAsync(course ID)?"}
        Archive["Read course / call Archive<br/>SaveChangesAsync"]

        R401["401 — Sign in first"]
        R403["403 — Create denied"]
        R404["404 — Not found or access denied"]
        R422["422 — Invalid course input"]
        R409["409 — Duplicate course code / conflict"]
        OK["HTTP response mapping<br/>ResultHttpExtensions"]
    end

    subgraph Database["Trust boundary B — persistent storage"]
        Courses[("PostgreSQL<br/>courses.Courses")]
    end

    User -->|"HTTP request"| HTTP
    HTTP --> Authn
    Authn -->|"No"| R401
    Authn -->|"Yes"| Action

    Action -->|"POST /api/courses"| CreatePerm
    CreatePerm -->|"No"| R403
    CreatePerm -->|"Yes"| Create
    Create -->|"Validation failure"| R422
    Create -->|"Duplicate / conflict"| R409
    Create <-->|"Check code / INSERT"| Courses
    Create -->|"201 + CourseDto"| OK

    Action -->|"GET /api/courses/{id}"| SinglePerm
    SinglePerm -->|"No"| R404
    SinglePerm -->|"Yes"| Single
    Single <-->|"SELECT by ID"| Courses
    Single -->|"Missing"| R404
    Single -->|"200 + CourseDto"| OK

    Action -->|"GET /api/courses"| List
    List <-->|"SELECT, soft-delete filter"| Courses
    List --> Filter
    Filter -->|"200 + visible DTO list"| OK

    Action -->|"POST /api/courses/{id}/archive"| ArchivePerm
    ArchivePerm -->|"No"| R404
    ArchivePerm -->|"Yes"| Archive
    Archive <-->|"SELECT / UPDATE"| Courses
    Archive -->|"Missing"| R404
    Archive -->|"204; archive is idempotent"| OK
    Archive -->|"Concurrent update conflict"| R409

    R401 --> OK
    R403 --> OK
    R404 --> OK
    R422 --> OK
    R409 --> OK
    OK -.->|"HTTP response"| User

    style Backend fill:#edf5ff,stroke:#86b5e8,stroke-width:2px
    style Database fill:#fff3e6,stroke:#d8a166,stroke-width:2px
    classDef process fill:#ffffff,stroke:#7189a4,color:#243247
    classDef decision fill:#fff4d8,stroke:#c99c39,color:#5b4218
    classDef denial fill:#ffebeb,stroke:#d47878,color:#822a2a
    classDef store fill:#fffaf0,stroke:#b88345,color:#243247
    class HTTP,Create,Single,List,Filter,Archive,OK,User process
    class Authn,Action,CreatePerm,SinglePerm,ArchivePerm decision
    class R401,R403,R404,R422,R409 denial
    class Courses store
```

### Data flow details

1. **Create:** The caller is authenticated, `CanCreateAsync` is checked, the request is validated and normalized, and a new `Course` is inserted. Success returns `201 Created` and `CourseDto`. Invalid input returns `422`; duplicate codes return `409`; permission denial returns `403`.
2. **Get by ID:** Authentication and `CanViewAsync(userId, courseId)` are checked _before_ loading the course. A missing or forbidden course returns the same `404 Not Found` message: `Course not found.`
3. **Get list:** The backend loads non-soft-deleted courses, sorts by `Code`, checks `CanViewAsync` for each one and returns only allowed `CourseDto` items. A signed-in caller with no visible courses gets `200 OK` and `[]`.
4. **Archive:** Authentication and `CanArchiveAsync` are checked before loading a course. The domain method `Archive(userId)` changes the status and stores archive metadata; the row is not deleted. Repeated archive requests succeed without overwriting the original archive metadata.

**Boundary A:** A request from the frontend is untrusted. The `X-Test-User` test header is accepted only in Development. The service makes its own authorization decisions even if the frontend hides restricted actions.

**Boundary B:** The database is accessed via EF Core, not directly by the client. Course queries respect the existing soft-delete query filter; archived courses are not soft-deleted and remain readable to authorized callers.

## 4. Security requirements

### Authentication and authorization

| Scenario                        | Expected behavior                                  |
| ------------------------------- | -------------------------------------------------- |
| No authenticated user           | `401 Unauthorized`                                 |
| Create without permission       | `403 Forbidden`                                    |
| Get a course without permission | `404 Not Found`                                    |
| Get a nonexistent course        | `404 Not Found` with the same public message       |
| Archive without permission      | `404 Not Found`                                    |
| List request                    | `200 OK`, containing only permitted courses        |
| Read an archived course         | `200 OK` when authorized; DTO status is `Archived` |

The identical `404` response for missing and forbidden individual resources reduces resource-existence disclosure (BOLA/IDOR risk). The list operation filters visibility per course. Production identity and permission integration are still pending; Development test headers and test permissions must not be treated as production controls.

### Input validation and data integrity

- `Code` and `Name` are mandatory; values are trimmed and the code is uppercased.
- Length limits: code **32**, name **200**, description **4000** characters.
- Duplicate course codes are rejected (`409 Conflict`); the PostgreSQL unique index also protects concurrent inserts.
- The database applies constraints, a unique index on non-soft-deleted course codes, and an EF Core soft-delete query filter.
- API responses use `CourseDto`, not the complete EF entity; archive actor and other internal fields are not exposed by this DTO.
- `CourseStatus.Archived` is a lifecycle state, not deletion. `Course.CanEnroll` becomes false for archived or soft-deleted courses; enrollment enforcement is the responsibility of the enrollment integration.
- The service uses asynchronous EF Core operations and receives `CancellationToken` values from HTTP endpoints.

## 5. Evidence of work

**Implementation:** `feature/acc01-api` → Pull Request into `feature/acc01-courses`.

**Earlier work:** PR #59 (Create/Archive) was merged into the ACC-01 integration branch. Current GET, visibility and KT1 changes are follow-up work and are not part of that already-merged PR until submitted separately.

**Main implementation files:**

- `backend/Team21.Acc.Api/Courses/CourseEndpoints.cs`
- `backend/Team21.Acc.Api/Courses/CoursesModule.cs`
- `backend/Team21.Acc.Core/Courses/ICourseService.cs`
- `backend/Team21.Acc.Core/Courses/ICourseAuthorization.cs`
- `backend/Team21.Acc.Infrastructure/Courses/Services/CourseService.cs`
- `backend/Team21.Acc.Infrastructure/Courses/Authorization/DevelopmentCourseAuthorization.cs`
- `backend/Team21.Acc.Infrastructure/Courses/Authorization/DenyAllCourseAuthorization.cs`
- `backend/Team21.Acc.Tests/Courses/CourseServiceTests.cs`
- `docs/team-21/README.md`

**Local verification (2026-11-06):**

- `dotnet build backend/Team21.Acc.slnx` — succeeded.
- `dotnet test backend/Team21.Acc.Tests` — **84 passed, 1 skipped, 0 failed**. The skipped test requires a dedicated PostgreSQL test database.
- HTTP `GET /api/courses` without identity — `401 Unauthorized`.
- HTTP `GET /api/courses` with Development test identity — `200 OK`, including an archived course.
- HTTP `GET /api/courses/{id}` for an existing archived course — `200 OK`.
- HTTP `GET /api/courses/{id}` without identity — `401 Unauthorized`.
- HTTP `GET /api/courses/{id}` for a nonexistent GUID — `404 Not Found`.
- Service tests also cover denied reads, visibility filtering, sorting and archived history. The denied-read `404` has not been separately demonstrated through an HTTP request.

**Development test identity:** `00000000-0000-0000-0000-000000000001` via `X-Test-User` header.

## 6. Remaining integration work

- Replace Development-only identity and course authorization with real authentication and resource-based permissions from the responsible teams.
- Integrate audit persistence through the AUD module; do not treat the current code as full audit integration.
- Consider database-side permission filtering and pagination for large course catalogs; the current list reads candidate courses then checks permissions in memory.
- Add PostgreSQL-backed integration tests for the database-specific race-condition behavior and run the currently skipped dedicated PostgreSQL test.
- Re-run CI and request review when the current changes are committed and submitted to `feature/acc01-courses`.
