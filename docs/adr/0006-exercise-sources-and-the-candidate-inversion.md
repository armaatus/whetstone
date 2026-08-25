# ADR-0006 — Exercise sources and the candidate inversion

**Status:** Accepted
**Date:** 2026-08-25
**Supersedes:** MVP spec §2.1–2.3 (product thesis), §2.4 row "Exercise marketplace", §3.2 (exercise
types are now source-derived), §14 Epic 5 tickets 5.1–5.4 (scope reduced)
**Related:** ADR-0007 (Lens trust model), ADR-0008 (exercise lifecycle and drift)

---

## Context

The MVP required users to hand-author every exercise. Three problems followed from that, and they
are the same problem seen from three angles:

1. **Cold start.** An empty database on day one, and "author your own exercises" is the Anki failure
   mode — the mechanism everyone admires and nobody feeds. The plan also contradicted itself:
   Milestone C promised dogfooding "with real content" in week 20; the authoring UI was week 25.
2. **No moat.** A generic exercise bank is a commodity. Any model produces infinite "spot the bug in
   this snippet" for free, so content a model can regenerate is worth zero.
3. **The hardest possible grading problem.** Free text scored against a hand-written rubric is the
   worst case for an LLM grader, which is why §9's entire eval apparatus exists. There is no ground
   truth to appeal to — only the author's opinion, months stale.

Meanwhile the thing that actually decays under agentic coding is not recall of an API. It is
understanding of the system you did not write but are responsible for (see
`docs/product-thesis.md`).

Both observations point the same way: **the exercises should come from the corpus.**

## Decision

### 1. Exercises are derived from a corpus, not authored

An `ExerciseSource` port inspects the tenant's corpus and yields `ExerciseCandidate`s. Manual
authoring becomes one source among several, not the entry point.

```
Whetstone.Application
  IExerciseSource            → yields ExerciseCandidate

Whetstone.Infrastructure
  GitHistorySource           code merged under your name that you did not write
  PullRequestSource          diffs currently awaiting your review
  AdrSource                  decisions with recorded rationale — free ground truth
  LensSource                 externally-authored rulebooks and perspectives (ADR-0007)
  ManualSource               a human writes the candidate directly
```

The port lives in `Application`; every adapter lives in `Infrastructure`. `Domain` knows about
`ExerciseCandidate` and about withholding; it knows nothing about Git, GitHub, or Lenses.

### 2. Candidates are not exercises. Withholding is the operation between them.

A source's natural output is *the answer*. A Lens such as `improve-codebase-architecture` reports
what to change and why. Piping a finding straight into an exercise would ship a tool that shows the
user the solution — a **PI-1 violation at the content layer**, where none of the §7.6 defences are
looking.

An `ExerciseCandidate` therefore holds both halves explicitly, and one half is marked withheld:

| Field | Role |
| --- | --- |
| `Location` | file + range, pinned to a commit sha |
| `BeforeState` | the artefact as it stands — **question side** |
| `Finding` | what the source says is notable — **withheld** |
| `Rationale` | why — **withheld** |
| `Provenance` | source kind, ref, content hash, generated-at |
| `Confidence` | the source's own confidence, 0..1 |

**Withholding** derives an `Exercise`: the prompt is built from `BeforeState` plus a question stem;
`Finding` and `Rationale` become the reference answer and the rubric criteria.

### 3. The withheld half is never present in a grading prompt

This is the load-bearing consequence and the reason the operation is named after it.

- Generation and grading run in **separate processes with separate contexts**.
- The grading prompt is assembled from `(prompt, rubric criteria)` only. There is no code path that
  can place `Finding` or `Rationale` into an `IChatClient` call for a graded turn.
- This is enforced by a type boundary, not by care: the grading DTO does not have fields for them.

PI-1 stops being prompt discipline and becomes structural. The model cannot leak what was never in
its context. The leak detector (ticket 4.5) is demoted from primary defence to backstop, and its
similarity threshold (OQ-5) stops being safety-critical.

**Withholding extends to provenance.** A citation can be the answer. "Explain why
`OrderProjection` caches by tenant and not by user", presented next to a link to the ADR that
states exactly why, is a lookup rather than an exercise. So `SourceKind` is shown before the
learner answers and `SourceRef` only after. Attribution is a reward for having reasoned, not a
hint offered alongside the question.

**Provenance also carries `Confidence` and `LensTier`**, copied onto the Exercise at withholding
time rather than joined back to the candidate. Two reasons: the session builder needs them to
weight judgment candidates up (see "Consequences → Bad" below), and candidate content is purged on
the retention schedule while the Exercise outlives it — so the join is not a reliable read.

### 4. Skills gain a kind, and are derived

`SkillKind.Module` maps to a real part of the corpus; `SkillKind.Concept` maps to a Lens rule or
rule group. The DAG is generated from corpus structure and registered Lenses rather than
hand-built, and mastery over Modules is what the team view reports as **comprehension** — the bus
factor report, which is a thing a lead can act on.

**Derived skills are identified by `SkillKey`, not by name.** Spec §4.2 INV-3 makes skill names
unique within a tenant, case-insensitively. That rule was written for names a human typed, and it
breaks on derived names in three ways: `src/Billing` and `src/billing` collide although a
case-sensitive filesystem considers them distinct; two Lenses may legitimately emit a `Concept` with
the same human-readable name; and a corpus rename leaves a skill with no stable identity to survive
on while INV-2 forbids deleting it.

So identity and label are separated:

| | `SkillKey` (identity) | `Name` (label) |
| --- | --- | --- |
| `Module` | canonical corpus path, case-sensitive as the corpus gives it | display text, may collide |
| `Concept` | `lens-id/rule-id` — namespaced, so two Lenses cannot collide | display text, may collide |
| Authored | a slug assigned at creation | display text |

INV-3 is restated to apply to `SkillKey` within `(TenantId, Kind)`. Name uniqueness becomes advisory
— a warning at authoring time, not an invariant.

**A Module whose path disappears goes `Dormant`, not deleted.** INV-2 forbids deletion while
exercises target a skill, and deleting would destroy the mastery history that is the entire point.
Dormant skills keep their history, stop accruing new cards, and reactivate if the path returns.
This is deliberately the same shape as exercise retirement (ADR-0008): the corpus moving is a fact
to record, never a reason to erase a measurement. A rename is a re-key with history preserved — the
competency did not change because a directory did.

## Consequences

### Good

- Cold start disappears. Point it at a repository and there is content.
- The moat becomes the tenant's corpus plus a decay model over it — not regenerable by anyone.
- Grading gains ground truth: a real diff, a real ADR, a named Lens rule.
- PI-1 becomes structural rather than behavioural, which is a materially stronger security story.
- The §2.4 marketplace non-goal is resolved rather than refused: the generator is shared, the
  generated content never leaves the tenant.
- Epic 5 authoring shrinks to a candidate review queue.

### Bad / costs

- New scope in Epics 2 and 4, paid for out of Epic 5 (see `docs/product-thesis.md` §7).
- Corpus ingestion is a new trust boundary — the system now reads private source code. Every §7
  control applies to the corpus, and `BeforeState` is as sensitive as `reviews.answer_text` for
  retention purposes.
- Exercises now rot. Handled in ADR-0008.
- Signal quality is inverted against generation ease: mechanically-derivable findings (a lint rule)
  make trivial exercises, while the judgment findings worth practising are the noisy ones. The
  session builder must weight toward judgment candidates despite their lower confidence. Do not
  design this tension away — measure it.

### Neutral

- Exercise *types* (§3.2) survive unchanged as presentation formats; what changes is where their
  content comes from. `MultipleChoice` remains the type that lets the loop be built before any AI.
- OQ-4 (does `WriteFromScratch` survive without execution?) is likely moot — a repo-derived
  `WriteFromScratch` has a real diff as reference.

## Alternatives considered

**Keep hand-authoring; seed a starter bank.** Solves cold start for one tenant and nothing else. No
moat, no relevance, no ground truth. Rejected.

**Generate exercises with an LLM from generic prompts.** Zero marginal cost and zero marginal value
— the user could ask the model directly. The scheduler over *forgotten* material is the only part
worth anything, and it needs a stable corpus to schedule against. Rejected.

**Pipe Lens findings straight to exercises.** Simplest possible pipeline, and it hands the user the
answer. Rejected — this is the failure mode the inversion exists to prevent.

**Put withholding in the Application layer.** Tempting, since generation is an application concern.
Rejected: withholding is where PI-1 is enforced, and PI-1 is a domain invariant. It belongs where
the invariants are.

## Open questions raised

- **OQ-8** — delivery surface: does the daily prompt live in the web app, a PR check, a CLI, or an
  editor? Blocks nothing in MVP; blocks the retention story after it.
- **OQ-9** — corpus ingestion mode: clone-and-index, or read-only API access per query? Trades
  freshness against how much private code is at rest. Blocks Epic 2.5.
- **OQ-10** — which source produces exercises worth answering twice? Answered by the premise probe,
  not by argument.
