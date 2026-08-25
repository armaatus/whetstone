# ADR-0007 — Lens trust model

**Status:** Accepted
**Date:** 2026-08-25
**Related:** ADR-0006 (exercise sources), MVP spec §7.6 (AI security)

---

## Context

A **Lens** is a versioned, externally-authored rulebook or perspective that inspects a tenant's
corpus and emits candidates. Community Claude Code skills are the first implementation.

This is a very good idea with a very sharp edge: it means **running third-party instructions against
private source code, and feeding the result into the grading pipeline.**

The MVP spec's AI security section rested on a premise that a Lens breaks:

> §7.6.2 — "No tools, no retrieval, no function calling in MVP. The model has no capability to
> reach. An injected instruction can only affect its text output."

A Lens *is* retrieval, and it carries instructions. Its output reaches the corpus on the way in and
the grading pipeline on the way out. The blast radius of a malicious or merely careless Lens is
categorically larger than that of an injected exercise prompt: exfiltrating proprietary code, or
poisoning reference answers so the tutor teaches something false.

The value is real enough to be worth the controls: a Lens gives grading a **named, versioned,
publicly-authored standard** to cite, which is a far stronger position than "our model thinks your
answer scored 0.7."

## Decision

### 1. Lenses are pinned, not fetched

A Lens is registered as `id@version` with a **content hash** recorded at registration. Resolution is
by hash. A hash mismatch fails the generation run loudly; it does not fall back to the newest
version. Upstream changing a skill file is a new version requiring re-approval, never a silent
in-place update.

### 2. Enablement is a tenant-admin act, and it is audited

No Lens reads a corpus until a `TenantAdmin` enables it for that tenant. Registration records who
enabled it, when, at which hash, and what corpus scope it may read. Every enable/disable/upgrade
writes an `audit_event`. A new version of an already-enabled Lens is disabled by default.

### 3. Generation is isolated from grading

Two contexts, two processes, no shared conversation state:

- **Generation:** Lens instructions + corpus content in, candidates out. May read the corpus. May
  not call the grader, read user answers, or read other tenants' anything.
- Outbound network access from generation is an **explicit allowlist**, not a single endpoint. If
  OQ-9 lands on read-only-API-per-query, generation must reach the corpus provider, and ADR-0008 §2's
  inconclusive-resolution rule depends on that call being permitted. The allowlist holds the model
  provider plus, conditionally, the corpus provider — and nothing else, denied by default.
: This is a **weaker guarantee than a single permitted endpoint**, and is recorded as weaker rather
  than quietly widened. A local index (the other OQ-9 option) keeps the stronger form, and that is a
  point in its favour that belongs in OQ-9's decision.
- **Grading:** exercise prompt + rubric criteria in, rubric result out. Never sees Lens
  instructions, never sees `Finding`/`Rationale` (ADR-0006 §3).

An instruction injected into a Lens can therefore influence what candidates are proposed. It cannot
influence how an answer is graded, and it cannot reach a second tenant.

### 4. Lens output is untrusted input

Spec §7.6.7 says model output is untrusted input. Lens output is model output plus third-party
instructions, so it gets the same treatment and then some:

- Structured output only, schema-validated before use; invalid → discard the candidate, log, alert.
- Length caps on every free-text field.
- `HtmlSanitizer` + the restricted Markdig pipeline (§7.5) on anything ever rendered.
- Candidate text is scanned for secrets before persistence — a Lens that quotes a `.env` file into
  a `BeforeState` must not create a durable copy of a credential.

### 5. Every candidate goes through a human review queue before it becomes practice material

No Lens-derived candidate is withheld into a live Exercise without a human accepting it. This is
the last line and the cheapest one. It also generates the labelled data the eval harness needs.

Rationale: candidate acceptance rate per Lens is the single best quality signal available, and it
costs one click to collect.

### 6. Only Rulebook and Perspective tiers are registrable

`Procedure`-tier skills (wizards, merge-conflict walkthroughs) produce nothing gradable and are
rejected at registration. A Lens opts in by emitting the candidate schema; "any skill works" is not
the contract.

### 7. Corpus scope is declared and enforced

A Lens declares which paths it needs. Enablement grants a scope. Requests outside the granted scope
are denied at the adapter, not by asking the Lens to behave.

## Consequences

### Good

- The ecosystem benefit arrives with no cross-tenant data flow, no marketplace to build, and no
  content moderation burden.
- Grading gains a citable external standard.
- Candidate acceptance rate becomes a free per-Lens quality metric.

### Bad / costs

- Registration, enablement, hashing, scoping, and the review queue are real tickets that did not
  exist before.
- Human review is a throughput ceiling on generation. Accepted deliberately — an unreviewed pipeline
  from third-party instructions to practice material is not something to ship.
- Three spec passages are now stale, not one, and all must be rewritten rather than left standing —
  a stale security premise is worse than none:
  - **§7.6.2** "no tools, no retrieval, no function calling" — a Lens is retrieval plus instructions.
  - **§7.6.5** the leak detector as PI-1's enforcement — demoted to backstop by ADR-0006 §3.
  - **§7.6.6** "pass a rubric derived from the reference answer where grading needs it" — that
    escape hatch is exactly what withholding removes, and leaving it authorised re-opens the hole.
- **SEC-4's "≥40 injection attempts" now covers two attackers with different blast radii** — a user
  writing an answer, and a Lens author reaching private source code. One number for both understates
  the second. Restate SEC-4 as two floors, one per attacker, rather than re-slicing the original 40.

### Neutral

- The eval harness (§9.4) gains a third set alongside golden and adversarial: **Lens candidate
  quality**, measured as acceptance rate and as false-finding rate.

## Alternatives considered

**Trust the marketplace.** Ship whatever a user installs. Rejected — the asset at risk is the
customer's proprietary source code, and the attacker only needs one popular skill.

**Fetch Lenses live at generation time.** Always current, and an upstream edit silently changes what
the system teaches. Rejected; pinning by hash is the whole point.

**Run Lenses only on public repositories.** Removes the exfiltration risk and most of the value.
Rejected.

**Skip the human review queue and rely on confidence thresholds.** Rejected for MVP. Revisit once
there is measured acceptance-rate data per Lens tier — that is a decision to take with numbers.
