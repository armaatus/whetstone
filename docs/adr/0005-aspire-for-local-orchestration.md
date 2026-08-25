# ADR-0005 — .NET Aspire for local orchestration, Compose for production

**Status:** Accepted
**Date:** 2026-08-25
**Closes:** OQ-7 (#12)
**Related:** MVP spec §13.9 ("deliberately not set up"), §12 (deployment), ADR-0007 §3

> Cited as "ADR-005" in `tests/Whetstone.Architecture.Tests/LayeringTests.cs` and in spec §14
> ticket 0.10. Same decision; the file uses the repo's four-digit convention.

---

## Context

Spec §13.9 listed Aspire as a genuine open question rather than a rejection, with the trade-off
stated as: service discovery, wired-up OpenTelemetry and a local dashboard largely for free, against
another layer to learn while learning EF Core and Blazor deeply, plus a production deployment story
that adds indirection over a Compose file you can read in full.

**The decision was already taken in code.** The repository contains:

- `src/Whetstone.AppHost` — orchestrates `pgvector/pgvector:pg17` on host port 5433 with the
  `deploy/db-init` mount and the `whetstone-dev-data` named volume, plus the `web` and `worker`
  projects with `WithReference` / `WaitFor`
- `src/Whetstone.ServiceDefaults` — service discovery, OpenTelemetry (ASP.NET Core, HttpClient, gRPC
  and runtime instrumentation, OTLP exporter), standard resilience handlers, `/health` and
  `/alive` endpoints
- `aspire.cli` 13.5.2 pinned in `.config/dotnet-tools.json`; `Aspire.Hosting.PostgreSQL` 13.5.2 in
  `Directory.Packages.props`
- `LayeringTests.cs`, exempting both projects from the §5.1 layering rules while **citing an ADR
  that did not exist**

That last point is why this ADR is written rather than assumed. An architecture test citing a
non-existent decision is a dangling reference, and the longer it dangles the more the exemption
looks arbitrary rather than reasoned. A decision that lives only as a directory which happens to
exist cannot be revisited deliberately, because nobody knows what it was trading off.

## Decision

**Aspire is in, for local orchestration and host wiring only. Production deployment stays Docker
Compose per spec §12.**

The split is the whole decision, so it is stated positively in both directions:

| Aspire is used for | Aspire is not used for |
| --- | --- |
| Local `F5`: bring up Postgres, web and worker together with one command | Production deployment — that is Compose on a single Linux host |
| Service discovery between web, worker and the database | Anything the production topology depends on to be correct |
| OpenTelemetry wiring (NFR-4, NFR-6) and the local dashboard | Being the only place a service's configuration is expressed |
| Health check plumbing (NFR-5) | Replacing `deploy/compose.yml` as the readable, reviewable production artefact |

`deploy/compose.dev.yml` from spec §13.4 is therefore **not** created — the AppHost replaced it, and
carries the same three requirements verbatim: pgvector at initialisation, the `db-init` mount that
creates the three roles from §7.3, host port 5433 so a mistyped connection string fails loudly, and
a named volume so `down` is not destructive.

### Why in

- The topology is genuinely multi-process — web, worker, Postgres — and about to become more so.
  ADR-0007 §3 requires **generation and grading to run in separate processes** with separate
  contexts and different egress allowlists. Hand-wiring service discovery, OTel and health checks
  across four processes is exactly the undifferentiated work Aspire removes.
- NFR-4 wants a trace spanning request → outbox → worker → source/Lens → AI call → SignalR. That is
  a distributed trace across process boundaries, and `ServiceDefaults` already wires the exporter
  and instrumentation to make it possible. Building that by hand teaches nothing this project set
  out to teach.
- The §13.9 objection — "another layer to learn" — is real but is spent on host wiring, not on the
  domain. Nothing on the §17 do-not-assist list touches Aspire.

### Why production stays Compose

- Spec §12's argument is unchanged: one host, one worker, moderate volume. The security posture
  there is explicit and readable — non-root, read-only root filesystem, `no-new-privileges`, dropped
  capabilities, Postgres not published to the host network.
- Aspire's production story adds indirection over an artefact that is currently reviewable in full.
  For a deployment whose main property is being auditable, that is a bad trade.
- Keeping the boundary sharp means a production incident is debugged against a file, not against a
  generator.

### The layering exemption

`Whetstone.AppHost` and `Whetstone.ServiceDefaults` sit **outside** the §5.1 dependency rules and
ARCH-1 must exempt them by name, not by pattern.

They are composition and host concerns: the AppHost references `Whetstone.Web` and
`Whetstone.Worker` directly, which is precisely the reference direction ARCH-1 forbids everywhere
else. Exempting by name means adding a third exempt project is a deliberate edit to a test, which is
the point — a pattern-based exemption would silently absorb future violations.

Neither project may be referenced *by* anything in `src/` other than the host entry points. Nothing
in `Domain`, `Application` or `Infrastructure` may reference either.

### Generation host

ADR-0007 §3's separate generation process is a **`Whetstone.Worker` concern, not a new AppHost
project**, in the first instance: the worker already owns the outbox and the scheduled jobs, and
generation is an outbox handler. If the egress allowlist turns out to require OS-level separation
rather than process separation — a decision for OQ-9 (#102) — that becomes a distinct project and
a distinct container, and this ADR is amended rather than reinterpreted.

## Consequences

### Good

- One command (`dotnet aspire run`) brings up the whole local topology; §13.4's requirements are met
  without a second orchestration file to keep in sync.
- OTel, health checks and resilience are wired before there is anything to observe, which is the
  cheap moment (§13.6's argument, applied to observability).
- The ARCH-1 exemption now cites a real decision.

### Bad / costs

- **Local and production orchestration are different mechanisms.** That is a real parity gap, and it
  is accepted knowingly: the things that historically break parity — Postgres version, extensions,
  roles, port — are pinned identically in both, and those are what §13.1 was protecting.
- `dotnet run --project src/Whetstone.AppHost -c Release` does not honour `-c`; the CLI bundle
  launches from `bin/Debug`. Documented in the README. A papercut, not a defect.
- Aspire version bumps are now a dependency of local development working at all. Pinned with
  `rollForward: false` in the tool manifest for that reason.

### Neutral

- Two extra projects in the solution, bringing the count from twelve to fourteen.

## Alternatives considered

**Plain `compose.dev.yml` per spec §13.4.** One mechanism for local and production, maximum parity,
and every §13.4 requirement expressible. Rejected because it leaves service discovery, OTel wiring
and health checks to be hand-built across what is about to be four processes — work that is pure
overhead against this project's learning goals, and the exact thing Aspire does well.

**Aspire for production as well.** Removes the parity gap outright. Rejected: it trades a Compose
file that can be read and audited in full for generated manifests, on a single-host deployment whose
main virtue is being small enough to understand completely.

**Neither — run everything on the host, Postgres included.** Rejected by spec §13.1, which is
unambiguous that Postgres is only ever run via Docker, because host installs drift in version,
extensions, locale and `search_path` in ways that surface as unreproducible bugs.

## The exit

Aspire is droppable. What would have to be true: the local topology stops being multi-process, or
Aspire's release cadence starts breaking local development more often than it saves time.

The cost at that point is bounded and known — write `deploy/compose.dev.yml` to the §13.4 spec,
hand-wire what `ServiceDefaults` currently provides (OTel exporter, instrumentation, health checks,
resilience handlers) into each host's startup, delete two projects, and remove the ARCH-1 exemption.
A day, not a migration. That bounded exit is part of why the decision is defensible: it is reversible
in a way the production deployment choice would not be.
