# AI log

Per the working agreement (spec 17). Record here any time AI was used for something
on the "not used" list, and why. This is data about where skills have decayed, not a
confession log.

| Date | What | Why |
| --- | --- | --- |
| 2026-08-24 | Epic 0 scaffolding (project skeleton, build props, Aspire wiring, arch test) | Boilerplate — explicitly on the "AI is used for" list. `deploy/db-init/01-roles.sql` deliberately left empty. |
| 2026-08-25 | Product strategy rework: `docs/product-thesis.md`, `CONTEXT.md`, ADR-0006/0007/0008, `docs/agents/ticket-authoring.md`, and the tracker realignment | On the "AI is used for" list — specification, architecture discussion, ADR review. No domain code written. The ticket-authoring convention exists so this stays true as the tracker grows. |
| 2026-08-25 | ADR-0002 (render modes) drafted by AI; ADR-0001 and ADR-0003 scaffolded only — context and open decision points, no decisions taken | ADR-0002 is transcription of spec §5.2 plus its rationale, on the "AI is used for" list. ADR-0001 (stack judgment) and ADR-0003 (tenancy/RLS) are §17 not-AI items, so the drafts stop at the questions: `## Decision` in both is a list of open `D-n` points with options and trade-offs, and both carry `Status: Draft`. |
