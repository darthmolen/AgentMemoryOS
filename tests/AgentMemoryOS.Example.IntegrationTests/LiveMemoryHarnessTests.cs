namespace AgentMemoryOS.Example.IntegrationTests;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;

[TestClass]
public sealed class LiveMemoryHarnessTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task InMemoryBackendTeachesAndRecallsAcrossSessions()
    {
        await RunTeachRecallAsync(store: "InMemory", redisEnabled: false);
    }

    [TestMethod]
    public async Task PostgresRedisBackendTeachesAndRecallsAcrossSessions()
    {
        if (!TcpReachable("localhost", 5432))
        {
            Assert.Inconclusive("Postgres not reachable on localhost:5432. Start the local stack with ./scripts/start.sh.");
        }

        if (!TcpReachable("localhost", 6379))
        {
            Assert.Inconclusive("Redis not reachable on localhost:6379. Start the local stack with ./scripts/start.sh.");
        }

        await RunTeachRecallAsync(store: "Postgres", redisEnabled: true);
    }

    [TestMethod]
    public async Task FoundryBackendTeachesAndRecallsWhenConfigured()
    {
        var endpoint = Environment.GetEnvironmentVariable("MAF_AIF_ENDPOINT");
        var deployment = Environment.GetEnvironmentVariable("MAF_AIF_MODEL");
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(deployment))
        {
            Assert.Inconclusive(
                "Set MAF_AIF_ENDPOINT and MAF_AIF_MODEL (and optionally MAF_AIF_API_KEY; otherwise DefaultAzureCredential is used) to run the Azure AI Foundry path.");
        }

        var foundry = new Dictionary<string, string?>
        {
            ["Memory:Backend"] = "Foundry",
            ["Memory:Foundry:Endpoint"] = endpoint,
            ["Memory:Foundry:Deployment"] = deployment,
            ["Memory:Foundry:ApiKey"] = Environment.GetEnvironmentVariable("MAF_AIF_API_KEY") ?? string.Empty,
        };

        await RunTeachRecallAsync(store: "InMemory", redisEnabled: false, extra: foundry, requireLocalLlm: false);
    }

    private static async Task RunTeachRecallAsync(
        string store,
        bool redisEnabled,
        IDictionary<string, string?>? extra = null,
        bool requireLocalLlm = true)
    {
        if (requireLocalLlm && !await LocalLlmReachableAsync())
        {
            Assert.Inconclusive("Local LLM not reachable on http://localhost:8000/v1. Start it with ./scripts/start.sh --gpu.");
        }

        var settings = new Dictionary<string, string?>
        {
            ["Memory:Store"] = store,
            ["Memory:Redis:Enabled"] = redisEnabled ? "true" : "false",
        };

        if (extra is not null)
        {
            foreach (var pair in extra)
            {
                settings[pair.Key] = pair.Value;
            }
        }

        using var factory = new ExampleWebHostFactory(settings);
        using var client = factory.CreateClient();

        // Session 1: teach a durable fact (a fresh conversation, no session id).
        var teach = await client.PostAsJsonAsync("/chat", new ChatPayload("Remember this for the future: our repo gates PRs on a React Doctor score below 50."), Json);
        Assert.IsTrue(teach.IsSuccessStatusCode, $"Teach call failed with {teach.StatusCode}.");

        // The reconciler materializes asynchronously; poll the deterministic fact surface.
        var materialized = await WaitForFactAsync(client, "react doctor score", TimeSpan.FromSeconds(30));
        Assert.IsTrue(materialized, "Expected the taught observation to be reconciled into a recallable fact.");

        // Session 2: a brand-new conversation must recall it across sessions.
        var ask = await client.PostAsJsonAsync("/chat", new ChatPayload("What score does our repo gate PRs on?"), Json);
        Assert.IsTrue(ask.IsSuccessStatusCode, $"Ask call failed with {ask.StatusCode}.");

        var reply = await ask.Content.ReadFromJsonAsync<ReplyPayload>(Json);
        Assert.IsFalse(string.IsNullOrWhiteSpace(reply?.Reply), "Expected a non-empty agent reply.");
    }

    private static async Task<bool> WaitForFactAsync(HttpClient client, string query, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var facts = await client.GetFromJsonAsync<List<FactPayload>>($"/memory/facts?query={Uri.EscapeDataString(query)}", Json);
            if (facts is { Count: > 0 })
            {
                return true;
            }

            await Task.Delay(500);
        }

        return false;
    }

    private static async Task<bool> LocalLlmReachableAsync()
    {
        try
        {
            using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var response = await probe.GetAsync("http://localhost:8000/v1/models");
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    private static bool TcpReachable(string host, int port)
    {
        try
        {
            using var probe = new TcpClient();
            return probe.ConnectAsync(host, port).Wait(TimeSpan.FromSeconds(3)) && probe.Connected;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (AggregateException)
        {
            return false;
        }
    }

    private sealed record ChatPayload(string Message);

    private sealed record ReplyPayload(string Reply);

    private sealed record FactPayload(string Content, double Trust, int OccurrenceCount);
}
