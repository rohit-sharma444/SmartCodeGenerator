# SLM Intelligence Layer — Design

Date: 2026-08-06
Branch: `SLM`
Status: approved for planning

## Goal

Add a small-language-model intelligence layer to the CodeGenerator platform without
modifying or breaking the rule-based execution path. The tokenizer, command parser,
semantic analyzer, validation engine, template engine, Roslyn generator and compiler
pipeline remain the primary path. The AI layer is a set of independent modules —
dataset creation, tokenization, embedding generation, vector storage, prompt
construction, inference, model management — reached through explicit interfaces and
absent from the assembly graph of the existing engine.

### First vertical

Intent understanding for **unrecognized input**. When `CommandFront.Parse` yields
`IntentType.Unknown`, the AI layer retrieves the nearest known commands, proposes an
intent, and asks the user to confirm before anything executes.

Chosen over `build app:` domain modeling and code suggestion because it has the
smallest corpus, the clearest success metric, and it touches the one place users hit
friction daily.

### Decisions taken

| Decision | Choice |
|---|---|
| Inference runtime | ONNX Runtime, in-process |
| First slice | Understand unrecognized input |
| Result UX | Always confirm before executing |
| Model artifact | Committed to the repo (via Git LFS) |
| Scope | Phases A, B and C — retrieval, generative, hybrid |

The model-in-repo choice was made with the cost stated: it puts binaries in git
history permanently. It buys one real benefit — CI always has the model, so AI tests
are unconditional rather than skipped.

## Architecture

Four new projects. One hard rule drives the split: **exactly one project may
reference `Microsoft.ML.OnnxRuntime`.** That makes "operates normally when AI is
disabled" a property of the assembly graph, not a runtime flag someone can forget to
check.

| Project | Contains | Depends on |
|---|---|---|
| `CodeIntelligence.Abstractions` | Interfaces and records only | nothing |
| `CodeIntelligence` | Corpus, storage, retrieval, prompting, resolvers, composition root | Abstractions, `CodeIntelligence.Onnx`, `NaturalLanguageBridge`, `Microsoft.Data.Sqlite`, `CodeGenerationEngine` |
| `CodeIntelligence.Onnx` | Tokenizer, embedding provider, generative provider, model management | Abstractions, `Microsoft.ML.OnnxRuntime`, `Microsoft.ML.Tokenizers` |
| `CodeIntelligence.Tests` | Tests (xunit 2.9.2) | all of the above |

**Amended when Task 7 landed.** This table first gave `CodeIntelligence` no dependency
on `CodeIntelligence.Onnx`, while also placing the composition root at
`CodeIntelligence/AiLayer.cs`. Both cannot hold: composing an ONNX provider means
naming its type. Resolved in favour of the composition root — `CodeIntelligence`
references the Onnx **project**, and `ArchitectureTests` continues to forbid it a direct
`Microsoft.ML.OnnxRuntime` **package** reference. (`NaturalLanguageBridge` also arrived
in this row at Task 3, where the corpus harvester began resolving catalog commands
through the real parser rather than duplicating its rules.)

`NaturalLanguageBridge` gains a reference to `CodeIntelligence.Abstractions` **only**.
It never learns ONNX exists. This is the property that actually matters, and it is
unaffected by the amendment above — the arrow runs `CodeIntelligence` →
`NaturalLanguageBridge`, never back. Verified at Task 7 by build output, not by
inspection: `NaturalLanguageBridge/bin/Release` contains zero `*OnnxRuntime*` assemblies.

`CodeIntelligence` sits downstream of `CodeGenerationEngine` because it needs
`IntentType` and `ParsedCommand`. The dependency arrow is one-way: the engine never
references the AI layer.

All four projects are added to `SmartCodeGenerator.slnx` so CI's Release
`-warnaserror` build covers them.

### Folder layout inside `CodeIntelligence`

Folders, not projects — these boundaries are conceptual, not dependency boundaries.

- `Corpus/` — harvesters producing `CorpusDocument`
- `Storage/` — SQLite vector store and schema
- `Retrieval/` — cosine ranking, k-NN voting
- `Prompting/` — context assembly (phase B onward)
- `Resolution/` — the three resolvers
- `AiLayer.cs` — the single composition point

### Abstractions

```csharp
public sealed record RetrievedExample(long DocumentId, string Text, IntentType Intent, double Score);

public sealed record AiResolution(
    IntentType Intent,
    double Confidence,
    IReadOnlyList<RetrievedExample> Evidence,
    string? Rationale);

public interface IIntentResolver { AiResolution? Resolve(string input, CancellationToken ct); }
public interface IEmbeddingProvider { float[] Embed(string text); string ModelId { get; } int Dimension { get; } }
public interface IGenerativeProvider { string Complete(string prompt, CancellationToken ct); string ModelId { get; } }
public interface ITokenizer { IReadOnlyList<int> Encode(string text); }
public interface IVectorStore { /* upsert, query, meta */ }
```

Three resolvers implement `IIntentResolver`, which is what makes A/B/C a phase
sequence rather than three competing designs:

- `RetrievalIntentResolver` (A) — embeddings plus k-NN vote
- `GenerativeIntentResolver` (B) — prompt plus small instruct model
- `HybridIntentResolver` (C) — A for intent, B for fields; the eventual default

## The seam

`CommandFront.Parse` stays pure. No AI in it. Every existing test of `Parse` is
untouched.

The hook goes in `RunDetailed`, because confirmation must happen between resolution
and execution, and only the CLI owns console I/O.

```csharp
public static RunResult RunDetailed(
    string input, ref ProjectMetaData? project, List<PropertyRule> configuredRules,
    string workspace, Action<ProjectMetaData?> setLastCreatedProject,
    Action<SemanticResult?>? onAnalyzed = null,
    IIntentResolver? aiResolver = null,          // new, optional
    Func<AiResolution, bool>? confirmAi = null,  // new, optional
    CancellationToken ct = default)              // new, optional
```

All three parameters have defaults, so every existing call site compiles and behaves
identically. No signature churn across the existing test suite.

`ct` is the session token the CLI already cancels on `exit`; the resolver combines it
with its own timeout budget internally.

### Runtime order

1. `Parse` → if `Intent != IntentType.Unknown`, execute exactly as today. The
   resolver is never called.
2. If `Unknown` and both delegates are present → `aiResolver.Resolve(input, ct)`.
3. Null result, `Unknown` intent, or confidence below the floor → today's
   "Command not recognized" path, unchanged.
4. Otherwise `confirmAi(resolution)`. On **no**, today's path. On **yes**,
   `CommandEntityExtractor.Extract(input, resolution.Intent, workspace)`, copy the
   confidence, and execute.

### The invariant

**The AI can only change what happens to input that already fails today.** It cannot
alter, slow or degrade any working command — including adding a millisecond of
latency, because step 1 short-circuits before the resolver is reachable.

This is enforced by a test, not by prose: a spy resolver must record zero calls
across the full corpus of recognized commands.

### Who supplies the delegates

The CLI (`CodeGeneratorStarter`) supplies `confirmAi`, rendered through the existing
`SelectionPrompt`. **When stdin is redirected, it passes `null`**, so piped and
scripted E2E runs remain deterministic and model-free.

## Data flow

### Indexing — offline, via `ai reindex`

```
harvesters → CorpusDocument → ITokenizer → IEmbeddingProvider → float[384] → SQLite
```

Phase A corpus sources:

- `IntentRepository` — 53 intents with their patterns and examples. This revives a
  473-line, JSON-backed catalog that currently drives none of the recognition path.
- `LiteralCommandTable` entries
- the `CommandFront` regex guards and their `GuardedIntents` list
- `docs/commands.md`
- the help catalog

### Schema

```sql
CREATE TABLE corpus_document(
  id INTEGER PRIMARY KEY, text TEXT NOT NULL, intent TEXT,
  source_kind TEXT NOT NULL, source_ref TEXT, content_hash TEXT NOT NULL);

CREATE TABLE embedding(
  document_id INTEGER PRIMARY KEY REFERENCES corpus_document(id),
  model_id TEXT NOT NULL, dim INTEGER NOT NULL, vector BLOB NOT NULL);

CREATE TABLE index_meta(key TEXT PRIMARY KEY, value TEXT NOT NULL);

CREATE TABLE ai_decision(
  id INTEGER PRIMARY KEY, created_utc TEXT NOT NULL, input TEXT NOT NULL,
  intent TEXT NOT NULL, confidence REAL NOT NULL,
  evidence_ids TEXT NOT NULL, accepted INTEGER);
```

### Retrieval

Brute-force cosine over a few thousand vectors — microseconds. No ANN index. Building
HNSW at this corpus size would be ceremony.

Top-k (default k=5), weighted vote over the neighbours' intents, confidence from the
vote margin.

**Two independent gates, not one.** `MinTopScore` asks "is anything actually
similar?"; `ConfidenceFloor` asks "do the neighbours agree?". Either alone is
insufficient — share-of-vote by itself gives a lone weak match (cosine 0.02, noise)
100% of the vote and thus maximum confidence. Both must pass or `Decide` returns
`null` rather than guess. They are deliberately not multiplied together: collapsing
them loses the ability to tell a weak-but-unanimous match from a strong-but-contested
one, and Task 10 calibrates each independently.

### Traceability

Every resolution appends a row to `ai_decision`, including the user's accept/decline.
That single table is both the audit trail and the calibration data phase C needs to
set an auto-execute threshold from measurement instead of guesswork.

## Models and model management

### Phase A — embedding

`all-MiniLM-L6-v2`, 384-dimensional, **int8-quantized ONNX (~23MB)**. Quantization
cuts the git burden 4× versus fp32 at negligible cost to cosine ranking over short
command phrases.

### Tokenizer

`Microsoft.ML.Tokenizers`' `BertTokenizer`, behind our own `ITokenizer` interface. The
module boundary exists and stays swappable, but we do not own WordPiece's fiddly edge
cases — unknown-token fallback, `max_input_chars_per_word`, accent stripping — where a
subtle bug degrades retrieval silently.

### Phase B — generative

`Qwen2.5-0.5B-Instruct`, int4, **~350MB**. Recorded explicitly because the
model-in-repo decision was taken against a stated ~90MB figure that covered embeddings
only. Git LFS handles it. Phase A alone stays at ~23MB, so this is revisitable when
phase B starts.

### Registry and drift

A registry records each model's id, dimension and SHA-256. The `embedding` table
stores `model_id` per row because **vectors from different models are not
comparable**. Swapping a model invalidates the index; `ai status` reports the drift
and retrieval refuses to run until `ai reindex` completes.

### Loading

The ONNX session is created lazily on first use, and only for the configured mode —
selecting retrieval mode never loads the generative model. `InferenceSession.Run` is
thread-safe, so one session is shared and disposed at shutdown.

## Configuration

`ai.json` alongside the existing `intents.json` in `AppDomain.CurrentDomain.BaseDirectory`,
matching `IntentRepository`'s established pattern.

| Key | Default |
|---|---|
| `Enabled` | `false` |
| `ResolverMode` | `Retrieval` |
| `ConfidenceFloor` | `0.55` provisional, replaced by the first golden-set measurement. Must stay **above 0.5**: a two-way ambiguous phrase computes to exactly 0.5 and the comparison is inclusive |
| `MinTopScore` | `0.35` provisional, calibrated alongside the floor |
| `TopK` | `5` |
| `EmbeddingModelId` | `all-MiniLM-L6-v2-int8` |
| `GenerativeModelId` | unset until phase B |
| `RetrievalTimeoutMs` | `500` |
| `GenerationTimeoutMs` | phase B |

Default-off for phase A. The flag flips once the decision log shows real accuracy.

## Commands

**Amended when Task 9 landed.** This section originally called for additive
`IntentType` members, `LiteralCommandTable` entries and `CommandHandlingService`
handlers. None were added. `ai ...` is handled in the CLI beside `clear`, `history`,
`build app` and `batch` — all of which are `IsX(input)` → handle → `continue` with no
enum member and no table entry — because these are console reports, not
code-generation actions, and routing them through `CommandHandlingService` would push
console formatting into the engine.

The reachable-intent gate reads `LiteralCommandTable`, `CommandFront.GuardedIntents`
and `NlpCommandMapper.MappedIntents`. Since no `ai` intent would appear in any of
them, enum members would have been unreachable dead code rather than something the
gate protects — and this codebase rejects unreachable intents on principle. Not
touching `IntentType` also avoids all risk to the learned-intent store, which
persists those members as integers.

| Command | Behavior |
|---|---|
| `ai status` | enabled, mode, model ids and SHAs, index freshness, doc count, decision count, and any initialization failure reason |
| `ai enable` / `ai disable` | toggle `Enabled` in `ai.json` |
| `ai reindex` | rebuild corpus and embeddings |
| `ai mode <retrieval\|generative\|hybrid>` | set `ResolverMode` |
| `ai why` | explain the last resolution from `ai_decision`, echoing the existing `why [name]` command |

## Error handling and degradation

**Every failure degrades to today's behavior, never to an exception.** One catch-all
boundary lives in `AiLayer`; `IIntentResolver` implementations never throw into
`CommandFront`. A resolver that fails returns `null`, and `null` means "print the
message you would have printed anyway."

| Failure | Behavior |
|---|---|
| Model file missing, or SHA mismatch | AI never initializes. Reported once at startup and by `ai status`. Recognition path unchanged. |
| ONNX native load failure (arch mismatch, missing VC++ runtime) | Caught at provider construction. AI marked unavailable with the reason in `ai status`. |
| Index missing, or `model_id` drift | Retrieval refuses rather than returning garbage neighbours. `ai status` reports "reindex required". |
| Inference throws or exceeds its time budget | Cancelled, logged, `null` returned. |
| Confidence below floor | `null`. Expected common case, not an error — the floor is what stops confident nonsense. |
| `exit` during a resolve | Cancelled via the token, matching how `exit` already cancels the background dump apply. |

Two requirements called out because this repository has been bitten before:

**`ai reindex` builds to a temporary database and atomically swaps on success.** A
killed index rebuild must leave the previous index intact and serving. Non-negotiable.

**Failure reporting is once per session, not per command.** A missing model must not
warn on every unrecognized input. It goes in the startup banner alongside the existing
updater-service line and stays queryable through `ai status`.

## Testing

`CodeIntelligence.Tests` uses **xunit 2.9.2**, matching the sibling library test
projects. MSTest is used only by `CodeGenerator.Tests`.

1. **The invariant** — a spy `IIntentResolver` records zero calls across the full
   corpus of recognized commands. This is the test that proves nothing existing broke.
2. **Golden-set accuracy** — roughly 80 held-out `(phrasing → expected intent)` pairs
   that are not in the corpus. Asserts top-1 accuracy and, separately and more
   importantly, the **wrong-but-above-floor rate**: a confident wrong answer is far
   worse than a decline.
3. **Retrieval math** — a fake `IEmbeddingProvider` returning canned vectors exercises
   cosine ranking, k-NN voting and floor enforcement without ONNX.
4. **Storage** — schema, model-drift refusal, and reindex atomicity: kill mid-build,
   assert the prior index still serves.
5. **Degradation** — missing model, bad SHA, throwing provider, timeout. Each asserts
   output is byte-identical to today's unrecognized message.
6. **CLI** — confirm-yes executes, confirm-no does not, redirected stdin passes a null
   resolver.

### On thresholds

No accuracy number is invented here and presented as a guarantee. The first
measurement sets `ConfidenceFloor` and the accuracy assertion; the test then locks
that floor in so regressions are caught. Committing the model is what makes this
deterministic enough to assert at all.

### Red first

Every test is written red and shown failing before its implementation lands. This
codebase has shipped inverted `Assert.DoesNotContain` arguments and assertions on
fixture-declared names, both of which passed green.

## Phasing

- **Phase A** — Abstractions, storage, corpus harvesters, embedding provider,
  `RetrievalIntentResolver`, `ai` commands. Ships default-off.
- **Phase B** — generative provider, prompt construction, `GenerativeIntentResolver`
  as a selectable mode.
- **Phase C** — `HybridIntentResolver` becomes the default: retrieval for intent,
  generation for fields. Auto-execute threshold calibrated from the accumulated
  `ai_decision` log rather than guessed.

The implementation plan that follows this spec covers **phase A only**. Phases B and C
get their own plans, written once phase A's decision log exists — B's design depends
on what phase A's accuracy and decline data actually show.

## Out of scope

Each of these needs its own spec. Phase A–C is the intent-understanding vertical
only, built so these extend it rather than replace it.

- Corpus expansion to Roslyn syntax trees, EF models, SQL scripts, validation rules,
  design patterns, project documentation and unit tests
- The `build app:` domain-modeling slice, which targets the NLE lexicon ceiling
- Context-aware code suggestion
- Auto-download of model artifacts in place of committed files

## Known limitations

Phase A improves **intent** recognition while field extraction stays exactly as good
as `CommandEntityExtractor` is today. For input like "make the Customer's email not
blank", phase A routes confidently to `AddValidation` and then extracts fields with
the existing code. If field extraction proves to be the real weakness, phase B should
be reordered to target fields first.
