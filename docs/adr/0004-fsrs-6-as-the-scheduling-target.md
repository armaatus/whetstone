# ADR-0004 — FSRS-6 as the scheduling target

**Status:** Accepted
**Date:** 2026-08-25
**Closes:** OQ-1 (#23) · unblocks #18 (ticket 1.6), #19 (1.7)
**Related:** MVP spec §4.3 (scheduling), §2.4 (non-goals)

> Cited as "ADR-004" in spec §14 ticket 0.10 and in #18. Same decision; the file uses the repo's
> four-digit convention.

---

## Context

Spec §4.3 set the default as FSRS-5 and required the current upstream state be verified before
implementing. That verification changes the answer.

### Upstream state, checked 2026-08-25

| Version | Status | Parameters |
| --- | --- | --- |
| FSRS-5 | Superseded | 19 |
| **FSRS-6** | **Current stable.** `fsrs-rs` v6.6.0 (2026-06-06); `py-fsrs` ships its defaults | **21** |
| FSRS-7 | Unfinished research. Not the upstream new-deck default | ~35 |

FSRS-6 differs from FSRS-5 in two ways:

1. **The decay exponent becomes a trainable parameter** (`w20`, clamped to `[0.1, 0.8]`), so the
   shape of the forgetting curve varies per learner. FSRS-5 hardcodes a single decay constant for
   everyone.
2. **Same-day review handling is reworked** — a formula improvement, not a training improvement.

Published default parameters exist for FSRS-6, and `py-fsrs`'s `tests/test_basic.py` carries usable
test vectors: exact interval sequences, plus stability and difficulty to four decimal places. Ticket
1.6's acceptance criterion ("all published test vectors match") is satisfiable.

### Why the spec leaned FSRS-5

> "FSRS-6 exists and adds a trainable decay term. MVP explicitly does not do parameter optimisation
> (§2.4), which weakens the case for a version whose advantage is a trainable term."

Sound reasoning from a wrong premise. The trainable term is not FSRS-6's only advantage, and being
untrained does not make it worthless.

## Decision

**Target FSRS-6 with published default parameters. FSRS-7 is explicitly out.**

Four reasons, weakest to strongest:

### 1. A fitted default beats a hardcoded constant, even untrained

FSRS-5's decay is a fixed constant chosen once. FSRS-6's `w20` ships with a default fitted against a
large review corpus. Shipping defaults — which is what §2.4 requires — therefore yields a
better-calibrated forgetting curve under FSRS-6 than under FSRS-5. The improvement arrives without
running an optimiser.

### 2. The same-day fix is unrelated to training, and Whetstone needs it most

This is the argument that actually settles it, and it is specific to this product rather than
inherited from Anki.

Whetstone **generates same-day reviews structurally**. Sessions are 5–15 exercises (§3.1) and expire
four hours after creation (INV-15); a rubric score below 40% maps to `Again` (§4.3), which sends a
card to `Learning`/`Relearning` inside the same sitting (INV-10, P-3). Repeated same-day exposure is
the normal path here, not the edge case it is in a deck reviewed once a day.

FSRS-6's same-day handling is a formula improvement that applies to every user regardless of whether
anything was ever optimised. Choosing FSRS-5 means deliberately adopting the weaker treatment of the
review pattern this product produces most.

### 3. §2.4 defers the optimiser; it does not cancel it

> "FSRS parameter optimisation | Ship default parameters; optimiser is post-MVP."

Post-MVP, not never. Under FSRS-5, adding the optimiser later means **migrating algorithm versions
against live scheduling state** — every card carrying stability and difficulty computed under a
different model, with no clean way to reconcile them and no way to tell a genuine change in a
learner from an artefact of the migration. Under FSRS-6, the optimiser is purely additive: the
parameters already exist and defaults are replaced by fitted values.

Choosing FSRS-5 buys a marginally simpler MVP and sells a migration on the one dataset that cannot
be regenerated.

### 4. Implementing a superseded version is a worse answer

§17's premise is that this code should be defensible under questioning. "I implemented FSRS-5 from
spec in 2026" invites the obvious follow-up. "I implemented FSRS-6, verified against upstream test
vectors, and deferred FSRS-7 because its formulas are not frozen" is the better answer, and it is
also simply true.

### Why not FSRS-7

Roughly 35 weights, a dual forgetting curve, and fractional interval support — and upstream has not
made it the new-deck default because the formulas and safe defaults are not frozen. Implementing an
unfrozen specification by hand means the test vectors move underneath the implementation. Revisit
only once upstream ships it as default; nothing in the domain design blocks a later move.

## Consequences

### Good

- Two more parameters, better-calibrated defaults, and a same-day formula that matches how this
  product actually schedules.
- The post-MVP optimiser becomes additive rather than a migration.
- Current, verifiable upstream target with real test vectors.

### Bad / costs

- **Marginally harder to implement by hand:** a decay that is a parameter rather than a constant,
  and a second same-day path. That cost is small, and §17 puts this work on the do-by-hand list
  precisely because the difficulty is the point.
- Two extra parameters to validate and clamp.

### Neutral — impact on the property tests (§4.3, P-1…P-7)

All seven survive unchanged. Nothing in FSRS-6 breaks the interval floor (P-1), grade ordering
(P-2), `Again` transitions (P-3), difficulty bounds (P-4), stability growth (P-5), retrievability
monotonicity in elapsed time (P-6), or determinism (P-7). A trainable decay changes the *shape* of
the curve, not its monotonicity.

One addition is worth making: `w20` is clamped upstream to `[0.1, 0.8]`, which is a parameter-bounds
invariant of the same kind as INV-11 (`Difficulty ∈ [1, 10]`). Ticket 1.3 should carry it as a value
object rather than leaving it as a bare double, and a property test should assert the clamp holds
across any parameter set — including a hand-edited one, which is how it will eventually be violated.

## Alternatives considered

**FSRS-5, per the spec's default.** Marginally simpler, one fewer thing to get right. Rejected on
reasons 2 and 3: it adopts the weaker same-day model for a product built around same-day reviews,
and converts the post-MVP optimiser into a migration against live scheduling state.

**FSRS-7.** Best available accuracy, eventually. Rejected — implementing unfrozen formulas from spec
means the target moves during implementation, which is the one condition under which
"implement from the specification" stops being a good idea.

**Port a library instead of implementing.** Rejected by §4.3 and §17: *"Implement it from the
specification, not from a ported library. This is the part you are here to learn."* Upstream
implementations remain legitimate as **test-vector sources** — verifying against their published
expected outputs is not porting.
