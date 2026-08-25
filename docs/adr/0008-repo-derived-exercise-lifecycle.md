# ADR-0008 — Repo-derived exercise lifecycle and drift

**Status:** Accepted
**Date:** 2026-08-25
**Related:** ADR-0006 (exercise sources), MVP spec §4.2 INV-6

---

## Context

Hand-authored exercises are stable: the author decides when they change. Corpus-derived exercises
are not. The file gets refactored, the function is deleted, the PR is reverted, the ADR is
superseded — and the exercise now tests code that no longer exists.

This is a new failure mode with two faces:

- **User-facing:** being asked to reason about a function that was deleted last month is worse than
  useless. It actively teaches a false model of the system and destroys trust in the tool.
- **Domain:** spec §4.2 INV-6 says a published exercise's prompt and rubric are immutable, and edits
  create a version with existing cards pinned to the version they were scheduled against. That rule
  is still right, but it has nothing to say about an exercise whose *source* moved. Versioning
  answers "the author changed their mind." Drift is "the world changed underneath."

Left unhandled, the obvious hack is to mutate the exercise to match the new code — which breaks
INV-6, invalidates every review recorded against it, and silently rewrites history.

## Decision

### 1. Drift retires; it never edits

An exercise whose provenance no longer resolves moves to `Retired`. Its prompt and rubric stay
exactly as they were. `Retired` exercises are never scheduled into a new session and never deleted.

Exercise states become: `Draft → Published → Retired`, and **`Draft → Retired` is legal**. A
candidate-derived exercise can drift while it is still sitting in the review queue — the corpus does
not wait for a human to get round to it — and forcing such an exercise to be published first, purely
to retire it, would put a knowingly-stale item in front of a learner. There is no path back from
`Retired`; a still-valuable finding at the new location is a **new** exercise with new provenance.

**A live session is not interrupted.** Retirement takes effect for session *building* (§1). A card
already presented in an in-flight session plays out normally, and its review is graded and counted:
the learner reasoned about the code as it was at the moment they were asked, and that reasoning was
real. Yanking a question mid-session to preserve tidiness would spend the user's trust to buy the
system nothing. The retirement notice appears afterwards, per §4.

### 2. Drift is detected, not guessed

A scheduled `DetectDrift` job resolves each published exercise's provenance against the current
corpus head and compares the recorded `ContentHash`. Detection is a comparison of recorded facts,
not a model call. Signals:

| Signal | Meaning |
| --- | --- |
| `SourceRef` unresolvable | commit gone, PR deleted, ADR path removed |
| `ContentHash` mismatch at same location | the code changed under the exercise |
| Location no longer exists | file or range removed |
| **Resolution failed** | **inconclusive — not drift. Retry; do not retire** |

A hash mismatch retires the exercise and enqueues re-generation for that location. Re-generation
produces a candidate, which goes through the human review queue like any other (ADR-0007 §5).

**Absence of evidence is not drift.** Because retirement is one-way (§1), detection must
distinguish *"the ref is genuinely gone"* from *"I could not find out."* A provider outage, a
revoked token, a rate-limit response, a network partition, or a shallow clone missing history are
all **inconclusive**, and an inconclusive resolution leaves the exercise `Published` and retries.

Without this rule the failure mode is severe and silent: one expired credential retires an entire
tenant's exercise set overnight, irreversibly, and the user sees a product that quietly emptied
itself. A drift run that cannot reach the corpus must fail loudly and change nothing.

This constrains **OQ-9** (corpus ingestion mode). A local index can distinguish the two cases
cheaply — the history is on disk. Read-only API access per query must be able to separate a 404
for a deleted ref from a 5xx, a 403, and a timeout, and must treat anything it cannot classify as
inconclusive. That difficulty is a genuine cost of the API-per-query option and belongs in OQ-9's
decision.

### 3. Review history survives retirement

**This is the invariant that makes the whole thing safe.** A Card's reviews remain valid after its
Exercise retires, and continue to contribute to mastery.

The justification is already in the model: mastery accrues to the **Skill**, not to the Exercise
(spec §4.4 sums retrievability over cards targeting a skill). An exercise is an instrument for
measuring a skill; retiring the instrument does not un-measure the reading.

Consequences for the FSRS layer: a retired exercise's card stops being scheduled but keeps its
stability and difficulty. If a re-generated exercise covers the same Skill, the user gets a new Card
(INV-8 — one card per user per exercise, ever) and the Skill's mastery is continuous across the
transition even though the Card is not.

### 4. Retirement is visible, not silent

A user whose card was retired mid-cycle sees why: "this exercise came from `PaymentReconciler.cs`,
which changed on 12 March." Provenance is shown, not hidden. That message is a feature — it tells
the user their system moved, which is itself worth knowing.

### 5. Drift rate is a monitored metric

Per tenant and per source kind. A source whose exercises retire faster than users can practise them
is generating from too volatile a part of the corpus, and the session builder should weight it down.
Expect `PullRequestSource` to drift fastest and `AdrSource` slowest.

## Consequences

### Good

- INV-6 survives untouched; drift gets its own concept rather than corrupting versioning.
- Mastery is continuous across corpus churn, which is the only way a decay chart over a live
  codebase means anything.
- Drift rate per source is a genuinely useful signal that falls out for free.

### Bad / costs

- A `DetectDrift` job, a `Retired` state, provenance resolution per source kind, and a re-generation
  path — all new work in Epics 1, 2, and 2.5.
- Corpus history must be retained long enough to resolve provenance. A force-pushed or
  history-rewritten repository will retire exercises en masse; this is correct but will look alarming
  the first time it happens.
- Storage grows monotonically — retired exercises are kept forever. Bounded by GDPR retention
  (spec §7.9), which purges `BeforeState` content on the same schedule as `answer_text`, leaving the
  provenance record.

### Neutral

- Users on a fast-moving codebase will see more retirements than users on a stable one. That is a
  true signal about their environment, not a defect.

## Alternatives considered

**Mutate the exercise to track the code.** Breaks INV-6, invalidates recorded reviews, rewrites
history. Rejected.

**Version the exercise on drift instead of retiring.** Conflates two different events under one
mechanism. A version says "the author revised this"; drift says "the subject is gone." Making them
the same makes both meaningless. Rejected.

**Delete drifted exercises.** Destroys the reviews' referential integrity and the audit trail.
Rejected.

**Ignore drift; let users flag stale exercises.** Cheapest to build, and it spends user trust to
save developer time — in a product whose entire premise is trustworthy feedback about a system.
Rejected.
