// To enable Microsoft Entra ID auth (see WEBHOST-ENTRA-AUTHENTICATION-AUTHORIZATION.md), add the
// Microsoft.Identity.Web NuGet package — IntelliSense will not always guess it for you. Under this
// repo's Centralized Package Management that means a version in Directory.Packages.props plus a
// versionless <PackageReference> here; otherwise: dotnet add package Microsoft.Identity.Web.
// Then uncomment the three Microsoft usings below (JwtBearer/Authorization ship with the web SDK).
using AgentMemoryOS.Abstractions;
using AgentMemoryOS.Example.WebHost;
// using Microsoft.AspNetCore.Authentication.JwtBearer;
// using Microsoft.AspNetCore.Authorization;
// using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);
builder.AddExampleMemory();

// ── Security ───────────────────────────────────────────────────────────────────────────
// This example has NO authentication: every endpoint is open. That is fine for a local demo
// but is NOT production-ready. To put Microsoft Entra ID (Azure AD) in front of the API, follow
// WEBHOST-ENTRA-AUTHENTICATION-AUTHORIZATION.md (it includes a drop-in AddCustomAuthentication
// extension) and uncomment the next line:
// builder.AddCustomAuthentication();

var app = builder.Build();
app.SeedExampleWorkspace();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

// Run one agent turn. Each call uses a fresh conversation (no session id), so recall across
// calls is proof of durable memory rather than in-conversation history.
app.MapPost("/chat", async (ChatRequest request, Microsoft.Agents.AI.ChatClientAgent agent) =>
{
    var response = await agent.RunAsync(request.Message);
    return Results.Ok(new ChatReply(response.Text));
});

// Inspect the materialized fact store directly. This is the deterministic surface the
// integration harness asserts against, independent of model prose.
app.MapGet("/memory/facts", async (string query, IFactStore facts, double? minTrust) =>
{
    var hits = await facts.RecallAsync(MemoryScope.Default, query, minTrust ?? 0.0);
    return Results.Ok(hits.Select(fact => new FactView(fact.Content, fact.Trust, fact.OccurrenceCount)).ToList());
});

await app.RunAsync();

public sealed record ChatRequest(string Message);

public sealed record ChatReply(string Reply);

public sealed record FactView(string Content, double Trust, int OccurrenceCount);

namespace AgentMemoryOS.Example.WebHost
{
    /// <summary>
    /// A public marker type in this assembly so the integration test harness can boot the host
    /// with WebApplicationFactory&lt;ExampleApiMarker&gt; without colliding on the entry point name.
    /// </summary>
    public sealed class ExampleApiMarker
    {
    }
}
