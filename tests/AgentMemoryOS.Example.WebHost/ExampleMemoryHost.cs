using System.ClientModel;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using AgentMemoryOS;
using AgentMemoryOS.Abstractions;
using AgentMemoryOS.Postgres;
using AgentMemoryOS.Redis;
using AgentMemoryOS.Stores;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace AgentMemoryOS.Example.WebHost;

/// <summary>
/// Wires the tiered-memory agent into the host purely from configuration. This is the shape
/// a customer would use to consume the package: choose a chat backend (local OpenAI-compatible
/// endpoint or Azure AI Foundry) and a store (in-memory or Postgres + optional Redis) entirely
/// through appsettings.json and user secrets.
/// </summary>
public static class ExampleMemoryHost
{
    public static void AddExampleMemory(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration.GetSection("Memory").Get<MemoryConfig>() ?? new MemoryConfig();
        builder.Services.AddSingleton(config);

        var (chatClient, modelId) = CreateChatBackend(config);
        builder.Services.AddSingleton(chatClient);

        // One call wires stores, caching, options, the background pipeline, and the provider — the
        // builder owns registration ordering. The extractor reuses the IChatClient registered above.
        builder.Services.AddTieredMemory(memory =>
        {
            if (string.Equals(config.Store, "Postgres", StringComparison.OrdinalIgnoreCase))
            {
                memory.UsePostgres(config.Postgres.ConnectionString, config.Postgres.EmbeddingDimensions);
            }

            if (config.Redis.Enabled)
            {
                memory.UseRedisCache(config.Redis.ConnectionString);
            }

            memory.Configure(options =>
            {
                options.MinTrust = 0.0;
                options.MinVectorScore = 0.6;
            });
        });

        // One call attaches the tiered-memory provider to the agent.
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IChatClient>().CreateMemoryAgent(sp, options =>
        {
            options.Name = "ExampleMemoryAgent";
            options.ChatOptions = new ChatOptions
            {
                ModelId = modelId,
                Instructions = "You are a helpful engineering assistant. Use any remembered context that is provided.",
            };
        }));
    }

    public static void SeedExampleWorkspace(this WebApplication app)
    {
        var config = app.Services.GetRequiredService<MemoryConfig>();
        if (!string.IsNullOrWhiteSpace(config.Workspace))
        {
            app.Services.GetRequiredService<InMemoryWorkspaceStore>().Set(MemoryScope.Default, config.Workspace);
        }
    }

    private static (IChatClient ChatClient, string ModelId) CreateChatBackend(MemoryConfig config)
    {
        if (string.Equals(config.Backend, "Foundry", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = new Uri(config.Foundry.Endpoint);

            // Use the API key from user secrets when present; otherwise fall back to Entra ID
            // (DefaultAzureCredential: az login, managed identity, environment, etc.).
            var azure = string.IsNullOrWhiteSpace(config.Foundry.ApiKey)
                ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential())
                : new AzureOpenAIClient(endpoint, new AzureKeyCredential(config.Foundry.ApiKey));

            return (azure.GetChatClient(config.Foundry.Deployment).AsIChatClient(), config.Foundry.Deployment);
        }

        var openAi = new OpenAIClient(
            new ApiKeyCredential(config.Local.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(config.Local.Endpoint) });

        return (openAi.GetChatClient(config.Local.Model).AsIChatClient(), config.Local.Model);
    }
}
