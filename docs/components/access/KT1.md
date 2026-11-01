# ACC-04 / ACC-08 - full KT1 checklist (exercise 4)

This checklist follows **all eight obligations supplied for section 3.1: basic implementation and security model**. Assigned requirements: ACC-04 (J) and ACC-08 (N). The two implemented primary operations are ReadAttempt and UpdateDraftAnswer in a repeatable synthetic adapter.

## Eight obligations and their actual evidence

| No. | Mandatory obligation | Concrete material | Actual state |
| --- | --- | --- | --- |
| 1 | Use cases, implementation order and prerequisites for every assigned component in Tapiz | [Backlog](BACKLOG.md), [machine-readable cards](backlog.json), copy helper | Cards prepared; real Tapiz creation and URLs remain external evidence |
| 2 | Common domain/flow, entities, statuses, ownership and Subject-Action-Resource-Allow/Deny matrix | [Common model and full matrix](DOMAIN_MODEL.md); separate [ACC-04](ACC-04.md) and [ACC-08](ACC-08.md) architecture/DFDs | Prepared and aligned with current Read + draft-update code |
| 3 | Repository structure, README, passing build, initial CI, task -> branch -> PR | Root README, solution/tests, existing CI; [PR #18](https://github.com/owlCoder/security-lab/pull/18); [baseline green run](https://github.com/owlCoder/security-lab/actions/runs/41132593252) | Baseline commit `f3d9d1c` green; prepared update requires a new CI after push; Tapiz task links pending |
| 4 | Primary rounded flow through logic and store/simulator; two key operations where appropriate | Protected ReadAttempt and UpdateDraftAnswer; actual stored answer/version; CAS and immutable historical states | Implemented; two primary operations remove the earlier read-only ambiguity |
| 5 | Server authorization, input validation and an automated negative test | AccessPolicy, read/draft services, NUnit policy and HTTP tests | Implemented and locally validated; see [actual results](VALIDATION.md) |
| 6 | Threat model for every assigned component: asset -> misuse -> control -> test -> evidence; trust boundary | [Threat model](THREAT_MODEL.md), component DFDs and common flow | Prepared with named tests and evidence references |
| 7 | Migrations/seed or equivalent repeatable state; structured security/change audit without secrets | DemoData fixed seed, atomic draft adapter, versioned metadata audit and audit test | Implemented synthetic equivalent; no SQL migration is required for this demonstration |
| 8 | At least one reviewed neighboring-team contract; positive/negative double if provider unavailable | [Proposed contract](CONTRACTS.md), provider ports, contract/HTTP tests | Contract and doubles ready; actual neighboring-team review remains external evidence |

## What is and is not closed

The source, two primary flows, models, matrices, threat traceability, repeatable seed and verification material are prepared. **A ZIP does not create a live Tapiz backlog or obtain another team's agreement.** Record those real references in [EVIDENCE.json](EVIDENCE.json), together with the new CI after pushing this update. Until then the complete KT1 is not declared accepted/closed.

No separate CONTRIBUTION.md is required or included. The real branch/commit/PR and the student's explanation provide the requested work trace. Do not fabricate task dates, reviewers, author contributions, new CI results or course approval.

## Run and present

1. Follow [DEMO.md](DEMO.md) to build, start the explicit AccessKt1 profile, run the HTTP demo and show both operations in React.
2. Show the common [domain model](DOMAIN_MODEL.md), separate component diagrams, the security matrix and one threat-to-test evidence chain.
3. Show an allowed read, an allowed own-draft update, a rejected foreign write, a stale-version conflict and expired-context denial.
4. Show the actual NUnit/TRX result and the metadata-only change audit example.
5. Show the real Tapiz task links, branch `feature/acc-04-authorization`, PR #18 and successful CI for the submitted revision.
6. Show the real neighboring-team review reference and explain remaining KT2 integration work.

Technical/document consistency check:

```powershell
python .\scripts\check-access-kt1.py
```

Optional evidence-completeness gate, after actual external references are recorded:

```powershell
python .\scripts\check-access-kt1.py --require-external
```

The default gate checks prepared material and reports external gaps without pretending they are complete. The strict option fails until the required links/acceptance record/new CI are present. It checks recorded completeness, not the truth of another person's approval.

## Remaining work through KT2

- Integrate agreed identity/account/membership/ATT providers, with current authorization and canonical IDs/statuses.
- Replace the synthetic answer adapter with the owning module's persistence/transaction implementation, preserving CAS semantics and immutable historical results.
- Extend agreed contextual attributes and policy ownership/version changes without removing mandatory object checks.
- Integrate protected durable audit and actual authentication/session lifecycle. No login/password administration, full policy language or generic ABAC laboratory is claimed here.
