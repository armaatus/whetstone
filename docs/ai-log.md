# AI log

Per the working agreement (spec 17). Record here any time AI was used for something
on the "not used" list, and why. This is data about where skills have decayed, not a
confession log.

| Date | What | Why |
| --- | --- | --- |
| 2026-08-24 | Epic 0 scaffolding (project skeleton, build props, Aspire wiring, arch test) | Boilerplate — explicitly on the "AI is used for" list. `deploy/db-init/01-roles.sql` deliberately left empty. |
| 2026-08-25 | Product strategy rework: `docs/product-thesis.md`, `CONTEXT.md`, ADR-0006/0007/0008, `docs/agents/ticket-authoring.md`, and the tracker realignment | On the "AI is used for" list — specification, architecture discussion, ADR review. No domain code written. The ticket-authoring convention exists so this stays true as the tracker grows. |
| 2026-08-25 | ADR-0002 (render modes) drafted by AI. ADR-0001, ADR-0003 and ADR-0011 written up by AI **from decisions taken by the author** | ADR-0002 is transcription of spec §5.2 plus its rationale — on the "AI is used for" list. The other three were first scaffolded as open questions only; the author then asked directly for options with recommendations on each, chose all twelve, and the AI wrote the documents from those choices. **The recommendations were AI-supplied, which is the part that touches the §17 list — stack judgment and the tenancy/RLS layer.** Recorded per the exception mechanism. No domain code written; the interceptor, the policies and the migrations remain unwritten and are the author's. |
| 2026-08-25 | `docs/spikes/rls-and-derived-objects.md` — RLS coverage of derived objects, measured against PostgreSQL 17.10 and EF Core 10 | Research and documentation, on the "AI is used for" list. The finding (a materialised view serves one arbitrary tenant's rows to everyone) changed ADR-0003 §4 from a judgment call into a measured one. |
