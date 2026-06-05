# Plan: MAF Tiered-Memory Provider (`maf-memoryos`)

## Context

`planning/maf-tiered-memory-design.md` describes porting the `memory-os` 6-layer
agent-memory pattern onto **Microsoft Agent Framework (MAF)**, .NET/C#. MAF's
`AIContextProvider` two-phase lifecycle (`InvokingAsync` = pre-LLM recall,
`InvokedAsync` = post-LLM extract/persist) *is* the hook model memory-os bolts onto
Hermes, so the adaptation is mostly recognition, not porting.

This repo is **greenfield** — only the design doc exists, no code, no commits yet
(remote `git@github.com:darthmolen/maf-memoryos.git`). We are building the
**single-agent baseline** (design §3): L1 always-on workspace, L3 trust-scored facts,
L5 vector recall, plus the token-efficiency trio (relevance gating, per-session dedup,
triviality filter). We defer the swarm/template-keyed shared store and the background
compactor (§4) — but design every store interface to take a partition key later
without churning provider logic.

**Confirmed decisions:**
- **Storage:** in-memory by default (zero external deps). **Postgres + pgvector**
  (optional) as a single durable store for both facts and vectors. **Redis** (optional)
  as a cache-aside read/recall cache only — never source of truth.
- **Model serving:** local **vLLM** serving Qwen on the RTX 5090, exposed as an
  OpenAI-compatible endpoint and consumed as an `IChatClient`. Started by `start.sh`.
- **Embeddings:** in-process **CPU/ONNX** embedding generator by default, so in-memory
  mode and unit tests need no GPU. vLLM only serves chat.
- **Testing:** **TDD** for all provider/store logic (unit tests, fakes, in-memory
  stores, no containers). **Testcontainers-dotnet** for Postgres/Redis integration tests.

> **API-verification caveat:** the design doc and web research give *illustrative* MAF
> signatures (`AIContextProvider`, `AIContext`, `InvokingContext`/`InvokedContext`,
> `ChatHistoryMemoryProvider`, serialization model). These MUST be verified against the
> actually-installed NuGet packages in Task 1 before building against them. Do not trust
> the researched signatures verbatim.

---

## Target structure

```
maf-memoryos/
├── AgentMemoryOS.sln
├── Directory.Build.props            # shared TFM, nullable, analyzers, langversion
├── .gitignore                       # dotnet + .env + model cache
├── .env.example                     # ports, model name, connection strings, feature flags
├── docker-compose.yml               # postgres(pgvector) + redis + vllm(qwen)
├── scripts/
│   ├── start.sh                     # bring up compose, wait for health, verify model
│   ├── stop.sh
│   └── serve-model.sh               # vLLM invocation / model pull notes (Blackwell flags)
├── src/
│   ├── AgentMemoryOS.Abstractions/    # store + provider-facing interfaces, DTOs
│   ├── AgentMemoryOS/                 # TieredMemoryProvider + in-memory stores + CPU embedder
│   ├── AgentMemoryOS.Postgres/        # pgvector-backed IFactStore + IVectorRecall
│   └── AgentMemoryOS.Redis/           # cache-aside decorators
├── samples/
│   └── AgentMemoryOS.Sample/          # console agent wired to vLLM Qwen + chosen stores
└── tests/
    ├── AgentMemoryOS.Tests/           # unit (TDD), fakes + in-memory, no containers
    └── AgentMemoryOS.IntegrationTests/# Testcontainers: postgres+pgvector, redis
```

---

## Architecture

**Provider thin, store fat** (design §7). `TieredMemoryProvider : AIContextProvider`
holds only injected clients + small hot state; the corpus lives behind store interfaces.

Core interfaces in `AgentMemoryOS.Abstractions` — **every method takes a `MemoryScope`
partition key now** (carrying `templateId`/`instanceId`, defaulted to a `Default` scope
in single-agent mode) so the swarm step later changes wiring, not signatures:

- `IWorkspaceStore` — L1 always-on markdown (MEMORY/USER/CREATIVE). `LoadAsync(scope)`.
- `IFactStore` — L3 trust-scored facts. `RecallAsync(scope, query, minTrust)`,
  `AppendObservationAsync(scope, observation)` (append-only hot path; mutation/trust
  aggregation deferred to the future compactor).
- `IVectorRecall` — L5 semantic recall. `RecallAsync(scope, queryEmbedding, minScore)`,
  `IndexAsync(scope, item)`.
- `IEmbeddingGenerator<string, Embedding<float>>` — reuse the
  `Microsoft.Extensions.AI` interface; default impl = CPU/ONNX.

`TieredMemoryProvider`:
- `InvokingAsync` → load workspace (L1), embed last user turn, recall facts (gated by
  trust) + vector hits (gated by score), **`Gate(Dedup(...))`** merge with per-session
  dedup, return `AIContext { Instructions = workspace, Messages = merged }`.
- `InvokedAsync` → `IsTrivial` filter (social-closer skip); else extract observations
  via injected `IChatClient` extractor; `AppendObservationAsync` each (append, no mutate).
- Serialization: persist **hot state only** (per-session seen-hashes for dedup, scope
  key) — never the corpus. Implement whatever the verified SDK serialization contract is.

The token-efficiency trio (`Gate`, `Dedup`, `IsTrivial`) are pure functions — primary
TDD targets, testable with no I/O.

**Optionality wiring** (DI in the sample, config-driven via `.env`):
- `MEMORY_STORE=inmemory|postgres` selects `IFactStore`/`IVectorRecall` impl.
- `REDIS_ENABLED=true` wraps the chosen stores in cache-aside decorators
  (`CachingFactStore`, `CachingVectorRecall`) from `AgentMemoryOS.Redis`.
- Stores compose as decorators so Redis caching is independent of the backing store.

---

## Implementation tasks (TDD throughout — invoke `superpowers:test-driven-development`)

**Task 1 — Solution scaffold + verify MAF API.**
Create solution, projects, `Directory.Build.props` (target the installed .NET; confirm
TFM), `.gitignore`. Add real NuGet packages: `Microsoft.Agents.AI`,
`Microsoft.Extensions.AI` (+ `.OpenAI` for the vLLM client), `Microsoft.Extensions.VectorData`.
**Write a throwaway spike** that subclasses `AIContextProvider` and compiles, to capture
the *actual* `InvokingAsync`/`InvokedAsync`/`AIContext`/context-type signatures and the
serialization contract. Record the verified API in a short note and adjust the interfaces
below to match. Gate: solution builds, `dotnet test` runs an empty suite green.

**Task 2 — Abstractions + DTOs.** `AgentMemoryOS.Abstractions`: `MemoryScope`,
`Observation`, `Fact`, `RecallItem`, the four store interfaces above. No logic.

**Task 3 — Token-efficiency core (pure, TDD-first).** In `AgentMemoryOS`: `Gate`, `Dedup`
(cosine-threshold merge, per-session seen-hash), `IsTrivial`. Write failing tests first
(relevance threshold, dedup collapses near-duplicates >0.92, triviality skips social
closers), then implement. Highest-value unit tests; no I/O.

**Task 4 — In-memory stores + CPU embedder.** In-memory `IWorkspaceStore`/`IFactStore`/
`IVectorRecall` (the zero-dependency default). CPU/ONNX `IEmbeddingGenerator` impl (small
sentence-embedding model, e.g. all-MiniLM via ONNX runtime) with a deterministic **fake**
embedder for unit tests. TDD each store.

**Task 5 — `TieredMemoryProvider`.** Wire `InvokingAsync`/`InvokedAsync` against the
verified API using fakes for `IChatClient` extractor + embedder + in-memory stores. Tests:
recall merges L1+L3+L5 and applies gating/dedup; `InvokedAsync` appends extracted
observations and honors the triviality filter; serialization round-trips hot state only.
Reuse `Microsoft.Extensions.AI` test patterns; do not test mock behavior
(`testing-anti-patterns`).

**Task 6 — Postgres + pgvector.** `AgentMemoryOS.Postgres`: `Npgsql`-backed `IFactStore` +
pgvector `IVectorRecall`, schema migration (facts table, vector column + index), every
query filtered by `MemoryScope`. **Integration tests via Testcontainers** (pgvector image).

**Task 7 — Redis cache-aside.** `AgentMemoryOS.Redis`: `StackExchange.Redis` decorators
caching hot recall results + L1 workspace (cache-aside, TTL, invalidate on append).
Testcontainers Redis integration tests; verify cache hit/miss + invalidation, and that
disabling Redis is a no-op pass-through.

**Task 8 — Infra: docker-compose + scripts.**
`docker-compose.yml`: `pgvector/pgvector` (Postgres), `redis`, and a `vllm` service for
Qwen (OpenAI-compatible). `.env.example` for ports/model/connection strings/flags.
`scripts/start.sh`: `docker compose up -d`, poll health (pg_isready, redis PING, vLLM
`/v1/models`), print endpoints. `scripts/serve-model.sh`: vLLM invocation with the
Blackwell/sm_120 caveats noted (CUDA 12.8+ image, `VLLM_FLASH_ATTN_VERSION=2`,
`--enable-auto-tool-choice --tool-call-parser`, quantized Qwen sized for 32 GB). Treat the
exact vLLM image/tag and Qwen model id as configurable in `.env` and **verify the image
actually starts on the 5090** — pre-built vLLM images may need a source build on Blackwell;
fall back to that if the tagged image fails.

**Task 9 — Sample console agent.** `AgentMemoryOS.Sample`: build a MAF agent with the
`OpenAI` `IChatClient` pointed at vLLM (`http://localhost:8000/v1`), attach
`TieredMemoryProvider`, DI-select stores from `.env`. Demonstrate recall persisting across
turns/sessions. This is the end-to-end manual verification harness.

---

## Verification

- **Unit (no deps):** `dotnet test tests/AgentMemoryOS.Tests` — gating/dedup/triviality,
  provider lifecycle, in-memory stores all green. Run continuously during TDD.
- **Integration:** `dotnet test tests/AgentMemoryOS.IntegrationTests` — Testcontainers spins
  up pgvector + redis; fact persistence, scoped queries, vector recall, cache hit/miss +
  invalidation pass without any manually-running stack.
- **Infra smoke:** `./scripts/start.sh` brings up postgres + redis + vLLM; health checks
  pass; `curl http://localhost:8000/v1/models` lists the Qwen model.
- **End-to-end:** run `AgentMemoryOS.Sample` against the live stack — confirm a fact stated
  in session 1 is recalled in session 2, with `MEMORY_STORE` toggled `inmemory`→`postgres`
  and `REDIS_ENABLED` on/off, all producing correct recall.
- Final pass via `superpowers:verification-before-completion` before claiming done.

## Out of scope (design §7 "skip early")
Template-keyed shared store + background compactor (§4) — interfaces are scope-ready but
unimplemented; HRR fact encoding; 4-level fallback cascade; self-curating L6 wiki.

---

## Plan Review

**Reviewed:** 2026-06-05 09:52
**Reviewer:** Claude Code (plan-review-intake)

### Strengths

- **Context / Confirmed decisions** clearly narrows scope to the single-agent baseline and defers shared-store/compactor work intentionally.
- **Architecture** has a solid separation-of-concerns story: "provider thin, store fat," abstractions in a separate project, and future-ready `MemoryScope`.
- **Optionality wiring** is well thought out: backing store selection and Redis as decorators keeps composition clean.
- **Verification** is concrete and testable, especially the TDD emphasis and split between unit, integration, infra smoke, and end-to-end checks.
- **Task 1** correctly treats MAF API signatures as untrusted until verified.

### Issues

#### Critical (Must Address Before Implementation)

**1. Architecture / Task 5 / Task 6 — L3 write model is underspecified**
- Section: Architecture / Task 5 / Task 6
- `IFactStore` is described as a trust-scored fact store, but `InvokedAsync` only appends observations and trust aggregation is deferred to a future compactor that is explicitly out of scope.
- This leaves baseline L3 behavior undefined: what exactly does `RecallAsync` return before any compactor exists?
- **Fix:** Define the v1 storage/read model explicitly: either store recalled items as observations with an initial trust policy, or add a synchronous materialization step from observation → fact for single-agent mode.

**2. Architecture / Task 5 — No ingestion path for L5 vector recall**
- Section: Architecture / Task 5
- `IVectorRecall` has `IndexAsync`, but the provider flow never calls it and no other indexing pipeline is defined.
- Without a write/index path, L5 cannot populate, so the baseline's "L1 + L3 + L5" claim is not implementable.
- **Fix:** Specify when embeddings are generated and indexed (e.g. on append, on fact materialization, or via a dedicated async indexer in scope).

#### Important (Should Address)

**1. Task 7 vs. Architecture — workspace contract mismatch**
- `IWorkspaceStore` only has `LoadAsync(scope)`, but Redis caching mentions "invalidate on append" for L1 workspace. There is no workspace write/append API in the plan.
- **Fix:** Either add/update the workspace mutation contract or remove workspace invalidation language and use TTL-only caching.

**2. Task 8 — too large and environment-specific for one task**
- Bundles compose setup, health-check scripts, model-serving config, GPU compatibility validation, and possible source-build fallback for Blackwell. Not a single actionable implementation chunk.
- **Fix:** Split into separate tasks: compose/config, start/stop scripts, model-serving validation/fallback.

**3. Architecture / Optional stores — failure behavior not defined**
- The plan does not say whether memory failures fail-open or fail-closed when Postgres/Redis/embedder/vLLM are unavailable.
- **Fix:** Define degraded-mode behavior per dependency.

**4. Task 6 — migration strategy is incomplete**
- "Schema migration" is named, but no mechanism/versioning/rollback approach is defined.
- **Fix:** Specify how migrations are created/applied/tested and what rollback means for greenfield v1.

**5. Repository conventions check is blocked**
- `/home/smolen/dev/maf-memoryos/CLAUDE.md` does not exist, and the repo currently contains only planning/research docs plus `.gitignore`; none of the planned source/build files exist yet.
- This prevents validating CLAUDE-specific conventions and makes the "esbuild.js" checklist item not applicable.
- **Fix:** Add the conventions file or point review/implementation to the correct path.

#### Minor (Consider)

**1. Task 1 — `.gitignore` already exists**
- Wording says "Create … `.gitignore`," but the repo already has one. Change to "update `.gitignore` as needed."

**2. Task 1 — "record the verified API in a short note" is vague**
- The artifact location is undefined. Name it explicitly.

### Recommendations

- Resolve the **observation/fact/vector lifecycle** first — that is the plan's main architectural gap.
- Split Task 8 into compose/config, scripts, and model-serving validation subtasks.
- Add explicit degraded-mode and migration policies before implementation begins.
- Restore or create `CLAUDE.md` so conventions can govern the implementation work.

### Assessment

**Implementable as written?** With fixes

**Reasoning:** The overall architecture is strong, but the core persistence pipeline is incomplete — the plan does not define how append-only observations become trust-scored facts or vector-indexed recall in the baseline. Those gaps would force the implementer to invent behavior mid-build.
