# CodeIntelligence.Abstractions

The contracts of the AI layer, and nothing else. A class library (`net10.0`) whose only reference
is [`CodeGenerationEngine`](../CodeGenerationEngine/README.md) (for `IntentType`). Its project file
says it must never gain a `PackageReference`, so any project can depend on it cheaply.

## Types

| Type | Role |
| --- | --- |
| `IIntentResolver` | `AiResolution? Resolve(string input, CancellationToken ct)`. |
| `IEmbeddingProvider` | `ModelId`, `Dimension`, `float[] Embed(string text)`. Disposable. |
| `ITokenizer` | `IReadOnlyList<int> Encode(string text)`. |
| `IVectorStore` | `GetStatus`, `LoadAll`, `ReplaceAll` over `CorpusDocument` rows and their vectors. |
| `AiResolution` | The answer: `Intent`, `Confidence`, `Evidence` and a `Rationale`. |
| `RetrievedExample` | One neighbour: document id, text, intent and score. |
| `CorpusDocument` | One phrasing: `Text`, `Intent`, `SourceKind`, `SourceRef`, `ContentHash`. |
| `CorpusSourceKind` | `IntentRule`, `LiteralCommand`, `CommandCatalog`, `Documentation`, `Learned`. |
| `StoredVector`, `IndexStatus` | What a store returns. |

## Who uses it

[`CodeIntelligence`](../CodeIntelligence/README.md) implements the resolver and the store;
[`CodeIntelligence.Onnx`](../CodeIntelligence.Onnx/README.md) implements the embedder and the
tokenizer; [`NaturalLanguageBridge`](../NaturalLanguageBridge/README.md) consumes `IIntentResolver`
without knowing how it works.
