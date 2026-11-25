# ADM-08 — Multi-course administration with delegation (N)

Specification §4.12: separation of administrative and teaching power must be verifiable. Two independent courses with explicit root administrative grants are seeded. A teacher is not an administrator merely because they teach a course; a course administrator cannot read student answers merely because they administer that course.

| Use case derived from the requirement | Implemented starter flow | Evidence / prerequisite |
|---|---|---|
| Inspect authorized course administration | Current active account + explicit active grant in exactly that course | `CourseAdministrationRequiresExplicitGrantInThisCourse` |
| Delegate limited course administration | Root course administrator grants ViewAdministration, optionally ManageSettings, to an existing active account | `DelegationRenameRevokeIsScopedAndImmediatelyEffective` |
| Edit permitted administrative metadata | Root or delegate with ManageSettings changes only display name; current course version is required | End-to-end delegation test, `CourseNameValidationAndStaleVersionPreventMutation` |
| Revoke delegation | Root for that same course revokes an active non-root grant with matching version | Root/cross-course and stale-revocation tests |
| Apply revocation without re-login | Every request re-resolves authority; final store read/write compares its exact active grant version | `RevocationRejectsPreviouslyCapturedAuthorityAtCommitBoundary`, existing-session HTTP test |
| Preserve teaching/student boundary | Grant does not create a course membership or change attempt ownership | `AdministrativeGrantDoesNotCreateTeachingMembership`; admin/delegate answer-denial scenarios |

This is permanent-until-revoked **administrative** delegation in a specific course. No expiration/window engine, temporary teaching delegation, role-assignment API, grading API or root-grant provisioning API is claimed. Those belong to their actual owners or future agreed integrations.

Capabilities:

- `ViewAdministration`: read course ID, display name, version and the caller's effective administrative capability names; never answers, grades or student lists.
- `ManageSettings`: change only course display name, with version check.
- `ManageDelegations`: seeded root administrators only. This capability cannot be delegated. No self-grant, redelegation, cross-course revocation or root revocation is allowed.

Actions: `course.admin.read`, `course.settings.update`, `course.delegation.create`, `course.delegation.revoke`; resource type `course`; policy `adm08.scoped.delegation`, version `adm08:v1`.

A delegation is Active or Revoked. Creation version is 1; revocation increments it. Concurrent writes compare the captured active authority under the same simulator lock as the mutation. An account is checked again on every request; no capability is trusted from cookie claims, headers, query or request body.

Architecture and DFD: `diagrams/adm08-architecture.svg` and `diagrams/adm08-dfd.svg`. The older diagrams describe KT1. KT2 now includes PostgreSQL persistence, real
Identity/session and ACC-01 catalog adapters, an explicit first-root operator and
supported central Audit delivery. See `KT2.md` for their exact scope, startup and
remaining provider/catalog agreements; there is no HTTP root provisioning or
claim of an ACC-01 catalog rename.
