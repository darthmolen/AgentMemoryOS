# AgentMemoryOS

**Durable, tiered memory for Microsoft Agent Framework (MAF) agents.** .NET 10 / C#.

LLM agents are amnesiacs. Between turns — and especially between sessions — they start from
zero: the same facts get re-explained, corrections never stick, and hard-won context evaporates
the moment a conversation ends. AgentMemoryOS gives a MAF agent a memory that *persists* and
*improves*.

It ports the `memory-os` pattern onto MAF's `AIContextProvider` lifecycle, so memory lives
inside the agent's own request loop instead of bolted on beside it:

- **Before each turn** it injects an always-on workspace plus **gated, deduplicated** recall —
  only what's relevant, never the whole corpus, so you don't pay for context bloat.
- **After each turn** it extracts durable observations (skipping greetings and small talk) and
  hands them off the request path.
- **In the background** a reconciler turns those observations into trust-scored facts and a
  vector-searchable index — so memory gets *sharper over time* without slowing the agent down.

Three tiers, mapped to how agents actually use memory:

| Tier | What it holds | When it's recalled |
| --- | --- | --- |
| **L1 Workspace** | always-on markdown (who the user is, standing instructions) | every turn |
| **L3 Facts** | trust-scored statements, reinforced by repeated observation | when relevant |
| **L5 Vector** | semantic recall over everything observed | when relevant |

Every store call is keyed by a `MemoryScope`, so today's single agent and tomorrow's swarm
(many agents sharing one template's memory) run the same code.

- **Default:** zero-dependency in-memory stores + a deterministic CPU embedder — `dotnet add` and go.
- **Optional:** Postgres + pgvector (durable facts + vectors), Redis (cache-aside).
- **Backends:** any OpenAI-compatible endpoint (vLLM, Ollama, …) or Azure AI Foundry — you bring
  the `IChatClient`, the library reuses it as the extractor.

## Quick start

Install à la carte, or the metapackage for the whole stack:

```bash
dotnet add package AgentMemoryOS            # core (in-memory, zero dependencies)
dotnet add package AgentMemoryOS.Postgres   # optional: durable Postgres + pgvector store
dotnet add package AgentMemoryOS.Redis      # optional: Redis cache-aside
# ...or everything in one reference:
dotnet add package AgentMemoryOS.All
```

One call wires the stores, caching, the background reconciler, and the provider; a second
attaches memory to an agent. Memory reuses the `IChatClient` you already registered — the
packages never build a chat client for you.

```csharp
using Microsoft.Extensions.DependencyInjection;

services.AddSingleton<IChatClient>(myChatClient);

services.AddTieredMemory(memory => memory
    .UsePostgres(postgresConnectionString)   // omit for the zero-dependency in-memory default
    .UseRedisCache(redisConnectionString)    // optional cache-aside
    .Configure(o => o.MinTrust = 0.5));

// later, from the resolved IServiceProvider:
var agent = chatClient.CreateMemoryAgent(serviceProvider, o =>
{
    o.Name = "Assistant";
    o.ChatOptions = new ChatOptions { Instructions = "You are a helpful assistant." };
});
```

The builder owns registration ordering, so `UsePostgres` / `UseRedisCache` / `Configure`
compose in any order. There is also an `IConfiguration` overload —
`services.AddTieredMemory(config.GetSection("Memory"), memory => memory.UsePostgres(conn))` —
that binds the recall options from configuration.

## How it works

`TieredMemoryProvider : AIContextProvider` overrides recall (before the model call) and capture
(after it); a background `MemoryReconciler` materializes captured observations into trust-scored
facts and a vector index, off the request path. Stores sit behind small interfaces keyed by
`MemoryScope`, so swapping in-memory for Postgres/Redis — or a single agent for a shared swarm —
never touches provider logic. The full rationale is in
[the design doc](https://github.com/darthmolen/AgentMemoryOS/blob/main/planning/maf-tiered-memory-design.md).

## Running it locally

Standing up the example app, the local model + datastore stack (Postgres / Redis / vLLM), the
Azure AI Foundry path, and build/test instructions all live in
**[HOW-TO-DEV.md](https://github.com/darthmolen/AgentMemoryOS/blob/main/HOW-TO-DEV.md)**.

## License

[MIT](https://github.com/darthmolen/AgentMemoryOS/blob/main/LICENSE).
