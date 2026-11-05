# Audit security matrix (AUD-01, AUD-06)

**Owner:** Vanja Jovanov (Team 12, member 7) · **Stories:** S-04/S-18
**Baseline:** `84166fb258681a5bc47aa510a3719226b7891ddd` (2026-11-04)

This matrix connects requirements and abuse cases to controls and existing evidence. A listed test is a source-level test name, not a claim that it was run in this review. PostgreSQL-backed and database-role cases need their documented connection strings; unit tests do not establish deployed configuration. See the [threat assessment](THREAT_MODEL.md) for context and limitations.

| Requirement / abuse case | Required security outcome | Existing control | Existing evidence | Status / gap |
|---|---|---|---|---|
| AUD-01: fake actor/time/correlation | Values come from trusted server context; UTC timestamp | Draft omits those fields; writer uses context provider and `TimeProvider` | `AuditWriterTests.WriteAsync_WhenDraftIsValid_WritesTrustedImmutableEventWithServerUtcTime` | Writer not registered by `AddAudit()`; no live producer proof |
| AUD-01: anonymous failed login | No invented user identity; no typed login value stored | `Anonymous` requires null ID; catalog constrains login actor/outcome | `AuditDraftValidatorTests.Validate_WhenActorKindIsNotAllowedForOutcome_RejectsContext`; `AuditEventTests.Create_WhenActorIsAnonymous_StoresNoActorId` | Identity integration/policy open |
| AUD-01/AUD-03: secret or arbitrary detail | Reject unknown/free-text fields and bounded payloads without echo | Event-specific allowlist, formats, secret-key block, size limits | `AuditWriterTests.WriteAsync_WhenDetailIsNotAllowed_RejectsWithoutWritingOrLeaking`; `Validate_WhenMoreThan20DetailKeys_RejectsTooLarge`; `Validate_WhenDetailsExceed2048Bytes_RejectsTooLarge` | Upstream logging remains separate; arbitrary free text is prohibited |
| AUD-01: invalid event/outcome/course/resource | No unrecognized or malformed event persisted | Catalog/schema validation and domain invariants | `AuditDraftValidatorTests.Validate_WhenEventTypeIsNotInCatalog_RejectsUnknownEventType`; outcome/course/resource test family | Producer's course provenance still needs trusted resolver/integration |
| AUD-01/ADM-07: duplicate or altered retry | Same ID/content idempotent; conflicting content cannot overwrite | Stable event ID, append-only insert, conflict comparison | `AuditWriterTests.WriteAsync_WhenEventIdRetryHasSameContent_IsIdempotent`; PostgreSQL retry tests | DB suite is configuration-dependent |
| AUD-01/OPS-04: storage failure | No success claim; no internal details leaked | Writer maps store exception to `Unavailable` | `AuditWriterTests.WriteAsync_WhenStoreFails_ReturnsUnavailableWithoutLeakingFailure` | Composition is not wired; producer transaction/outbox behavior open |
| AUD-01: mutation succeeds without durable evidence | Business change and evidence atomic or durable outbox | Store participates in caller transaction | `PostgresAuditEventStoreTests.Rollback_RemovesUncommittedAuditInsert` | Does not test a real rights mutation; no integrated producer proof |
| AUD-01/SEC-02: runtime modifies history or schema | Runtime cannot UPDATE/DELETE/TRUNCATE/DDL audit data | Dedicated runtime role grants SELECT/INSERT only in Audit schema | `AuditRuntimePermissionsTests.Runtime_RejectsForbiddenSqlWithoutChangingEvent` | Requires actual runtime-role test configuration; privileged DBA remains trusted |
| AUD-06/SEC-01: teacher changes course ID (IDOR) | No events/count/page/filter data for other course | Endpoint/query unavailable, so no current exposure | None | Must implement server authorization and SQL course predicate before read/count/page |
| AUD-06: student, blocked account, invalid delegation or provider outage | Deny by default; current membership checked per request | Endpoint/query unavailable | None | Must add positive and negative direct API/query tests against agreed provider contracts |
| AUD-06: admin/global role leaks cross-course events | No implicit global teacher access | No endpoint exposed | None | Define accepted policy; role alone is insufficient |
| AUD-06/AUD-03: fields, counts, paging, XSS/filter injection leak data | Minimal DTO; safe text; bounded filters; parameterized queries; deterministic paging | No query/API/UI exists | None | All listed behaviors remain unimplemented and unverified |
| OPS-05: DI lifetime crosses requests | Scoped state cannot be captured by singleton | Host/provider scope validation and negative test | `CompositionScopeTests`; `HostStartupScopeTests` | Recheck when `AddAudit()` and actual adapters register services |

## Coverage interpretation

- **Implemented** means a code path/control exists in the inspected source. It does not mean its test ran in this task or that it is deployed.
- **Unavailable** means no route/query is registered at the baseline. This limits exposure but does not satisfy AUD-06.
- **Open** means a producer, other component owner, operational setup, or integration test is still needed.

The DFD is maintained as editable Mermaid at [diagrams/audit-dfd.mmd](diagrams/audit-dfd.mmd). The scope and residual risks are maintained in [THREAT_MODEL.md](THREAT_MODEL.md).
