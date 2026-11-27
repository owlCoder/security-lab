
# ACC-01 - Course Management Frontend

## 1. Component information

- Team: 21
- Requirement: ACC-01 - Creating and archiving courses
- Component: Frontend
- Branch: feature/acc01-frontend
- Technologies: React, TypeScript, Vite, CSS, Playwright

## 2. Responsibilities

The ACC-01 frontend provides a user interface for:

- Loading courses from the backend.
- Displaying active and archived courses.
- Creating new courses.
- Searching by course code, name and description.
- Filtering courses by status.
- Confirming and performing course archival.
- Displaying loading, success and error states.
- Retrying failed list requests.

Archived courses remain visible when returned by the backend.
The frontend does not provide permanent course deletion.

## 3. Component structure

| File | Responsibility |
|---|---|
| App.tsx | Shared application integration |
| CoursesPage.tsx | UI state and operation coordination |
| CourseForm.tsx | Course input and submission |
| CourseList.tsx | Course list rendering |
| CourseCard.tsx | Individual course rendering |
| courseRules.ts | Input normalization and validation |
| courseService.ts | HTTP communication |
| types/course.ts | TypeScript contracts |
| CoursesPage.css | Component styling |

## 4. API integration

| Method | Endpoint | Operation |
|---|---|---|
| GET | /api/courses | List visible courses |
| GET | /api/courses/{id} | Read individual course |
| POST | /api/courses | Create course |
| POST | /api/courses/{id}/archive | Archive course |

The service implements all four API operations.
The current page uses the list, create and archive operations.

After successful creation or archival, the page reloads
the course list from the backend.

The server remains the authoritative source of course data.

## 5. Input validation

| Field | Rule |
|---|---|
| Code | Required, maximum 32 characters |
| Name | Required, maximum 200 characters |
| Description | Optional, maximum 4000 characters |

Input values are trimmed and the course code is normalized
to uppercase before submission.

Client-side validation improves usability but cannot
replace server-side validation.

## 6. Error handling

| Status | Frontend behavior |
|---|---|
| 400 | Invalid request feedback |
| 401 | Authentication required |
| 403 | Permission denied feedback |
| 404 | Course unavailable or not found |
| 409 | Conflict feedback |
| 422 | Validation feedback |
| 5xx | Server error feedback |

Failed list requests clear the displayed course data
and provide a retry action.

Mutating controls are disabled during relevant pending
operations to reduce repeated requests.

## 7. Security requirements

- The backend is responsible for authentication and authorization.
- Frontend controls are not security boundaries.
- Input validation is repeated on the server.
- Unauthorized resource responses are handled without
  attempting to bypass access checks.
- Archived courses cannot be archived again through
  the normal user interface.
- Archival requires explicit user confirmation.
- Development identity headers are disabled in production builds.

The development header is enabled only when both
Vite Development mode and VITE_ACC01_DEV_AUTH are enabled.

## 8. Threat-control-test-evidence

| Threat | Frontend control | Test evidence |
|---|---|---|
| Empty required input | Client validation | T04 PASS |
| Duplicate course code | HTTP 409 handling | T15 PASS |
| Unauthenticated request | HTTP 401 handling | T12 PASS |
| Insufficient permissions | HTTP 403 handling | T13 PASS |
| Missing or inaccessible course | HTTP 404 handling | T14 PASS |
| Invalid server-side input | HTTP 422 handling | T16 PASS |
| Accidental archival | Confirmation dialog | T09, T10 PASS |
| Repeated archival through UI | Archived action hidden | T11 PASS |
| Backend failure | Error state and retry | T17, T18 PASS |

These frontend tests verify user-interface behavior
using controlled API responses. They do not independently
prove server-side security enforcement.

## 9. Automated frontend tests

Test files:

- frontend/tests/acc01/mockCourses.ts
- frontend/tests/acc01/courses.spec.ts
- frontend/tests/playwright.acc01.config.ts

| ID | Scenario | Result |
|---|---|---|
| T01 | Course list loading | PASS |
| T02 | Empty course list | PASS |
| T03 | Course creation | PASS |
| T04 | Required field validation | PASS |
| T05 | Search by name | PASS |
| T06 | Search by code | PASS |
| T07 | Active course filter | PASS |
| T08 | Archived course filter | PASS |
| T09 | Cancel archival | PASS |
| T10 | Confirm archival | PASS |
| T11 | Archived course controls | PASS |
| T12 | HTTP 401 handling | PASS |
| T13 | HTTP 403 handling | PASS |
| T14 | HTTP 404 handling | PASS |
| T15 | HTTP 409 handling | PASS |
| T16 | HTTP 422 handling | PASS |
| T17 | HTTP 500 handling | PASS |
| T18 | Retry after failed loading | PASS |

Final isolated frontend test result:

- Total: 18
- Passed: 18
- Failed: 0
- Duration: 6.7 seconds

Both course API and health endpoint responses are
intercepted by Playwright during testing.

## 10. Running locally

From the frontend directory:

```powershell
npm ci
npm run build
npm run dev
```

For isolated frontend tests:

```powershell
npm install --no-save --package-lock=false @playwright/test
npx playwright install chromium
npx playwright test --config tests/playwright.acc01.config.ts
```

The test runner starts a separate Vite server on port 5174.

Course API and health endpoint responses are mocked
within Playwright tests.

No backend process or database is required for these tests.

Playwright is installed locally without modifying the shared
package.json or package-lock.json files.

This local installation is not pinned in the shared
dependency files. Reproducible CI integration requires
agreement on the project-wide testing dependencies.

## 11. Verification evidence

Local verification performed on 2026-11-27:

- TypeScript compilation: PASS.
- Vite production build: PASS.
- Playwright frontend tests: 18 passed, 0 failed.
- Final frontend test duration: 6.7 seconds.
- Test environment isolation: PASS.
- Team21 backend tests: 117 passed, 1 skipped, 0 failed.
- Team21 course database migration: applied successfully.
- Actual backend health endpoint: HTTP 200.
- Actual frontend-to-backend course request: HTTP 401.
- Development identity request header: verified in browser.
- Frontend authentication error handling: verified.
- Git diff whitespace check: PASS.

The backend tests were executed in a separate worktree
using the ACC-01 backend integration branch.

No backend source files were modified during frontend
implementation or local verification.

## 12. Remaining integration dependencies

The shared backend access middleware currently rejects
the development identity used by ACC-01 in the tested
local configuration.

The complete authenticated frontend-to-backend workflow
has therefore not been verified.

Remaining dependencies:

- Real identity and session integration.
- Resource-based course authorization.
- Persistent audit integration.
- Authenticated end-to-end testing against the actual API.
- Shared UI permission integration when available.

The frontend test suite verifies isolated UI behavior
and must not be interpreted as completed production
authentication or full-system integration.

## 13. Diagrams

- ACC01_Component_Architecture.md
- ACC01_Data_Flow.md
