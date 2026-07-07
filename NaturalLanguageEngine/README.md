# Natural Language Engine

A multi-layered natural language processing pipeline that analyzes English text
to extract **entities**, **relationships**, and **intent**. It is a self-contained
`net10.0` class library with no third-party NLP dependencies — the English
knowledge base, tokenizers, tagger, and parser are all built in.

## 🏗️ Architecture

The engine processes text through seven sequential stages, each isolated in its
own layer and communicating via a shared `AnalysisContext`:

1. **Normalization** (Order: 10) — standardizes text (whitespace, case handling)
2. **Tokenization** (Order: 20) — breaks text into sentences and tokens
3. **Morphology** (Order: 30) — lemmatization and stemming
4. **Part-of-Speech Tagging** (Order: 35) — identifies word types (noun, verb, …)
5. **Phrase Detection** (Order: 40) — identifies noun/verb phrases and their heads
6. **Dependency Parsing** (Order: 45) — extracts syntactic relationships between words
7. **Semantic Analysis** (Order: 50) — recognizes entities, derives intent, and extracts relationships

Assembly of the whole pipeline lives in [`Core/EngineBuilder.cs`](Core/EngineBuilder.cs).

## 🚀 Quick Start

### Basic usage

```csharp
using NaturalLanguageEngine.Abstractions.Semantics;
using NaturalLanguageEngine.Models.Semantics;

// EngineBuilder lives in the global namespace.
ISemanticAnalyzer analyzer = EngineBuilder.BuildDefault().Analyzer;

SemanticResult result = analyzer.Analyze("Book a flight to London at 14:30");

Console.WriteLine($"Intent: {result.Intent?.Name}");
foreach (var entity in result.Entities)
    Console.WriteLine($"  {entity.Type}: {entity.Text}");
foreach (var rel in result.Relationships)
    Console.WriteLine($"  {rel.Source.Text} --{rel.Type}--> {rel.Target.Text}");
```

Illustrative output:

```
Intent: BookIntent
  Action: Book
  Object: flight
  Location: London
  Time: 14:30
  Book --Object--> flight
  Book --Location--> London
```

### With configuration options

Enable logging and caching (see [`EngineBuilderOptions`](Core/EngineBuilderOptions.cs)):

```csharp
using NaturalLanguageEngine.Core;

var build = EngineBuilder.Build(new EngineBuilderOptions
{
    EnableLogging = true,   // capture per-stage metrics on the returned Logger
    EnableCaching = true,   // memoize results by input text
    MaxCacheSize  = 200,
});

ISemanticAnalyzer analyzer = build.Analyzer;
PipelineLogger?   logger   = build.Logger;   // null unless EnableLogging is true

var result = analyzer.Analyze("Alice sent the report to Bob on 2025-06-30");
```

## 📊 Features

### Entity recognition

The recognized [`EntityType`](Models/Semantics/EntityType.cs) values are:

- **Person** — proper nouns and capitalized names
- **Organization** — company names, institutions
- **Location** — cities, countries, geographic locations
- **Date** — ISO dates (`2025-06-30`) and natural-language dates
- **Time** — `HH:MM` times (`14:30`) and time ranges
- **Quantity** — numbers with optional units
- **Value** — email addresses, URLs
- **Action** — verb phrases that indicate actions
- **Attribute** — descriptive modifiers
- **Object** — non-person, non-location entities
- **Unknown** — unclassified tokens

Each `Entity` carries its source `Text`, a `Type`, a `Confidence` score, and a
free-form `Metadata` bag.

### Intent derivation

- Extracts the first action verb and derives intent (e.g. `"book"` → `"BookIntent"`).
- Falls back to `"DescribeIntent"` for noun-only sentences.
- Confidence is blended from the action's confidence.

### Relationship extraction

Recognized [`RelationshipType`](Models/Semantics/RelationshipType.cs) roles link
a source entity to a target — subject, object, location, time, and modifier
relationships. The extractor prefers syntactic dependency parsing when available
and falls back to positional heuristics for edge cases.

### Diagnostics

- Errors and warnings are reported on `SemanticResult.Diagnostics`.
- A **Critical** diagnostic means a stage failed and analysis stopped early — so
  entities/relationships/intent may be partial. Callers that care about
  completeness should check `Diagnostics` rather than assuming a normal empty
  result.
- Diagnostic codes follow the pattern `NLE.<Layer>.<Issue>`.

## 🔧 Configuration

[`EngineBuilderOptions`](Core/EngineBuilderOptions.cs):

| Option | Default | Effect |
| --- | --- | --- |
| `EnableLogging` | `false` | Capture per-stage execution metrics via `PipelineLogger`. |
| `EnableCaching` | `false` | Memoize analysis results by input text. |
| `MaxCacheSize` | `100` | Max cached results; oldest is evicted when full. |
| `StopOnCriticalError` | `true` | Stop the pipeline on the first critical error (results partial). |
| `IncludeDiagnostics` | `true` | Report warnings/errors in `SemanticResult.Diagnostics`. |

## 📈 Performance

Typical performance on a modern system (illustrative):

| Metric | Value |
|--------|-------|
| Simple sentence | 5–15 ms |
| Complex sentence (3+ entities) | 15–30 ms |
| Dependency parsing stage | ~70% of total time |
| Memory (base) | ~5 MB |

**Optimization tips:** enable caching for batch processing of similar texts, and
enable logging to find per-stage bottlenecks.

## 🧪 Testing

Tests live in [`NaturalLanguageEngine.Tests`](../NaturalLanguageEngine.Tests/README.md)
(xUnit).

```bash
# from the repository root

# run all engine tests
dotnet test NaturalLanguageEngine.Tests/NaturalLanguageEngine.Tests.csproj

# run one test class
dotnet test NaturalLanguageEngine.Tests/NaturalLanguageEngine.Tests.csproj \
  --filter "FullyQualifiedName~EntityRecognizerTests"
```

Tests are organized by layer: **Core** (pipeline, engine), **Lexical**
(tokenization, sentence splitting), **Morphology** (lemmatization, stemming),
**Syntax** (POS tagging, phrase detection, dependency parsing), and
**Semantics** (entity recognition, intent, relationships).

## ⚠️ Known limitations

1. **Passive voice** — relationships may be reversed.
2. **Coordinated subjects/objects** — limited support.
3. **Negation** — not explicitly handled.
4. **Questions** — basic support only.
5. **Language** — English only (English morphology rules, POS tagset, and
   knowledge base; not suitable for multilingual input).
6. **Single verb anchor** — only the first action verb drives intent.

## 🔌 Extending the engine

The pipeline is a list of stages implementing
[`IPipelineStage`](Abstractions/Pipeline/IPipelineStage.cs). To add behavior,
implement a stage, give it an `Order` that slots it between existing stages, and
register it on the `Pipeline` (see `EngineBuilder.Build`). The English knowledge
base is assembled by `LanguageRepositoryBuilder().UseEnglish()`; swap that
builder to supply a different `ILanguageRepository`.

## 📚 API reference (key types)

| Type | Namespace | Purpose |
| --- | --- | --- |
| `EngineBuilder` | *(global)* | `BuildDefault()` / `Build(options)` → `EngineBuildResult`. |
| `EngineBuildResult` | `NaturalLanguageEngine.Core` | Holds the `Analyzer` and an optional `Logger`. |
| `ISemanticAnalyzer` | `NaturalLanguageEngine.Abstractions.Semantics` | `Analyze(string) → SemanticResult`. |
| `SemanticResult` | `NaturalLanguageEngine.Models.Semantics` | `Entities`, `Relationships`, `Intent`, `Confidence`, `Diagnostics`. |
| `Entity` | `NaturalLanguageEngine.Models.Semantics` | `Text`, `Type`, `Confidence`, `Metadata`. |
| `Relationship` | `NaturalLanguageEngine.Models.Semantics` | `Source`, `Target`, `Type`. |
| `Intent` | `NaturalLanguageEngine.Models.Semantics` | `Name`, `Confidence`. |

## 🤝 Contributing

Areas for improvement:

- [ ] Better handling of passive voice
- [ ] Support for coordinated verbs
- [ ] Explicit negation handling
- [ ] Extended entity types (Product, Event, …)
- [ ] Multi-language support
- [ ] Performance optimizations

## 📋 License & attribution

See the repository root for license information.

---

**Target framework**: .NET 10.0
