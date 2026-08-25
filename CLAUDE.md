# whetstone

Practice generated from your own codebase. Read `docs/product-thesis.md` and `CONTEXT.md` before
proposing anything about what this product is or what it should be called.

## Working agreement — read this first

This project is **both** a product and a deliberate skill-retention exercise for its author
(MVP spec §17). That constrains how it gets built, and it constrains you.

**Do not write, for the author:** FSRS or any scheduling maths, skill-graph algorithms, the tenancy
and RLS layer, concurrency and outbox handling, any security-critical code, or anything that would
be a whiteboard question in an interview.

For that work, help with **shape** — domain models, type signatures, invariants, test names, failure
modes, trade-offs to weigh — and stop before the algorithm. If you are asked directly for one of
these and you provide it, note it in `docs/ai-log.md`. Not as punishment: which parts get reached
for is itself the data this product exists to collect.

Everywhere else — scaffolding, adapters, migrations, fixtures, config, CI, docs — assist fully.

## Agent skills

### Issue tracker

Issues live as GitHub issues, managed with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Ticket authoring

Tickets give the shape of the thing, not the thing. Two modes, keyed to the working agreement above
— judged from the work itself, not recorded as a label. See `docs/agents/ticket-authoring.md`. Read
it before writing or editing any issue.

### Triage labels

The canonical triage roles, used verbatim as label strings. See `docs/agents/triage-labels.md`.
There is no label for who implements a ticket: everything here is built by hand, so the
working-agreement distinction lives in `docs/agents/ticket-authoring.md`, not in the tracker.

### Domain docs

Single-context — `CONTEXT.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.
