# Proposed inter-team access/attempt contract

Version: **access-attempt-v1**. State: **PROPOSED - NOT REVIEWED BY A NEIGHBORING TEAM**.

This document and its tests are ready for a real review. They do not substitute for one. The current PR is [#18](https://github.com/owlCoder/security-lab/pull/18); the owner team/person of the real ATT provider must be identified from the confirmed project roster, not old SCUTUM team numbers.

## Minimum review requested for KT1

Review the ATT read boundary and the synthetic draft-CAS extension below. At least one actual relevant neighboring-team agreement is required. Record the reviewer, contract version and a real confirmation/comment URL in [EVIDENCE.json](EVIDENCE.json). Other provider integrations remain explicit follow-up work.

## Ownership and data minimization

- ATT owns attempt identity, OwnerId, CourseId, lifecycle state, content and Version. Access receives snapshots and never changes another module's internal tables.
- Course/membership providers own current course-scoped roles and delegation; the account provider owns current activity. Client-supplied role/owner/status fields are never authoritative.
- ACC-08 owns the decision about contextual restriction through a policy provider; server UTC is authoritative.
- Only the local `DemoData` adapter implements this proposal at KT1. No ATT team's service, database, route or grading logic is replaced.

## Consumer-side ports in code

```csharp
public interface IAttemptReader
{
    PortResult<AttemptSnapshot> FindAttempt(Guid id);
    PortResult<AttemptView> ReadAnswer(AttemptSnapshot authorizedSnapshot);
}
public interface IAttemptDraftWriter
{
    DraftWriteResult TryUpdateDraft(AttemptSnapshot authorizedSnapshot,
        int expectedVersion, string answer);
}
```

These are read/draft ports proposed by this consumer. They do not claim to replace the specification's broader `IAttemptStore` contract or its submission/evaluation responsibilities.

| DTO | Fields and invariants |
| --- | --- |
| AttemptSnapshot | Id, OwnerId, CourseId are nonempty GUIDs; State is a known enum; Version >= 1; ContextPolicyId identifies the required context |
| AttemptView | Id, answer and Version from exactly the authorized snapshot; content is released only after allow |
| UpdateDraftRequest | Only proposed answer and expectedVersion; nonblank content <= 4000 characters and version >= 1 |
| ContextPolicy | Matching ID, nonempty version, NotBefore < NotAfter when both are set; absent bounds are an explicit unrestricted window, not absent policy |
| AccessAuditEvent | UTC, correlation, actor, action, target, course, outcome, internal reason, policy version; draft update also includes before/after resource versions |

## Result and error semantics

| Provider reply | Meaning | Consumer behavior |
| --- | --- | --- |
| PortStatus.Ok + matching snapshot | Metadata/content exists | Continue validation and authorization |
| PortStatus.NotFound | Missing resource/membership/policy | Generic public denial; precise internal reason |
| PortStatus.Unavailable | Known dependency failure normalized by the adapter | Fail closed; no answer/write; same public denial |
| DraftWriteStatus.Updated | Exact authorized snapshot and expected version matched atomically | Return new answer/version; record metadata-only update event |
| DraftWriteStatus.Conflict | Version/owner/course/status/policy binding no longer matches, or attempt is no longer a draft | No mutation; authorized caller gets 409 and must reload/re-authorize |
| DraftWriteStatus.NotFound / Unavailable | Resource gone or writer unavailable | Generic public denial; no mutation |

Known transport/database failures must not escape as inconsistent public existence signals. TimeoutException is normalized by the current services. The production adapter and provider owner must agree any additional error mapping before integration.

## Synthetic positive and negative examples

Read positive: Ana / matching active membership / attempt `30000000-0000-0000-0000-000000000001` / open policy -> ReadAttempt allowed, synthetic answer returned.

Read negative: Marko / same course / Ana's attempt -> public 404 `RESOURCE_UNAVAILABLE`; internal `NOT_OWNER_OR_COURSE_REVIEWER`. Missing attempt produces the same public reply with internal `RESOURCE_NOT_FOUND`.

Draft positive: Ana / own InProgress attempt `30000000-0000-0000-0000-000000000004` / version 1 / open context -> answer updated, Version 2, owner/course/state retained.

Draft negative: another student or reviewer -> generic 404 without mutation. Stale version from an otherwise authorized owner -> 409 without mutation. Submitted/Completed -> denial without rewriting history. Membership/context outage -> generic denial, with distinct internal audit reason.

## Contract tests and scope

`AttemptPortContractTests` checks explicit missing response, snapshot binding, exact-one-writer CAS, immutable submitted data and repeatable seed. `AccessDraftHttpTests` exercises real HTTP authorization, failures, stale version, ignored authority fields and metadata-only audit. Existing read tests remain.

These tests verify the **local double against the proposed contract**. They do not prove a real ATT provider conforms. After the external review, adapt to the agreed canonical IDs/status vocabulary and run the same behavior checks against the actual adapter.

## Review record

| Item | Current fact |
| --- | --- |
| Contract revision | access-attempt-v1 |
| Neighboring provider owner | Not yet recorded |
| Review/confirmation URL | Not yet recorded |
| Accepted DTO/status/CAS semantics | Awaiting real reviewer response |
| KT1 replacement component | DemoData + explicit negative test doubles |

No confirmation is invented or sent by this package. Use `scripts/record-access-evidence.ps1` only after the actual review exists.
