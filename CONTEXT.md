# Whetstone — Context

The domain glossary. When code, an issue title, a test name, or an ADR names a concept, it uses
the term as defined here. Synonyms listed under "avoid" are avoided deliberately.

Decisions live in `docs/adr/`. The product argument lives in `docs/product-thesis.md`.

---

## 1. What the system is

A deliberate-practice platform whose exercises are **derived from the user's own codebase**, not
authored by hand and not drawn from a shared bank. It watches what you have stopped understanding
about the systems you are responsible for, and schedules questions that make you re-derive it.

The AI is a Socratic tutor and rubric grader. It never produces the answer (**PI-1**).

---

## 2. Ubiquitous language

### Corpus and sourcing

**Corpus**
: The tenant's body of source material — a Git repository and everything derivable from it:
  commits, merged pull requests, ADRs, file structure. The corpus is tenant-private and never
  leaves the tenant boundary.

**ExerciseSource**
: A port that inspects a corpus and yields `ExerciseCandidate`s. Each implementation is one way of
  finding something worth practising.
: Kinds: `GitHistory`, `PullRequest`, `Adr`, `Lens`, `Manual`.
: _Avoid:_ "generator", "importer", "scraper".

**Lens**
: A versioned, externally-authored rulebook or analytical perspective that can inspect a corpus and
  emit candidates. Community Claude Code skills are the first implementation
  (e.g. `rust-skills`, `codebase-design`).
: A Lens is **third-party instruction executed against private code** and is governed by ADR-0007.
: _Avoid:_ calling a Lens a "skill". That word is taken — see **Skill** below. This collision is
  the single most likely source of confusion in the codebase; the rename is not optional.

**Lens tier**
: `Rulebook` — dense, checkable, citable rules (high yield, low judgment).
: `Perspective` — judgment-heavy analysis (lower yield, higher practice value).
: `Procedure` — produces nothing gradable. Out of scope; not registrable.

### The inversion

**ExerciseCandidate**
: A finding that is not yet an exercise. Holds **both halves** — the situation and the resolution:

  | Field | Meaning |
  | --- | --- |
  | `Location` | file + range, pinned to a commit sha |
  | `BeforeState` | the code or artefact as it stands |
  | `Finding` | what the source says is notable — **withheld** |
  | `Rationale` | why — **withheld** |
  | `Provenance` | where it came from (see below) |
  | `Confidence` | the source's own confidence, 0..1 |

: Candidates sit in a review queue. A candidate is not practice material until it has been
  withheld.

**Candidate state**
: `Proposed` — emitted by a source, awaiting human review.
: `Accepted` — a human accepted it; eligible for withholding.
: `Rejected` — a human rejected it, **with a reason**. The reason is not bookkeeping: rejection
  rate per source and per Lens is the cheapest quality signal the system has (ADR-0007 §5).
: `Withheld` — an Exercise has been derived from it. Terminal.
: Transitions: `Proposed → Accepted → Withheld` and `Proposed → Rejected`. No other edges, and
  nothing returns from a terminal state. Schema-invalid source output is discarded before it ever
  becomes `Proposed` (ADR-0007 §4) — discarding is not a state.

**Withholding**
: The named operation that derives an `Exercise` from an `ExerciseCandidate`. The prompt is built
  from `BeforeState`; `Finding` and `Rationale` become the reference answer and rubric criteria.
: **The reference half is never present in any prompt sent for grading.** PI-1 is therefore
  structural, not a matter of prompt discipline — the model cannot leak what was never in its
  context. See ADR-0006.
: _Avoid:_ "converting", "publishing" a candidate. The name states the guarantee.

**Provenance**
: Where an exercise came from: `SourceKind`, `SourceRef` (commit sha / PR number / ADR path /
  `lens-id@version`), `ContentHash`, `GeneratedAt`, `Confidence`, and `LensTier` where applicable.
  Enables attribution in feedback, drift detection, regeneration, and session weighting.
: `Confidence` and `LensTier` are **copied onto the Exercise at withholding time**, not joined back
  to the candidate. The candidate's content is purged on the retention schedule while the Exercise
  outlives it, so a join is not a reliable read.
: Provenance is **partially withheld at presentation time** — see below.

**Provenance reveal**
: `SourceKind` is shown before the learner answers ("this came from an ADR"). `SourceRef` and the
  rest are shown only after (`ADR-0012`).
: The reason is PI-1 at the content layer: for whole classes of exercise the reference *is* the
  answer. "Explain why `OrderProjection` caches by tenant" plus a visible link to the ADR that says
  why is not an exercise, it is a lookup. Withholding applies to provenance, not only to `Finding`
  and `Rationale`.

**Drift**
: A repo-derived exercise whose provenance no longer resolves against the corpus — the file moved,
  the code was refactored, the PR was reverted. Drifted exercises are **retired**, never edited.

### Practice

**Skill**
: A named competency node. Skills form a DAG.
: `SkillKind.Module` — a real part of the corpus (`src/Billing`). Mastery over a Module is what the
  team view reports as comprehension.
: `SkillKind.Concept` — an abstract competency, typically one Lens rule or rule group.
: Skills are **derived** from corpus structure and registered Lenses. Hand-authoring a skill is the
  exception, not the entry point.

**Exercise**
: An authored or derived practice item targeting exactly one Skill. Versioned; a published
  exercise's prompt and rubric are immutable.

**Card**
: The pairing of one user with one exercise. Holds scheduling state. The unit FSRS acts on.
: A Card's review history **survives the retirement of its Exercise** — mastery accrues to the
  Skill, not to the Exercise.

**Review**
: One attempt at a Card. Immutable once graded.

**Session**
: An ordered set of Cards presented in one sitting.

**MasterySnapshot**
: An immutable `(user, skill, timestamp, score)` record. Append-only.

**Comprehension**
: Mastery aggregated over `SkillKind.Module` for a user. The team view of comprehension is the
  **bus factor** report: who actually understands which part of the system.
: _Avoid:_ "skill decay" as the headline framing on the team view. A lead acts on bus factor; nobody
  acts on a decay chart.

**Tenant**
: An organisation. The isolation boundary for all data, all corpora, and all candidates.

---

## 3. Invariants that constrain the language

- A candidate's `Finding`/`Rationale` may be persisted, but may **never** be serialised into a
  grading prompt (ADR-0006).
- No candidate becomes a live Exercise without passing through `Accepted` — there is no path from
  `Proposed` straight to `Withheld` (ADR-0007 §5).
- A Lens is pinned by `id@version` + content hash, and must be enabled per tenant by an admin
  before it may read a corpus (ADR-0007).
- Generation and grading run in **separate contexts**. Lens output is untrusted input on the way in,
  the same as model output is untrusted on the way out.
- An Exercise is never edited in place. Change creates a version; drift creates a retirement.

---

## 4. What this is not

Not LeetCode, and not competing with it. There is no shared exercise bank, no leaderboard, and no
interview-prep positioning. A generic exercise bank is a commodity any model can regenerate for
free; the defensible asset is **the tenant's own corpus plus a decay model over it**.
