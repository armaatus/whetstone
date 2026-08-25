# ADR-0002 — Blazor render modes

**Status:** Accepted
**Date:** 2026-08-25
**Related:** MVP spec §5.2 (render modes), §5.1 (projects), §6.4 (team dashboard), §7.4 (circuits),
ADR-0001 (stack), ADR-0006 (withholding)

---

## Context

A Blazor Web App picks a render mode **per component**. The default template invites you to set one
globally on `<Routes />` and never think about it again, and not taking that shortcut is most of what
this ADR is for. Render mode is a per-surface trade: first paint against interactivity latency,
server memory against shipped payload, and — the part that gets forgotten — *what code and what
state end up on the client*.

Getting it wrong is expensive in both directions:

- **Global Interactive Server** puts every page, including marketing and login, behind a SignalR
  circuit. A circuit per anonymous visitor is server state bought for nothing, and it delays first
  paint on exactly the pages judged on first paint.
- **Global Interactive WebAssembly** ships the application to the browser. For a system whose
  central guarantee is that the server holds the withheld half of an exercise (ADR-0006), shipping
  domain code to the client is the wrong thing to have to remember to override.

Spec §5.2 fixes the answer, and §5.2 itself instructs that the table be recorded here verbatim,
because "why that mode for that surface" is a standard interview probe and a table reconstructed
from memory is a claim rather than evidence.

The repository already matches this shape. `src/Whetstone.Web/Program.cs` registers both interactive
modes; `App.razor` and `Routes.razor` carry **no** global `@rendermode`. Every interactive surface is
therefore an explicit opt-in today. That is the intended state, not an omission waiting to be tidied.

## Decision

### 1. The surface → mode table, verbatim from spec §5.2

| Surface | Mode | Rationale |
| --- | --- | --- |
| Marketing, login, docs | Static SSR | No interactivity; fastest first paint |
| Dashboards | Static SSR + streaming rendering | Slow aggregate queries stream in |
| Practice session | Interactive Server | Low latency, server-held session state, no code shipped |
| Exercise authoring | Interactive Server | Rich validation against server-side domain rules |
| Code editor (post-MVP Monaco) | Interactive WebAssembly | Keystroke latency must not round-trip |

### 2. Render mode is opted into per component, never globally

No `@rendermode` on `<Routes />`, `<HeadOutlet />` or `MainLayout`. **Static SSR is the default by
absence:** a component is non-interactive unless someone wrote down that it is not. A new page
therefore starts at the cheapest and safest mode and is moved deliberately, and the move shows up in
the diff as a line someone had to add.

### 3. `Whetstone.Web.Client` ships no interactive components in MVP

Per §5.1 the client project has no server-side references, and the only row in the table assigned to
WebAssembly is the post-MVP Monaco editor. So the project exists as **wiring** — registered in
`Program.cs`, debuggable, and empty of features — rather than as a home for anything shipped in MVP.

That is deliberate. Adding a WebAssembly render target after a component tree exists means
retro-fitting a second execution model across all of it; wiring it while the tree is empty costs one
`AddAdditionalAssemblies` call.

### 4. Interactive Server on the practice session is a security choice, not only a latency one

The session's ordered card list, the current card, and the in-flight answer live on the server. Only
the current prompt is rendered to the browser. Withholding (ADR-0006) therefore survives a user with
devtools open, because the withheld half was never serialised to the client — the same structural
argument PI-1 rests on, applied one layer out.

This inherits §7.4's obligations rather than restating them: circuits revalidate authentication every
30 minutes, and Data Protection keys are persisted to the database. Without the second, every restart
drops every circuit and breaks antiforgery — which reads as a Blazor problem and is a configuration
one.

### 5. Dashboards get streaming, not interactivity

§6.4's team dashboard is a slow aggregate query against a materialised view. Streaming SSR puts the
shell in front of the user while the query runs, with no circuit and no shipped code. It is the
cheapest mode that addresses the actual problem, which is *perceived* latency against NFR-2
(p95 < 500 ms). Reaching for Interactive Server here would buy interactivity the surface does not
have and pay for it in server state per viewer.

## Consequences

### Good

- Anonymous traffic — marketing, login, docs — costs no server-side session state at all.
- No domain code reaches the browser in MVP.
- Render mode is visible in the diff, so a change of execution model is reviewable as such.

### Bad / costs

- Two interactive modes are registered for one post-MVP feature, and both hosting models must keep
  working in CI and in the Playwright E2E path (§11).
- Interactive Server ties the practice loop to connection stability. `ReconnectModal` is a
  first-class UI concern with a real failure story to design, not a template leftover.
- Circuit memory scales with concurrently practising users. NFR-3 bounds grading throughput; circuit
  count is a separate capacity question that this ADR does not answer and §10 does not currently
  carry an NFR for.
- Mixed modes make "where does this code run?" a question every component author must answer. The
  mitigation is convention and review, not tooling.

### Neutral

- Interactive components prerender by default, so any component whose first render touches
  tenant-scoped data runs that path twice. That is a correctness constraint on component authors
  (idempotent initialisation), not an argument against the mode.

## Alternatives considered

**One global Interactive Server render mode.** Simplest to write, and it makes every page a circuit —
including the ones with nothing to be interactive about. Rejected: it spends server state on
anonymous traffic and gives up the fastest-first-paint property that §5.2 buys for marketing and
login.

**Interactive Auto.** Starts on the server and silently switches to WebAssembly once the runtime is
cached. Rejected: it makes "where does this run" a *runtime* property that varies per user and per
visit, and every component then has to be correct under both models. For a system where the server
holding the withheld half is the guarantee (§4 above), that ambiguity is not one to accept for a
latency improvement on a repeat visit.

**All WebAssembly behind a JSON API.** Rejected: it ships domain code, requires a public API surface
that does not otherwise need to exist, and puts the withheld half one fetch away from a determined
learner.

**Razor Pages or MVC plus a JavaScript framework.** Rejected on §13.9's grounds — it is two
programming models, two validation implementations, and a serialisation boundary in the middle of the
practice loop, in exchange for nothing this application needs.
