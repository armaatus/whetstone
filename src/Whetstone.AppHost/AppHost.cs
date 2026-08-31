using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

// Role passwords for deploy/db-init/01-roles.sql (spec 7.3, ticket 0.6).
//
// Generated, and persisted to this project's user secrets, which is load-bearing
// rather than convenient: /docker-entrypoint-initdb.d runs ONCE, on first
// initialisation of the whetstone-dev-data volume. The password baked into each
// role at that moment has to be the one the app still presents on every later
// run. A value regenerated per run would leave the app unable to authenticate
// against roles it created itself. Special characters are excluded because these
// values are interpolated into Npgsql connection strings below.
//
// They are not in the SQL file because that file is committed and hooks/pre-commit
// runs gitleaks over it. The init script reads them with \getenv and aborts
// startup if any is missing.
var migratorPassword = builder.AddParameter("whetstone-migrator-password",
    new GenerateParameterDefault { MinLength = 24, Special = false }, secret: true, persist: true);
var appPassword = builder.AddParameter("whetstone-app-password",
    new GenerateParameterDefault { MinLength = 24, Special = false }, secret: true, persist: true);
var readonlyPassword = builder.AddParameter("whetstone-readonly-password",
    new GenerateParameterDefault { MinLength = 24, Special = false }, secret: true, persist: true);

// Postgres 17 with pgvector, matching production (spec 12).
//
// pgvector is enabled now even though MVP does not use it: adding the extension
// later needs superuser on a running production database (spec 13.4).
//
// The db-init mount runs deploy/db-init/*.sql on first initialisation of the data
// volume — where the three roles from spec 7.3 are created, along with the
// `whetstone` database itself. Editing those files after the volume exists has no
// effect and reports no error; deploy/db-init/README.md has the reset.
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17")
    .WithBindMount("../../deploy/db-init", "/docker-entrypoint-initdb.d", isReadOnly: true)
    .WithDataVolume("whetstone-dev-data")
    .WithHostPort(5433) // spec 13.4: a mistyped connection string should fail loudly
    .WithEnvironment("WHETSTONE_MIGRATOR_PASSWORD", migratorPassword)
    .WithEnvironment("WHETSTONE_APP_PASSWORD", appPassword)
    .WithEnvironment("WHETSTONE_READONLY_PASSWORD", readonlyPassword);

// 01-roles.sql already created this database, owned by whetstone_migrator. Aspire
// issues its own CREATE DATABASE once the server reports healthy, hits 42P04
// duplicate_database, handles it, and logs "Database 'whetstone' already exists".
// Kept for the health gate that WaitFor() below depends on — and as the safety net
// if the volume ever predates the init script.
var whetstoneDb = postgres.AddDatabase("whetstone");

var pgHost = postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Host);
var pgPort = postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Port);

// Two connection strings, deliberately named now rather than renamed in #26
// (spec 6.5 applies migrations from a separate one-shot step, as a different role).
//
// Neither is called "whetstone". That name belongs to the database resource above,
// whose connection string carries the CONTAINER SUPERUSER — the credential spec
// 13.4 exists to keep away from application code. Nothing here references it, and
// because the names differ, nothing can pick it up by accident either.
var appConnection = builder.AddConnectionString("whetstone-app",
    ReferenceExpression.Create(
        $"Host={pgHost};Port={pgPort};Database=whetstone;Username=whetstone_app;Password={appPassword.Resource}"));

var migratorConnection = builder.AddConnectionString("whetstone-migrator",
    ReferenceExpression.Create(
        $"Host={pgHost};Port={pgPort};Database=whetstone;Username=whetstone_migrator;Password={migratorPassword.Resource}"));

// Delivered as Whetstone__Database__ConnectionString rather than via WithReference: ticket 0.7
// makes Whetstone:Database:ConnectionString the one key both hosts bind (and validate at boot),
// and production sets the same variable. A second copy under ConnectionStrings:whetstone-app
// would be a second name for the same secret, which is how the wrong one gets picked up.
builder.AddProject<Projects.Whetstone_Worker>("worker")
    .WithEnvironment("Whetstone__Database__ConnectionString", appConnection.Resource.ConnectionStringExpression)
    .WaitFor(whetstoneDb);

builder.AddProject<Projects.Whetstone_Web>("web")
    .WithEnvironment("Whetstone__Database__ConnectionString", appConnection.Resource.ConnectionStringExpression)
    .WaitFor(whetstoneDb)
    .WithExternalHttpEndpoints();

builder.Build().Run();
