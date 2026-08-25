using Whetstone.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Service discovery, resilience, health checks, OpenTelemetry (spec NFR-4, NFR-6).
builder.AddServiceDefaults();

// Render modes per spec 5.2 (recorded verbatim in ADR-002):
//   static SSR         marketing, login, docs
//   streaming SSR      dashboards
//   Interactive Server practice session, exercise authoring
//   Interactive WASM   code editor (post-MVP Monaco)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // Spec 7.5 requires max-age=31536000; includeSubDomains. Ticket 3.7 sets the
    // full header set (CSP with nonces, nosniff, Referrer-Policy, Permissions-Policy).
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

// /health and /alive (spec NFR-5 extends these to readiness incl. DB + outbox depth).
app.MapDefaultEndpoints();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Whetstone.Web.Client._Imports).Assembly);

app.Run();
