# AgentMemoryOS — repository guide

Tiered agent-memory provider for **Microsoft Agent Framework (MAF)**, .NET 10 / C#. Ports
the `memory-os` 6-layer pattern onto MAF's `AIContextProvider` lifecycle. See
[planning/maf-tiered-memory-design.md](planning/maf-tiered-memory-design.md) for the design
and the approved implementation plan.

## Solution layout

- `src/AgentMemoryOS.Abstractions` — `MemoryScope`, DTOs, store/sink interfaces. No logic.
- `src/AgentMemoryOS` — `TieredMemoryProvider`, in-memory stores, default embedder, the
  observation sink + background reconciler.
- `src/AgentMemoryOS.Postgres` — pgvector-backed fact + vector store (optional).
- `src/AgentMemoryOS.Redis` — cache-aside decorators (optional).
- `src/AgentMemoryOS.All` — metapackage bundling core + Postgres + Redis.
- `tests/AgentMemoryOS.Tests` — unit tests (no containers).
- `tests/AgentMemoryOS.Postgres.IntegrationTests`, `tests/AgentMemoryOS.Redis.IntegrationTests` — Testcontainers.
- `tests/AgentMemoryOS.Example.WebHost` — config-driven dogfood app and runnable example.
- `tests/AgentMemoryOS.Example.IntegrationTests` — boots the WebHost via WebApplicationFactory.

## Build & test

```bash
dotnet build      # warnings are errors; must be clean
dotnet test       # MSTest
./scripts/start.sh          # postgres + redis only (default dev path)
./scripts/start.sh --gpu    # also starts the vLLM Qwen model server (RTX 5090)
```

## Quality gates (non-negotiable)

Write all C# via the `csharp-quality-developer` workflow. The build enforces
`TreatWarningsAsErrors`, `AnalysisMode=all`, `EnforceCodeStyleInBuild`,
`GenerateDocumentationFile`, and full StyleCop (config in `src/.editorconfig`,
`tests/.editorconfig`, `stylecop.json`).

- `.cs` files: CRLF line endings, UTF-8 **with BOM**, 4-space indent, single final newline,
  no trailing whitespace.
- `this.` qualification on members; private fields camelCase (no underscore); `sealed` where
  applicable; file-scoped namespaces; usings outside namespace, System first.
- Every public type/member in `src/` needs XML documentation. Docs are relaxed in `tests/`.
- No file headers (SA1633 / IDE0073 are disabled). `Migrations/*.cs` are analyzer-exempt.

## Dependency management

- **Centralized Package Management**: all versions live in `Directory.Packages.props`.
  `<PackageReference>` entries in `.csproj` carry **no** `Version`. StyleCop is applied
  globally via `<GlobalPackageReference>`.
- Test projects use the **MSTest SDK** pinned in `global.json` (`<Project Sdk="MSTest.Sdk">`).
- `src/` and `tests/` each have their own `Directory.Build.props` (TFM, analyzers, nullable);
  do not duplicate those settings in individual `.csproj` files.

## Reference source

Third-party source (MAF, Microsoft.Extensions.*) is cloned under the git-ignored `research/`
folder — read it to confirm real APIs rather than guessing. Verified MAF notes live in
`research/MAF-API-NOTES.md`. Do not decompile NuGet packages when a public repo exists.

## Architecture in one paragraph

`TieredMemoryProvider : AIContextProvider` overrides `ProvideAIContextAsync` (recall: L1
workspace + gated/deduped L3 facts + L5 vector hits) and `StoreAIContextAsync` (capture:
triviality filter → extract observations → publish to `IObservationSink`). A background
reconciler drains the sink, embeds + indexes (L5) and upsert-merges observations into
trust-scored facts (L3) — the in-process precursor to the swarm compactor. Stores are keyed
by `MemoryScope` so template-partitioned shared memory drops in later without changing
provider logic.
