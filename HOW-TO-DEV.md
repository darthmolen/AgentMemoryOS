# AgentMemoryOS — developer guide

How to run the example app, stand up the local model + datastore stack, exercise the Azure AI
Foundry path, and build/test the solution. For what the library *is* and how to consume it, see
the [README](README.md); for the design rationale, see
[planning/maf-tiered-memory-design.md](planning/maf-tiered-memory-design.md).

## Solution layout

| Project | Role |
| --- | --- |
| `src/AgentMemoryOS.Abstractions` | DTOs, store/sink interfaces, shared `MemoryText`/`TrustModel` |
| `src/AgentMemoryOS` | `TieredMemoryProvider`, in-memory stores, CPU embedder, channel/sink/reconciler, DI |
| `src/AgentMemoryOS.Postgres` | pgvector-backed fact + vector store + migrations |
| `src/AgentMemoryOS.Redis` | cache-aside decorators (fail-open) |
| `src/AgentMemoryOS.All` | metapackage bundling core + Postgres + Redis |
| `tests/AgentMemoryOS.Example.WebHost` | **dogfood** web app — config-driven agent over HTTP, and the runnable example |
| `tests/AgentMemoryOS.Example.IntegrationTests` | boots the WebHost via `WebApplicationFactory` |
| `tests/*` | unit + Testcontainers integration suites |

## The Example.WebHost

`tests/AgentMemoryOS.Example.WebHost` is a minimal ASP.NET Core app that wires the agent
**entirely from configuration** — it is the shape a consumer of the NuGet package would use.
Endpoints:

- `POST /chat` `{ "message": "…" }` → runs one agent turn (a fresh conversation each call).
- `GET /memory/facts?query=…` → the materialized fact store, for deterministic assertions.
- `GET /healthz`.

All knobs live in the `Memory` configuration section
([appsettings.json](tests/AgentMemoryOS.Example.WebHost/appsettings.json)); secrets live in
user secrets. The integration harness boots this exact host and drives a teach-then-recall
scenario across two independent sessions.

---

## Path 1 — Local (OpenAI-compatible endpoint, e.g. vLLM on an RTX 5090)

### Prerequisites

- .NET 10 SDK, Docker (with the NVIDIA container runtime for the GPU profile).
- An NVIDIA GPU for the model server (the stack defaults to `Qwen/Qwen2.5-7B-Instruct`).

### 1. Start the local stack

```bash
./scripts/start.sh         # postgres + redis only (in-memory dev path)
./scripts/start.sh --gpu   # also starts vLLM serving Qwen (first run downloads the model)
```

`docker compose up` works too; the vLLM service is behind the `gpu` profile:
`docker compose --profile gpu up -d`.

### 2. Run the WebHost directly

The store and cache are config-driven (`Memory:Store` = `InMemory` | `Postgres`,
`Memory:Redis:Enabled`), so the same host demonstrates every backend.

```bash
dotnet run --project tests/AgentMemoryOS.Example.WebHost
curl -s localhost:5000/chat -H 'content-type: application/json' \
  -d '{"message":"Remember: our repo gates PRs on a React Doctor score below 50."}'
curl -s 'localhost:5000/memory/facts?query=react%20doctor%20score'
```

### 3. Run the integration harness (boots the WebHost against the live stack)

```bash
./scripts/start.sh --gpu          # stack must be up
dotnet test tests/AgentMemoryOS.Example.IntegrationTests
```

The InMemory and Postgres+Redis tests run against the live local LLM; they report
**inconclusive** (not failed) if the stack isn't reachable.

---

## Path 2 — Azure AI Foundry (AIF)

The same WebHost targets Azure AI Foundry / Azure OpenAI by setting `Memory:Backend=Foundry`.
Authentication is **key-or-Entra**: it uses the API key if present in user secrets, otherwise
falls back to `DefaultAzureCredential` (`az login`, managed identity, environment, …).

### 1. Configure via user secrets (keys never go in appsettings.json)

```bash
cd tests/AgentMemoryOS.Example.WebHost
dotnet user-secrets init
dotnet user-secrets set "Memory:Backend"            "Foundry"
dotnet user-secrets set "Memory:Foundry:Endpoint"   "https://<your-foundry-resource>.openai.azure.com/"
dotnet user-secrets set "Memory:Foundry:Deployment" "<your-deployment-name>"
# Option A — API key:
dotnet user-secrets set "Memory:Foundry:ApiKey"     "<your-key>"
# Option B — Entra ID: omit the key and run `az login` (DefaultAzureCredential is used).
```

### 2. Run

```bash
dotnet run --project tests/AgentMemoryOS.Example.WebHost
```

That's the whole customer story: configure user secrets, run the host.

> **Maintainer note:** the gated integration test reads three environment variables for a
> quick one-off check against a real Foundry deployment — it is *not* the customer flow
> (customers use the user secrets above):
>
> ```bash
> export MAF_AIF_ENDPOINT="https://<your-foundry-resource>.openai.azure.com/"
> export MAF_AIF_MODEL="<your-deployment-name>"
> export MAF_AIF_API_KEY="<your-key>"   # optional; omit to use DefaultAzureCredential
> dotnet test tests/AgentMemoryOS.Example.IntegrationTests -- --filter "FullyQualifiedName~FoundryBackend"
> ```
>
> Without those variables the Foundry test reports **inconclusive**, so CI stays green.

---

## Build & test

```bash
dotnet build AgentMemoryOS.slnx        # warnings-as-errors, full StyleCop + analyzers
dotnet test  tests/AgentMemoryOS.Tests # unit tests, no external dependencies
dotnet test  AgentMemoryOS.slnx        # everything (Testcontainers spins up pg + redis)
dotnet pack  AgentMemoryOS.slnx -c Release -o ./artifacts   # produces the 5 packages + symbols
```

> Test projects use the **MSTest SDK** on Microsoft.Testing.Platform — pass test arguments
> after a `--` separator, e.g. `dotnet test … -- --filter "FullyQualifiedName~MemoryGate"`.

## Quality gates

The libraries build under Centralized Package Management with `TreatWarningsAsErrors`,
`AnalysisMode=all`, full StyleCop, and required XML docs. The example WebHost opts out of the
internal analyzer suite (`RunAnalyzers=false`) so it reads like idiomatic consumer code. See
[CLAUDE.md](CLAUDE.md) for the full conventions.
