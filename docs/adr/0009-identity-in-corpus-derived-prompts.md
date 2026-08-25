# ADR-0009 — Identity in corpus-derived prompts

**Status:** Accepted
**Date:** 2026-08-25
**Related:** MVP spec §7.6 (privacy), ADR-0006 (exercise sources), ADR-0007 (Lens trust model)

---

## Context

Spec §7.6 states, without qualification:

> No user email, name, or id in any prompt. Ever. Use an opaque per-request correlation id.

Corpus sourcing appears to break this on day one. Commit metadata carries author names and emails.
ADRs are signed. PR descriptions name reviewers. And the flagship `GitHistorySource` exercise is
literally *"code merged under your name that you did not write."*

Read naively, either the privacy rule dies or the best source does.

## Decision

Neither. The rule survives intact, because it was about something narrower than it looked.

### 1. Identity is a selection criterion, not a prompt fact

"You merged this and did not write it" determines **which card the user is given**. It does not need
to appear in the prompt. The scheduler already knows whose card it is; the grader does not need to,
and gains nothing from knowing.

So the personalisation happens in card selection, entirely outside the model's context. The prompt
says *"here is a function"*; the reason this particular user is seeing this particular function is
a fact of the scheduler, and stays there.

This is the same move as withholding: the interesting fact is held one layer away from the model.

### 2. Whetstone never *adds* identity to a prompt

The §7.6 rule is a constraint on **what the system contributes**. It is not, and never was, a claim
that no string in a prompt could ever coincide with a human name. Whetstone must not put the user's
email, display name, or user id into a prompt. That obligation is unchanged and absolute.

### 3. Identity inherent to the artefact is stripped where it is not load-bearing

Corpus content is not authored by Whetstone, and it may contain identity. The rule is
proportionality, applied at ingestion:

| Content | Treatment |
| --- | --- |
| Commit author name / email | **Stripped.** Never needed — the source used it for selection already |
| Committer, reviewer, approver metadata | **Stripped** |
| ADR authorship line, PR description byline | **Stripped** |
| Identity inside the code itself (a `@author` tag, a name in a comment, a test fixture) | **Retained.** It is the artefact; removing it would corrupt the thing being reasoned about |
| Identity in a `SourceRef` shown after the reveal | Retained — rendered in the UI, never sent to a model |

Stripping happens at ingestion, before persistence, so a stripped field is not merely un-prompted
but absent.

### 4. The residual case is disclosed, not denied

Case 3's retained row means a prompt can contain a name that a repository put there. That is a real
residual, and pretending otherwise would make the privacy documentation untrustworthy. It is
disclosed in `docs/privacy/` and surfaced in the tenant-level AI settings, alongside the existing
control to disable AI grading entirely.

A tenant for whom that residual is unacceptable turns off AI grading and keeps self-assessment
(UX-7). That control already exists and is now load-bearing for a second reason.

## Consequences

### Good

- §7.6 survives unamended, and its intent is now stated precisely enough to apply to cases it did
  not anticipate.
- `GitHistorySource` — the best source — is unblocked without a privacy exception.
- Stripping at ingestion means the guarantee holds for every downstream consumer for free,
  including any Lens, rather than being re-litigated per call site.

### Bad / costs

- An ingestion-time stripping pass per source kind, with tests, in Epic 2.5.
- The retained-identity residual is real and must be documented rather than hidden.
- Stripping is irreversible against the stored copy. Attribution shown in the UI must resolve from
  the live corpus via `SourceRef`, not from stored prompt content — a constraint on OQ-9.

### Neutral

- Whetstone's own repository is a corpus for its author, so this behaviour is dogfooded from the
  first probe.

## Alternatives considered

**Amend §7.6 to permit identity in corpus content.** Simplest, and it trades away a rule whose
absoluteness is exactly what makes it enforceable. A privacy rule with a carve-out is a privacy
rule with a growing list of carve-outs. Rejected.

**Pseudonymise identity consistently (author → `Developer A`).** Preserves relational structure for
exercises about ownership patterns, at the cost of a stable pseudonym map — which is itself
personal data, and a re-identification risk in a 20-person tenant. Rejected for MVP; revisit only
if an exercise class genuinely needs cross-commit author identity.

**Strip everything, including identity inside code.** Corrupts the artefact. An exercise about code
that has been silently altered teaches a false model of the system, which is the exact failure mode
ADR-0008 exists to prevent. Rejected.
