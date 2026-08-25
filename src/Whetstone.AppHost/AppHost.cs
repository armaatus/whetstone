var builder = DistributedApplication.CreateBuilder(args);

// Postgres 17 with pgvector, matching production (spec 12).
//
// pgvector is enabled now even though MVP does not use it: adding the extension
// later needs superuser on a running production database (spec 13.4).
//
// The db-init mount runs deploy/db-init/*.sql on first initialisation of the data
// volume. That is where the three roles from spec 7.3 are created.
//
// WARNING (spec 13.4): until deploy/db-init/01-roles.sql creates whetstone_app and
// the app is switched to connect as it, this connection is the container superuser,
// which BYPASSES every RLS policy. Ticket 0.6.
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17")
    .WithBindMount("../../deploy/db-init", "/docker-entrypoint-initdb.d", isReadOnly: true)
    .WithDataVolume("whetstone-dev-data")
    .WithHostPort(5433); // spec 13.4: a mistyped connection string should fail loudly

var whetstoneDb = postgres.AddDatabase("whetstone");

builder.AddProject<Projects.Whetstone_Worker>("worker")
    .WithReference(whetstoneDb)
    .WaitFor(whetstoneDb);

builder.AddProject<Projects.Whetstone_Web>("web")
    .WithReference(whetstoneDb)
    .WaitFor(whetstoneDb)
    .WithExternalHttpEndpoints();

builder.Build().Run();
