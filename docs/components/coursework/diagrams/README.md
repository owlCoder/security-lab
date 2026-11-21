# KT1 diagrams

Each `.mmd` is editable Mermaid source. The matching `.svg` is rendered and opens in a browser without Mermaid tools.

| Diagram | Component / purpose |
|---|---|
| acc04-architecture | ACC-04 components, interfaces and dependencies |
| acc04-dfd | ACC-04 actors, processes, stores and trust boundaries |
| adm08-architecture | ADM-08 components, interfaces and dependencies |
| adm08-dfd | ADM-08 administrative data flow and trust boundaries |
| access-admin-domain | Unified entities, ownership and course scope |
| access-admin-flow | Shared decision and snapshot-bound read/write flow |

Provider ports are trusted server contracts. In KT1 their adapters run in the in-memory simulator; separate boxes identify ownership and trust boundaries, not deployed microservices. The administrative store contains no student answers or teaching memberships.

Malformed transport input (such as an invalid ID/body or a missing mutation header) can be rejected before application audit processing. The audit flows represent application outcomes and relevant changes; they do not claim to log every framework/parser rejection.
# KT2 diagrams

| Diagram | Current scope |
|---|---|
| adm08-kt2-architecture | Real Identity/catalog/Access/PostgreSQL integration, explicit first-root operator, supported central Audit relay and pending event mappings |
| adm08-kt2-dfd | Runtime client/provider trust boundaries, current-state queries, authorized operations, own transactional outbox and uniform rejection |
| acc04-kt2-architecture | Real Identity/session, central read/draft checks, shared membership bridge, HTTPS ATT adapter and explicitly unavailable neighboring providers |
| acc04-kt2-dfd | Untrusted client inputs, real session checks, current resource/membership contract flows, bound operations, diagnostics and public responses |

The editable Mermaid and static SVG versions describe the same scoped relationships.
DFDs show runtime data paths; the ADM architecture also shows the separate operator
bootstrap and relay. Yellow dashed ATT/membership boxes are **not implemented provider
services on this main revision**. Their positive tests use explicit contract doubles.
No durable ATT or whole-platform integration is inferred from those pictures.

The older diagrams above retain their KT1 simulator scope. Runtime command/startup,
remaining audit catalog mappings and cross-provider transaction limits are in `KT2.md`.


Increment 4 executes the ADM architecture over an actual HTTPS/Kestrel process and
real PostgreSQL providers, including restart and exact-cookie revocation. This is
recorded in `KT2-VALIDATION.md`; the diagrams still keep ATT/ACC-02 providers marked
as unavailable on main. The separate demo orchestrator is a client/operator test tool,
not a new platform trust bypass or public seed endpoint.
