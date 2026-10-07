# CodeIntelligence

The optional **AI layer** of the CLI. When the rule-based recognizers cannot read a line the user
typed, this project finds the closest known phrasings by meaning and offers the intent they share.
It never acts alone: the CLI asks the user to confirm first, and a piped run never uses it.

This is a class library (`net10.0`) with no console I/O. It references
[`CodeIntelligence.Abstractions`](../CodeIntelligence.Abstractions/README.md),
[`CodeIntelligence.Onnx`](../CodeIntelligence.Onnx/README.md),
[`CodeGenerationEngine`](../CodeGenerationEngine/README.md) and
[`NaturalLanguageBridge`](../NaturalLanguageBridge/README.md), and stores its index with
`Microsoft.Data.Sqlite`.

## How it works

```
typed line --> embed (ONNX) --> nearest corpus phrasings (cosine) --> NeighbourVote --> AiResolution
                                        ^                                  |
                       SqliteVectorStore (ai-index.db)          two gates: MinTopScore, ConfidenceFloor
```

A line is turned into a vector, compared with the vectors of the corpus, and the top `TopK`
neighbours vote for an intent. The answer is declined unless the best score reaches `MinTopScore`
**and** the neighbours agree to at least `ConfidenceFloor`.

## Main types

| Type | Role |
| --- | --- |
| `AiLayer` | Composition root and the one catch-all boundary. `AiLayer.Create()` never throws: if the model is missing or the runtime will not load, `Resolver` stays `null` and `Unavailable` says why. Also `Disable()`, `Reindex()` and `CreatePhraseResolver(...)`. |
| `AiConfig` | `ai.json` beside the binaries: `Enabled` (default on), `ResolverMode`, `ConfidenceFloor`, `MinTopScore`, `TopK`, `EmbeddingModelId`, `RetrievalTimeoutMs`. A missing file gives the defaults; an unreadable one turns the layer off. |
| `RetrievalIntentResolver` | The `IIntentResolver`. Embeds, retrieves, votes, and never throws (it runs inside the REPL loop). |
| `PhraseResolver` | The same method over a caller's own closed list of phrases and labels, with the caller's thresholds. |
| `NeighbourVote`, `PhraseVote`, `Similarity` | The vote and the cosine maths. |
| `SqliteVectorStore`, `VectorSchema` | The `IVectorStore` over `ai-index.db` (tables `corpus_document`, `embedding`, `index_meta`, `ai_decision`). |
| `DecisionLog` | Records each AI reading and what the user answered. |
| `CorpusHarvester` | Builds the corpus from intent rules, `LiteralCommandTable`, the command catalog and the documentation. |
| `LearnedPhrases` | Reads `Corpus/learned-phrases.tsv` (embedded resource): phrasings users typed that the CLI missed, each with the intent it should have had. |

## How the solution uses it

- `CodeGenerator` (`CodeGeneratorStarter`, `ChatSession`, `ChatCommand`, `AiCommand`) holds one
  `AiLayer`. The `ai` command shows status, enables, disables and reindexes.
- `BuildAppCommand` asks `AiLayer.CreatePhraseResolver` for a resolver over a closed phrase list.
- `NaturalLanguageBridge` treats a `null` resolver as "no AI", so every failure degrades to the
  behaviour without this layer.

## Files

- `ai.json` and `ai-index.db` are written beside the executable.
- The index is rebuilt on start when its content hash differs from the shipped corpus, so an
  unchanged corpus costs one harvest and one query.
- To add a learned phrase, add a line `<phrase><TAB><IntentType>` to `Corpus/learned-phrases.tsv`.
  `show learning` writes candidates to `.codegen\phrase-candidates.jsonl` in the workspace; review
  them first.

## Tests

[`CodeIntelligence.Tests`](../CodeIntelligence.Tests) (xUnit), including a golden set that measures
the shipped thresholds.
