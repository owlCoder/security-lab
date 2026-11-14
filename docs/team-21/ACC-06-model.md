# ACC-06 model: groups, members and material visibility

**Team:** 21

**Student:** Vuk [surname] / PR 34/2023 (team member 2)

**Specification requirements:** ACC-06 (primary); related: ASN-05, ACC-05, ACC-04, SEC-01, spec 2.1

## Responsibilities and boundaries

- Data model of ACC-06 in the `groups` schema: groups, group members, material audiences and group access to restricted materials.
- EF Core mapping in `GroupsDbContext` (keys, foreign keys, indexes, enum storage, soft delete).
- Not part of this component: business rules and endpoints (member 5), the visibility decision (member 6), tests (member 8).
- External references are stored as plain columns without foreign keys: `CourseId` (ACC-01, `courses` schema), `UserId` (IDN), `MaterialId` (LAB / ASN).

## Where the code is

```
backend/Team21.Acc.Core/Groups/
  Group.cs, GroupMember.cs, MaterialAudience.cs, GroupMaterialAccess.cs
  GroupStatus.cs, MaterialType.cs, MaterialVisibility.cs
backend/Team21.Acc.Infrastructure/Groups/
  Configurations/   one IEntityTypeConfiguration per table, picked up automatically
  Data/             GroupsDbContext and IGroupsDbContext with the DbSets
```

## Tables (schema `groups`)

Every table also has the `BaseEntity` columns: `Id`, `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`, `IsDeleted`, `DeletedAt`, `DeletedBy`.

| Table | Columns | Keys and indexes |
| --- | --- | --- |
| `Groups` | `CourseId`, `Name` (100), `Status` (`Active` / `Archived`), `ArchivedAt`, `ArchivedBy`, `xmin` as `Version` | PK `Id`; index `CourseId`; unique `(CourseId, Name)` where active and not deleted |
| `GroupMembers` | `GroupId`, `UserId` | PK `Id`; FK `GroupId` → `Groups`; index `UserId`; unique `(GroupId, UserId)` where not deleted |
| `MaterialAudiences` | `CourseId`, `MaterialType` (`Assignment` / `Attachment`), `MaterialId`, `Visibility` (`CourseWide` / `RestrictedToGroups`), `xmin` as `Version` | PK `Id`; index `CourseId`; unique `(MaterialType, MaterialId)` where not deleted |
| `GroupMaterialAccess` | `MaterialAudienceId`, `GroupId` | PK `Id`; FK → `MaterialAudiences`; FK → `Groups`; index `GroupId`; unique `(MaterialAudienceId, GroupId)` where not deleted |

Relationships: `Groups` 1–0..* `GroupMembers`; `Groups` 1–0..* `GroupMaterialAccess` *–1 `MaterialAudiences` (many-to-many between groups and restricted materials). All foreign keys use `Restrict`; rows are soft deleted, never removed.

History: joining a group is `CreatedAt` / `CreatedBy` of the member row, leaving is the soft delete (`DeletedAt` / `DeletedBy`). Re-joining creates a new row. The same applies to granting and revoking material access. Removed rows are read with `IgnoreQueryFilters([TeamDbContextBase.SoftDeleteFilter])`.

Default deny: a material with `RestrictedToGroups` and no active access rows is visible to no student. Members of archived groups and removed members never count.

### ER diagram

_To be added._

## Rules for the group service (member 5)

The model stores data; these checks belong in the service and return `Error` values:

| Rule | Suggested error |
| --- | --- |
| Caller is authenticated and is a teacher of the course | `Unauthorized` / `Forbidden` |
| Course exists and is not archived (ask ACC-01) | `NotFound` / `Conflict` |
| Name is trimmed, 1–100 characters, no control characters | `Validation` |
| Name not used by another active group of the course (also enforced by the unique index) | `Conflict` |
| Archived group cannot be renamed, gain members or get material access | `Conflict` |
| Archiving sets `Status`, `ArchivedAt`, `ArchivedBy`; archiving twice changes nothing | — |
| Student is enrolled in the course before joining (ask ACC-02) | `Validation` |
| No second active membership (also enforced by the unique index) | `Conflict` |
| Removing a member is a soft delete with `DeletedAt` / `DeletedBy` | — |
| Material access only for an active group of the same course as the material | `Conflict` |
| Unknown `MaterialType` or `Visibility` value is rejected | `Validation` |
| Concurrent change of the same group or audience (`DbUpdateConcurrencyException`) | `Conflict` |
| `CourseId` of a group never changes | — |

## Readers for the visibility check (member 6)

Two read-only contracts in `Team21.Acc.Core/Groups/`, implemented in `Team21.Acc.Infrastructure/Groups/Services/` and registered in `GroupsModule.AddGroups()`. They do no access checks themselves and must not be exposed directly through an endpoint.

| Contract | Method | Returns |
| --- | --- | --- |
| `IGroupMembershipReader` | `GroupsOfAsync(accountId, courseId)` | Active memberships (`GroupMembership`: group id, name, course, joined at) in active groups of the course |
| `IMaterialAccessRuleReader` | `FindAsync(MaterialRef)` | `MaterialAccessRule` (course, visibility, allowed group ids) or `null` when the material has no rule |
| `IMaterialAccessRuleReader` | `ListByCourseAsync(courseId)` | All rules of a course |

`AllowedGroupIds` contains only active access rows of active groups of the same course. A rule with `RestrictedToGroups` and an empty list means no student sees the material.

The underlying queries, for reference. Active groups of a user in a course:

```csharp
db.GroupMembers
    .Where(m => m.UserId == userId
             && m.Group.CourseId == courseId
             && m.Group.Status == GroupStatus.Active)
    .Select(m => m.GroupId)
```

Groups allowed to see a restricted material (soft-deleted access rows are filtered out automatically):

```csharp
db.GroupMaterialAccesses
    .Where(a => a.MaterialAudience.MaterialType == type
             && a.MaterialAudience.MaterialId == materialId
             && a.Group.Status == GroupStatus.Active)
    .Select(a => a.GroupId)
```

## Next steps

1. Draw the ER diagram and adjust the model if the diagram changes it.
2. Generate and review the first migration:
   ```bash
   dotnet ef migrations add AddGroups --context GroupsDbContext --output-dir Groups/Data/Migrations --project backend/Team21.Acc.Infrastructure --startup-project backend/Team21.Acc.Infrastructure
   ```
3. Confirm with the team that "materials" are owned by LAB / ASN and that ACC-06 only stores their identifiers.
