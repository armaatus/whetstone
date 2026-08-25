# ADR-0010 — `WriteFromScratch` survives MVP without code execution

**Status:** Accepted
**Date:** 2026-08-25
**Closes:** OQ-4 (#74)
**Related:** ADR-0006 (exercise sources), MVP spec §2.4 (non-goals), §3.2 (exercise types), §9.4 (evals)

---

## Context

OQ-4 asked whether `WriteFromScratch` survives MVP without code execution, or is too weak to grade
fairly.

The question was well-posed under the original design. Sandboxed execution is a hard non-goal
(§2.4 — "highest-risk subsystem"), so a `WriteFromScratch` answer could only be rubric-graded. And
under hand-authoring, the rubric was one person's opinion, written months earlier, against which a
model scored free-form code. That is the weakest grading path in the product, applied to the type
with the widest space of correct answers. Dropping the type was a live option.

**ADR-0006 dissolved the premise rather than answering the question.** A corpus-derived
`WriteFromScratch` is graded against **the diff that actually shipped**, with the candidate's
`Rationale` supplying the criteria (ADR-0006 §2). ADR-0006's own Good column states it: *"Grading
gains ground truth: a real diff, a real ADR, a named Lens rule."*

Execution was never the only source of ground truth. It was the only one available when exercises
were authored by hand.

## Decision

**`WriteFromScratch` stays in MVP, graded without execution, against corpus-derived references.**

Three parts, and the second matters as much as the first:

1. **The type survives**, and is retained deliberately rather than by default. It is the type
   closest to the product thesis — the blank-page skill is the one agentic tooling most efficiently
   substitutes for, and therefore the one that decays first.

2. **§2.4's non-goal on sandboxed code execution is unchanged and unchallenged.** Nothing in this
   decision argues for execution, now or later. Resolving OQ-4 must not be read as softening the
   non-goal — the reasoning went the other way, by removing the need.

3. **The residual concern becomes a standing metric, not an open question.** What remains is
   measurable rather than debatable: does rubric agreement on `WriteFromScratch` clear §9.4's ≥85%
   gate when the reference is a real diff, and is it materially worse than for other types? The eval
   harness (#68) reports rubric agreement **broken out by exercise type**, so the concern stays
   visible after this closes and fails as a measured regression if it fails at all.

A question that has become a number does not need to stay open. It needs the number to be reported.

## Consequences

### Good

- The type most aligned with the product thesis survives, with better ground truth than the original
  design could give it.
- The concern does not disappear — it moves somewhere it is checked on every eval run rather than
  remembered.
- One fewer open question gating Epic 4.

### Bad / costs

- #68 must break agreement down by exercise type rather than reporting one aggregate. Small work,
  and useful independently: an aggregate that hides one bad type is a metric doing the opposite of
  its job.
- `WriteFromScratch` now depends on source quality in a way the other types depend on it less. A
  poor diff makes a poor reference, and there is no execution backstop to catch it. The candidate
  review queue (ADR-0007 §5) is where that gets caught, which is another argument for not skipping
  it.

### Neutral

- Hand-authored `WriteFromScratch` exercises via `ManualSource` still have only an author's rubric.
  They are the exception rather than the entry point, and inherit the original weakness — correctly,
  since nothing has changed about them.

## Alternatives considered

**Drop the type from MVP.** Cheapest, and it removes the type that best expresses what the product
is for. Rejected — the reason to drop it was a grading weakness that no longer exists.

**Keep it, but self-assessed only.** Sidesteps grading quality entirely and produces no measurement,
in a product whose premise is measuring decay. Rejected.

**Add sandboxed execution after all.** Would give the strongest ground truth. Rejected: §2.4's
reasoning is untouched — it is the highest-risk subsystem in the design, and this ADR exists
precisely because it turned out not to be needed.

**Leave OQ-4 open until eval data exists.** Superficially cautious, actually worse: it keeps a
decision-shaped item in the tracker for something that is now a threshold on a chart, and open
questions that cannot be closed by deciding anything are how a decision log rots.
