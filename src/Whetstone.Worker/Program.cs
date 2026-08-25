var builder = Host.CreateApplicationBuilder(args);

// Service discovery, resilience, health checks, OpenTelemetry (spec NFR-4, NFR-6).
builder.AddServiceDefaults();

// Hosted services land here:
//   - outbox dispatcher      (ticket 2.11 — FOR UPDATE SKIP LOCKED, hand-written per spec 17)
//   - SnapshotMastery        (ticket 5.5, daily)
//   - PurgeDeletedTenant     (ticket 6.2)
// The host runs with none registered; that is expected until Epic 2.

var host = builder.Build();
host.Run();
