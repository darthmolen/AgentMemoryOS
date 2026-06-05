# Tiered Agent Memory on Microsoft Agent Framework

**Source pattern:** `memory-os` (6-layer memory OS for Hermes Agent)
**Target:** Microsoft Agent Framework (MAF), .NET/C#
**Scope:** single-agent baseline → template-keyed shared swarm memory

---

## 1. Problem

memory-os solves agent amnesia with a tiered store (always-injected workspace →
searchable history/facts → vector recall → curated wiki) wrapped in two lifecycle
hooks: `pre_llm_call` recalls context, `post_llm_call` / `on_session_end` extracts
and persists learnings. It is built for a single Hermes user.

We want the same capability in MAF, with one structural change: memory is **keyed on
the swarm template**, and **all running instances of that template share one memory
namespace**. Instance-private task state stays local; reusable domain knowledge
(repo conventions, team gating rules, recurring false positives) accrues to the
template and benefits every future instance.

The non-obvious result: the adaptation is mostly *recognition*, not porting. MAF's
context-provider lifecycle is already the hook model memory-os bolts onto Hermes.
The real engineering is concentrated in one place — concurrent writes to shared
template memory — and it resolves with the same fast-append / slow-reconcile split
already used in the saga work.

---

## 2. Mechanism

### 2.1 The MAF primitive

`AIContextProvider` runs a two-phase lifecycle per invocation:

- **`InvokingAsync(InvokingContext, ct)` → `AIContext`** — called *before* the model
  runs. Returns instructions, tools, and/or messages to merge into the request. This
  is `pre_llm_call` recall.
- **`InvokedAsync(InvokedContext, ct)`** — called *after* the response. Inspects
  request/response messages and updates state. This is `post_llm_call` extraction.

State is serialized with the thread via `SerializeAsync(...)` plus a constructor
taking a `JsonElement`. Providers are attached through the agent's
`AIContextProviders = [ ... ]` collection. A built-in `ChatHistoryMemoryProvider`
already does semantic-similarity retrieval and, in **`OnDemandFunctionCalling`** mode,
exposes recall as a *tool the model calls* rather than injecting every turn — the
direct lever for context bloat.

> Confirm exact `AIContext` / `InvokingContext` / `InvokedContext` member names
> against the installed SDK; signatures below are illustrative.

### 2.2 Layer → MAF mapping

| memory-os layer | MAF home | Inject mode |
|---|---|---|
| L1 Workspace (MEMORY/USER/CREATIVE) | `InvokingAsync` returns `AIContext` w/ instructions+messages | Always-on |
| L2 Sessions (FTS history) | `ChatHistoryProvider` persistence + FTS index | On-demand |
| L3 Structured facts + trust | Custom `AIContextProvider`; extract in `InvokedAsync` via injected `IChatClient` | Gated |
| L4 Fabric (multi-source) | Aggregator provider **or** stacked providers in `AIContextProviders` | Gated |
| L5 Vector (Qdrant) | `ChatHistoryMemoryProvider` or `Microsoft.Extensions.VectorData` + custom provider | On-demand |
| L6 LLM wiki | Background hosted service / timer Function (not request-path) | N/A |

**Aggregator vs. stacked providers.** Stacking is simpler and each provider gates
independently, but you lose *cross-source* dedup (the cosine >0.92 merge across
facts + sessions + vector). A single aggregator provider regains cross-source dedup,
per-session dedup, and the triviality filter in one place at the cost of being the
component that knows about all stores. **Recommendation: aggregator**, because the
token-efficiency story (gated retrieval + dedup + triviality skip) is exactly the
cross-cutting logic stacking can't express.

---

## 3. Single-agent baseline

The provider is the durable component; stores sit behind it. Keep heavy state
(SQLite, vector) *out* of thread serialization — persist only handles + small hot
state.

```csharp
public sealed class TieredMemoryProvider : AIContextProvider
{
    private readonly IChatClient _extractor;          // fact extraction
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embed;
    private readonly IFactStore _facts;               // durable, trust-scored
    private readonly IVectorRecall _vector;           // semantic
    private readonly IWorkspace _workspace;           // always-on markdown

    public TieredMemoryProvider(IChatClient extractor, /* stores */,
        JsonElement serializedState, JsonSerializerOptions? opts = null)
    { /* rehydrate small hot state from serializedState */ }

    // pre_llm_call: surgical, gated, deduped recall
    public override async ValueTask<AIContext> InvokingAsync(
        InvokingContext context, CancellationToken ct = default)
    {
        var ws      = await _workspace.LoadAsync(ct);                 // L1 always-on
        var query   = LastUserText(context);
        var facts   = await _facts.RecallAsync(query, minTrust: 0.6, ct);
        var hits    = await _vector.RecallAsync(query, minScore: 0.78, ct);

        var merged  = Gate(Dedup(facts, hits));                      // relevance + dedup
        return new AIContext { /* instructions: ws; messages: merged */ };
    }

    // post_llm_call: extract + persist
    public override async ValueTask InvokedAsync(
        InvokedContext context, CancellationToken ct = default)
    {
        if (IsTrivial(context)) return;                              // social-closer filter
        var observations = await ExtractAsync(_extractor, context, ct);
        foreach (var o in observations)
            await _facts.AppendObservationAsync(o, ct);              // append, not mutate
    }

    public override JsonElement Serialize(JsonSerializerOptions opts) => /* hot state only */;
}
```

This alone reproduces L1/L3/L5 and the gating discipline. L2 is the
`ChatHistoryProvider`; L6 is a separate background job.

---

## 4. Swarm: template-keyed shared memory

### 4.1 The keying model

- **Partition key = `SwarmTemplateId`.** Every store read filters on it; every write
  tags it. Instances of `PRReviewSwarm` share one namespace; `DeploymentSwarm` has a
  fully separate one.
- The `AIContextProvider` instance is per-thread and ephemeral. **Durable state lives
  in the shared, template-partitioned store** — the provider is a thin client seeded
  with the template key. Make the key a required constructor argument so a provider
  *cannot* be built unkeyed.

```csharp
public TieredMemoryProvider(SwarmTemplateId templateId, /* stores, clients */) { ... }
// all store calls: _facts.RecallAsync(templateId, query, ...) / AppendObservationAsync(templateId, o)
```

### 4.2 The write-concurrency problem (the real work)

Many instances of one template run at once; their `InvokedAsync` hooks all write the
same namespace. Three hazards:

1. **Duplicate facts** — two instances learn the same thing.
2. **Trust contention** — concurrent adjustments to one fact's score; last-write-wins
   corrupts it.
3. **Dedup races** — semantic merge is itself a read-modify-write.

**Resolution — same split as the saga / Beads work: separate the fast append path
from the slow reconcile path.**

- **Hot path (per invocation, contention-free):** `InvokedAsync` only *appends*
  immutable observations tagged `(templateId, instanceId, ts, embedding)`. Appends
  never conflict. No locks, no saga coordination on the request path.
- **Cold path (background compactor, single-writer per key):** merges semantic
  duplicates (cosine >0.92), recomputes **trust as an aggregation over observations**
  (count / agreement / recency-decay) rather than a contended mutable counter, runs
  decay + archival, curates the wiki. Partition the compactor by `templateId` so each
  namespace has one writer — races vanish.

This makes trust scoring a *derived* quantity, eliminates the counter race entirely,
and keeps memory writes off the saga-coordinated path (they are eventually consistent,
which is correct for memory).

### 4.3 Hot vs. cold path summary

| Path | Trigger | Operation | Consistency |
|---|---|---|---|
| Recall | `InvokingAsync` | read, filter by `templateId`, gate, per-session dedup | read-your-writes not required |
| Capture | `InvokedAsync` | append observation (no mutation) | immediate, conflict-free |
| Compact | timer / queue, per-key | dedup, trust aggregation, decay, wiki | eventual, single-writer |

---

## 5. Decision rules

- **Share vs. keep private.** Shareable → reusable domain knowledge (repo
  conventions, team gating rules, resolved patterns). Private → transient run state
  for one instance. Never write ephemeral task state into shared template memory.
- **Auto-inject vs. on-demand tool.** Small, high-value, broadly-relevant → always-on
  (L1). Large corpora (vector, wiki) → `OnDemandFunctionCalling` to bound context.
- **Append vs. mutate.** Hot path appends only. Any mutation (trust, dedup, merge) is
  deferred to the single-writer compactor.
- **Write authority.** Decide per-role whether an agent may write shared memory; gate
  with the existing per-agent OAuth scopes. Read-broad, write-narrow is a safe default.

---

## 6. Worked example — `PRReviewSwarm`

Template `PRReviewSwarm`; one instance per PR. Shared facts accrue at the template
level: "repo X gates on React Doctor score <50", "team Y treats rule Z as
comment-only", "pattern P is a recurring false positive here." Instance A reviewing
PR #100 appends the convention; the compactor merges and scores it; instance B
reviewing PR #105 of the same repo recalls it on its next `InvokingAsync`. A
`DeploymentSwarm` instance sees none of this — separate namespace.

Concurrent reviews of two PRs both append observations about the same repo
convention; neither blocks the other; the compactor later merges the duplicates and
raises trust via agreement count.

---

## 7. Heuristics (carry-over)

- Provider thin, store fat — never serialize the corpus into the thread.
- Append on the hot path, reconcile on the cold path.
- Make `templateId` impossible to omit (required ctor arg; every store signature takes it).
- Trust = aggregation over observations, never a contended counter.
- Port the token-efficiency trio verbatim: relevance gating, per-session dedup,
  triviality filter.
- Build single-agent first; swapping the backing store for the partitioned shared one
  leaves provider logic nearly unchanged.

### What to skip early

- HRR fact encoding — exotic; FTS + vector suffices.
- 4-level fallback cascade (hybrid → dense → lexical → SQLite) — good resilience,
  premature for v1.
- Self-curating wiki (L6) — most machinery, least early payoff; add after L1/L3/L5 prove out.

---

## 8. Open questions

- **Tenant isolation.** Is `templateId` the top partition, or does org/tenant sit
  above it for multi-tenant enterprise use?
- **Forgetting / deletion.** Right-to-delete across a shared namespace — purge by
  source/entity across all observations + vector points + wiki.
- **Cross-template sharing.** Any case for a shared subset across templates? (Default: no.)
- **Compactor placement.** Azure Function on timer vs. queue-triggered vs. hosted
  service in the control plane — and how it claims single-writer ownership per key.

---

## 9. Summary

MAF's `AIContextProvider` *is* the memory-os hook model, so the six layers map onto
native constructs with little friction. Template-keyed sharing means a thin,
key-seeded provider over a `templateId`-partitioned shared store. The only hard part —
concurrent writes from sibling instances — resolves with the fast-append /
single-writer-compact split already proven in the saga work, which additionally turns
trust scoring into a clean derived aggregation. Build single-agent first; the swarm
version is the same provider over a partitioned store plus a background compactor.
