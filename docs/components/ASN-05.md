# Project component: ASN-05, assignment for a specific group (Team 13)

**Team:** 13

**Specification requirements:** ASN-05 (P) — assignment for a specific group; acceptance criterion: another group does not see a private assignment. A student from another group must neither see it in the list nor open or start it through a direct call. Related: ASN-16 and ACC-17 (membership checked on the server), ACC-05 (deny by default), SEC-01 (server-side authorization).

The work is split between team members. Each section below describes one part.

## Igor

To be completed.

## Đorđe Marković – group membership and visibility policy

**Student:** Đorđe Marković / PR 69/2022

### Purpose

This part defines the rule that decides whether an enrolled student may see an assignment:

- A course-scoped assignment is visible to an enrolled student in that course.
- A group-scoped assignment is visible only to an enrolled student who belongs to the assignment's group in that course.
- A missing enrollment, missing group membership, or unsupported assignment scope is denied by default.

The policy receives facts collected by its caller. Those facts must come from server-side assignment, enrollment, and membership data, never from client claims.

### Responsibilities and boundaries

| Component | Responsibility | Owner | Status |
| --- | --- | --- | --- |
| `Assignment` / `AssignmentScope` | Stores whether an assignment applies to a course or a group, and its `GroupId`. | Igor | Implemented |
| `IGroupMembership` | Answers whether a student belongs to a specific group on a specific course. | Đorđe | Implemented |
| `InMemoryGroupMembership` | In-memory membership set for tests and development, keyed by the exact course, group, and student IDs. | Đorđe | Implemented; not persistent |
| `AssignmentGroupPolicy` | Pure allow/deny decision from scope, enrollment, and membership facts, with a stable reason code. | Đorđe | Implemented and unit-tested |
| Start-attempt check | Applies the policy before a student starts an attempt. | Igor | Implemented |
| Assignment list filtering (`VisibleAssignmentsService`) | Applies the policy before returning the assignment list. | Andrija | Implemented |
| Production membership adapter and DI registration | Supplies real membership data to `IGroupMembership`. | Membership/storage owners | Pending (KT2) |

This part does not persist groups or memberships. The in-memory implementation loses its data when the process stops.

### Decision rules

| Condition | Decision | Code |
| --- | --- | --- |
| Scope is neither `Course` nor `Group` | Deny | `UnsupportedScope` |
| Student is not enrolled in the course | Deny | `NotEnrolled` |
| Scope is `Course` and the student is enrolled | Allow | `CourseStudent` |
| Scope is `Group`, student is enrolled and is a member of that group | Allow | `GroupMember` |
| Scope is `Group`, student is enrolled but is not a member of that group | Deny | `NotGroupMember` |

Enrollment is required even when a group-membership record exists, so a stale membership record cannot grant access to a student who has left the course.

### Architecture and data flow

Everything the client sends is untrusted. The server loads the assignment's `CourseId`, `Scope`, and `GroupId` from storage and checks enrollment and membership itself; only the decision leaves the server.

```mermaid
flowchart LR
  subgraph CLIENT["Client (untrusted)"]
    STUDENT["Student"]
  end

  subgraph SERVER["Server (trusted) - Platform.Assignments"]
    CALLER["Caller<br/>list (Andrija) / start attempt (Igor)"]
    A["Assignment<br/>CourseId, Scope, GroupId"]
    E["ICourseEnrollmentCheck"]
    M["IGroupMembership"]
    P["AssignmentGroupPolicy"]
    CALLER -->|"load stored assignment"| A
    CALLER -->|"is enrolled?"| E
    CALLER -->|"member of exact course/group?"| M
    CALLER -->|"scope + facts"| P
    P -->|"allow/deny + reason code"| CALLER
  end

  STORE[("Assignment and membership stores<br/>(adapters pending)")]

  STUDENT -->|"request crosses trust boundary"| CALLER
  CALLER -->|"allowed data or access denial"| STUDENT
  A -.-> STORE
  M -.-> STORE
```

### Security considerations

- The caller loads `CourseId`, `Scope`, and `GroupId` from the stored assignment; it never trusts a client-supplied group ID or membership claim.
- Group membership is checked for the exact `(courseId, groupId, studentId)` tuple.
- Enrollment and membership failures deny access. Unsupported scope values also deny access.
- The reason code is for server-side diagnostics only. Client responses do not reveal whether a private assignment exists or which group owns it.
- The same rule is used for listing and for starting an attempt, so a guessed assignment ID cannot bypass list filtering.

### Tests and verification

| Test class | Covers |
| --- | --- |
| `AssignmentGroupPolicyTests` | Course access, group-member access, non-member denial, non-enrolled denial, unsupported scopes |
| `InMemoryGroupMembershipTests` | Exact course/group/student matching, missing values, invalid input |

```powershell
dotnet test backend/Platform.Tests/Platform.Tests.csproj --filter "FullyQualifiedName~AssignmentGroupPolicyTests|FullyQualifiedName~InMemoryGroupMembershipTests"
```

All 19 focused test cases pass.

### Files

- `backend/Platform.Assignments/Contracts/IGroupMembership.cs`
- `backend/Platform.Assignments/Infrastructure/InMemoryGroupMembership.cs`
- `backend/Platform.Assignments/Application/AssignmentGroupPolicy.cs`
- `backend/Platform.Tests/Assignments/AssignmentGroupPolicyTests.cs`
- `backend/Platform.Tests/Assignments/InMemoryGroupMembershipTests.cs`

## Andrija — list of assignments visible to a student

**Student:** Andrija Gradištanac / PR 12/2022

### Responsibilities and boundaries

- Processes and user scenarios: a student asks for the list of assignments in their courses. The list contains only the assignments that `AssignmentGroupPolicy` allows for that student. A group assignment of another group is left out completely, so the list reveals nothing about it.
- What this part does:
  - `VisibleAssignmentsService.GetVisibleForStudentAsync(studentId, courseIds)`: loads the assignments of the given courses, decides visibility for each one and returns only the allowed ones, in stored order.
  - `IAssignmentVisibilityLog` (contract): records the reason code of every hidden assignment on the server side.
- What this part does NOT do: it does not check the start of an attempt (Igor, `StartAttemptService`), does not change `AssignmentGroupPolicy`, `IGroupMembership` or `InMemoryGroupMembership`, does not change any existing contract or service of the Assignments module (Team 15), does not decide availability (archived or expired assignments, ASN-04), and does not register anything in DI or add an API route.
- Contracts with other modules:
  - `IAssignmentRepository.GetForCoursesAsync` (existing, Team 15) — assignments of the given courses.
  - `ICourseEnrollmentCheck` (existing, Team 15) — active enrollment, asked once per course within one call.
  - `IGroupMembership` (existing, ASN-05) — asked only for a group assignment of an enrolled student, always with the group stored on the assignment.
  - `AssignmentGroupPolicy.DecideVisibility` (existing, ASN-05) — used as it is; `decision.Allowed` decides.
  - `IAssignmentVisibilityLog` (new) — the adapter to the server log is a next step.
- Implementation status: service and contract are implemented and covered by 12 NUnit tests with fake implementations. DI registration, the log adapter and the API route are next steps.

### Order of checks

1. An invalid `studentId` (`PrincipalId.IsValid`) or an empty course list returns an empty list without reading any assignment.
2. `GetForCoursesAsync(courseIds)` loads the assignments. `courseIds` comes from the caller and is not trusted: every assignment is checked against enrollment in its own stored `CourseId`, so a foreign course in the request opens nothing.
3. For each assignment: `isEnrolled` from `ICourseEnrollmentCheck`; `isGroupMember` from `IGroupMembership` only for a `Group` assignment of an enrolled student; then `AssignmentGroupPolicy.DecideVisibility`.
4. Allowed: added to the result. Denied: left out, and `IAssignmentVisibilityLog.RecordHiddenAsync(studentId, assignmentId, code)` records why.
5. Fail closed, consistent with `StartAttemptService`: an error in the enrollment or membership check hides only that assignment (code `CheckFailed`). Cancellation and a failure of the repository itself are passed on, not swallowed.

### Component architecture

```mermaid
flowchart LR
  API["Assignment list endpoint<br/>(next step)"]

  subgraph Assignments["Platform.Assignments (Team 15 module)"]
    S["VisibleAssignmentsService<br/>GetVisibleForStudentAsync<br/>(new, Andrija)"]
    REPO[["IAssignmentRepository<br/>(existing)"]]
    ENR[["ICourseEnrollmentCheck<br/>(existing)"]]
    GM[["IGroupMembership<br/>(ASN-05)"]]
    P["AssignmentGroupPolicy<br/>DecideVisibility<br/>(ASN-05)"]
    VL[["IAssignmentVisibilityLog<br/>(new, Andrija)"]]
    A["Assignment<br/>CourseId, Scope, GroupId"]
  end

  LOG[("Server log<br/>(adapter, next step)")]

  API -->|"studentId, courseIds"| S
  S -->|"GetForCoursesAsync"| REPO --> A
  S -->|"isEnrolled, once per course"| ENR
  S -->|"isGroupMember, Group scope only"| GM
  S -->|"Scope + facts"| P
  S -->|"hidden: assignmentId, code"| VL -.-> LOG
  S -->|"allowed assignments only"| API

  classDef andrija stroke:#c2410c,stroke-width:2px
  class S,VL andrija
```

The orange outline marks this part. The same policy is used by `StartAttemptService`, so the list and a direct start call follow one rule.

### Data flow diagram: assignment list

```mermaid
flowchart LR
  ST(["Student"])

  subgraph TB["Trust boundary: client → API (HTTP)"]
    direction LR
    P1["P1 Identity<br/>studentId from the server session"]
    P2["P2 Load assignments<br/>GetForCoursesAsync(courseIds)"]
    P3["P3 Visibility per assignment<br/>enrollment + group + AssignmentGroupPolicy"]
    P4["P4 Build the list<br/>allowed assignments only"]
  end

  D1[("D1 Assignments<br/>CourseId, Scope, GroupId")]
  D2[("D2 Course enrollment")]
  D3[("D3 Group membership")]
  D4[("D4 Server log")]

  ST -->|"list request, courseIds"| P1
  P1 -->|"invalid identity: empty list"| ST
  P1 -->|"studentId, courseIds"| P2
  P2 -->|"courseIds"| D1
  D1 -->|"assignments"| P2
  P2 -->|"assignments"| P3
  P3 -->|"(studentId, CourseId)"| D2
  D2 -->|"yes / no"| P3
  P3 -->|"(studentId, CourseId, GroupId from D1)"| D3
  D3 -->|"yes / no"| P3
  P3 -->|"hidden: assignmentId, reason code"| D4
  P3 -->|"decisions"| P4
  P4 -->|"visible assignments only"| ST
```

Assumptions at the boundary: `studentId` must come from the server session, never from the request body (the API route is a next step and must keep this). `courseIds` may come from the client and is not trusted (see Order of checks). `CourseId` and `GroupId` used in the checks always come from D1. D2 and D3 are fakes or in-memory implementations until the membership owners (ACC) provide real adapters.

### Security requirements

- Server-side access checks: every assignment in the list goes through `AssignmentGroupPolicy`; the same rule protects the start of an attempt.
- Deny by default: not enrolled, another group, no group, an unknown scope or a failed check hides the assignment.
- Input validation: invalid `studentId` or empty course list returns an empty list.
- Sensitive data: a hidden assignment is not in the result at all (no placeholder, no count); the reason code is recorded only through `IAssignmentVisibilityLog`.
- Negative scenarios: see the test table.

### Tests

`backend/Platform.Tests/Assignments/VisibleAssignmentsServiceTests.cs` — fake repository, fake enrollment, `InMemoryGroupMembership` and a recording visibility log.

| Requirement ID | Precondition | Action | Expected | Evidence (test) |
| --- | --- | --- | --- | --- |
| ASN-05 | enrolled student, course assignment of that course | get list | assignment is listed | `GetVisible_StudentSeesCourseAssignmentOfOwnCourse` |
| ASN-05 | member of `group-a`, group assignment for `group-a` | get list | assignment is listed | `GetVisible_GroupMemberSeesGroupAssignment` |
| ASN-05 | member of `group-b`; group assignment for `group-a` and course assignment of the same course | get list | only the course assignment | `GetVisible_StudentOfAnotherGroup_SeesCourseAssignmentButNotGroupAssignment` |
| ASN-05 | enrolled student in no group, group assignment | get list | empty | `GetVisible_StudentWithoutGroup_DoesNotSeeGroupAssignment` |
| ASN-05, ASN-16 | student not enrolled | get list | empty; reason `NotEnrolled` | `GetVisible_StudentNotEnrolled_SeesNothing` |
| ASN-05, SEC-01 | two courses (one not enrolled), course and group assignments of several groups | get list | exactly the allowed ones, in stored order; enrollment asked once per course | `GetVisible_MixedList_ReturnsExactlyAllowedAssignmentsInStoredOrder` |
| ASN-05 | invalid `studentId` (empty, spaces, space inside, trailing newline) | get list | empty; repository not called | `GetVisible_WithInvalidStudentId_ReturnsEmptyWithoutReadingAssignments` |
| ASN-05, ACC-05 | membership check throws | get list | only that assignment hidden (`CheckFailed`) | `GetVisible_WhenMembershipCheckFails_HidesOnlyThatAssignment` |
| ASN-05 | student of another group | get list | empty result; reason only in the log | `GetVisible_HiddenAssignment_IsOnlyInServerLog` |

Run: `dotnet test backend/SecurityLab.slnx` (the tests are in `Platform.Tests/Assignments`).

### Next steps

- DI registration (not done in this part):

  ```csharp
  builder.Services.AddScoped<VisibleAssignmentsService>();
  builder.Services.AddSingleton<IGroupMembership, InMemoryGroupMembership>();   // development
  builder.Services.AddSingleton<ICourseEnrollmentCheck, ...>();                 // real enrollment adapter
  builder.Services.AddSingleton<IAssignmentVisibilityLog, ...>();               // adapter to ILogger / Audit
  ```

- An `IAssignmentVisibilityLog` adapter that writes to the server log (and later to the Audit module).
- An API route for the student's assignment list that takes `studentId` from the session and returns only `GetVisibleForStudentAsync` results, with an HTTP negative test for a student of another group.

### Files

- `backend/Platform.Assignments/Application/VisibleAssignmentsService.cs` (new)
- `backend/Platform.Assignments/Contracts/IAssignmentVisibilityLog.cs` (new)
- `backend/Platform.Tests/Assignments/VisibleAssignmentsServiceTests.cs` (new)
