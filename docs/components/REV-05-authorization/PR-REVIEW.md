# Complete REV-05 authorization and transactional audit

At 6e310e9 the registered deny-only permissions and missing helper policies made
successful revision review impossible. This change registers every API action,
connects server-managed persistent course/action grants and validates revision
callers using the existing Identity session contract.
An additive read-only Identity account-state contract also rejects blocked or
archived accounts even when an earlier session has not yet been revoked.

Approval now follows the persisted course policy: the author is denied when
independent approval is required and allowed otherwise only with approve/read
permissions. Missing policy and missing, revoked, expired or wrong-course grants
remain denied. account.unlock follows the existing resource-less System contract.

The Audit module gains a public PostgreSQL composition entry point and a
transaction-aware writer. Submission, return and publication records are inserted
in the same database transaction as revision state, decisions and idempotency
receipts. Audit failure returns 503 and rolls back the mutation. Retries do not
produce duplicate publication or audit records. Denied HTTP reads also produce
persisted access.denied events through the Access adapter.

Validation: Release solution build passed without warnings or errors. 1336 distinct
backend tests passed across the main isolated PostgreSQL run and the separately
configured nine Authoring persistence tests; no failing tests. All Audit runtime
permission tests ran. The ten new real HTTP/PostgreSQL cases cover successful
workflow, both policy settings, durable audit, revoked/expired/wrong-course grants,
retry behaviour, audit-storage failure rollback and repeatable migration. Frontend
build and all 60 tests passed. Detailed evidence is in TEST-EVIDENCE.md.

Deployment: apply the new revision_security migration and the existing Audit
migration to the Laboratories database. Provision explicit minimum grants using
administration credentials; runtime can only read grants. Audit runtime privileges
remain SELECT/INSERT, with no update/delete/DDL. No production seed users or
public grant-management endpoint are introduced. See README.md for exact setup.

The targeted shared changes are Audit contracts/composition, a read-only Identity
account-state adapter and one transactional audit call in ReviewService. Access,
CI workflows and existing Laboratories migrations are not rewritten. This PR does not implement other owners' lockout or
password-reset stories.
