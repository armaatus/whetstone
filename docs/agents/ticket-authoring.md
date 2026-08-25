# Ticket authoring

How issues in this repo are written. This encodes MVP spec §17 (the working agreement) into the
tracker, so that reading a ticket cannot accidentally undo the point of the project.

## The rule

> **A ticket gives you the shape of the thing. It does not give you the thing.**

Whetstone exists because its author is deliberately retaining skills that agentic tooling erodes. A
ticket that contains the algorithm has already spent the exercise. A ticket that contains *nothing*
is not a ticket — you should not have to re-derive the domain model to start work.

The line is: **structure, contracts, and consequences are given. Derivations are withheld.**

This is the same inversion the product itself performs on `ExerciseCandidate` (ADR-0006). The
tracker practises what it ships.

## Two modes

Every ticket carries exactly one of these labels.

### `ready-for-human` — build it by hand

Applies to everything on the §17 do-not-AI-assist list:

- FSRS and any scheduling maths
- Skill-graph algorithms (cycle detection, topological ordering, mastery propagation)
- Tenancy, RLS, and the connection interceptor
- Concurrency and the outbox
- Any security-critical code
- Anything you would be asked to whiteboard in an interview

**Give:**
- Type names, their relationships, and where they live (project + layer)
- Public method signatures — names, parameters, return types
- The invariant IDs enforced, quoted by ID and one-line meaning
- States and which transitions are legal (as a list, not as an implementation)
- Test names and property statements — what must be true, never how to make it true
- Pointers: spec section, ADR number, the upstream specification URL
- The failure modes worth knowing about before you start
- Why this one is hand-built

**Withhold:**
- Algorithm bodies, in any language, including pseudo-code
- Formulas not already in the author's own spec
- Step-by-step "first do X, then Y" derivations
- Data-structure choices where choosing is the exercise (which queue? which traversal?)
- Links to a reference implementation to copy

**Calibration.** The right feeling on reading one of these is *"I know exactly what I'm building and
exactly what 'done' means, and I have not yet been told how."*

> ✅ "`SkillGraph.AddPrerequisite(SkillId child, SkillId parent)` must enforce INV-1 (the
> prerequisite graph is acyclic) at the moment of addition, not on read. Throws — it does not return
> a bool a caller can ignore. Cycle detection is unit-tested for self-loops (A→A) and 3-node cycles
> (A→B→C→A). Note that the naive check is O(V+E) *per edge*; whether that matters at 50 skills is
> yours to decide and to justify in the PR."

> ❌ "Do a depth-first search from `parent`; maintain a visited set; if you reach `child`, there's a
> cycle. Colour nodes white/grey/black for the topological sort."

### `ready-for-agent` — plumbing, assistance is fine

Applies to what §17 explicitly permits: scaffolding, migration setup, test fixtures, seed data, DTO
mapping, config, CI, docs, adapters.

These may be fully prescriptive — commands, file paths, exact config, worked examples. A ticket
being AFK-able is the goal, and there is no skill being protected here. Being vague to seem
consistent wastes the author's time.

## Structure

Keep the existing format. Every ticket has:

```markdown
**Spec:** §<section>, ticket <n> · implements <what>
**Size:** 2–8 h
**Layer:** <project> — <constraint>
**Blocked by:** <issue / ADR / OQ>          (omit if none)

## Goal
One or two sentences. What exists when this is done.

## Shape                                     (ready-for-human only)
Types, signatures, states. The scaffolding, not the fill.

## Acceptance criteria
- [ ] Checkable statements. Invariant IDs by number.

## Context
Domain background, quoted spec/ADR lines, the failure modes worth knowing.

## Working agreement (§17)                   (ready-for-human only)
Why this is hand-built, and the `docs/ai-log.md` note if the rule gets broken.
```

## Vocabulary

Use `CONTEXT.md`'s terms exactly. In particular:

- A **Lens** is a community rulebook. A **Skill** is a competency node. Never swap them.
- **Withholding** is the candidate → exercise operation. Not "converting", not "publishing".
- **Comprehension** is module-level mastery — the team view's framing. Not "skill decay".

If a ticket needs a concept that is not in `CONTEXT.md`, that is a signal: either the language is
drifting, or the glossary has a gap worth closing before the ticket is written.

## Flag ADR conflicts

If a ticket contradicts an ADR, say so in the ticket rather than silently overriding:

> _Contradicts ADR-0008 (drift retires, never edits) — but worth reopening because…_
