# DomainDiscovery

Turns a plain-English description of a system into a **candidate domain model** —
a set of entities (and, where possible, their fields) that a later phase can
scaffold into code. It combines the [`NaturalLanguageEngine`](../NaturalLanguageEngine/README.md)
with Wikipedia lookups to go from *"a library that lends books to members"* to a
concrete list of entities like `Book`, `Member`, `Loan`.

This is a class library (`net10.0`) with no console I/O. The
[`CodeGenerator`](../CodeGenerator/README.md) CLI drives it through the
`build app: <description>` command.

## Pipeline

```
free text
    │
    ▼
NLE analysis            →  entities, intent, relationships
    │
    ▼
SeedEntityExtractor     →  the nouns the user actually named (seed model)
    │
    ▼
WikipediaTopicRecognizer→  match the seed to a Wikipedia article (the "topic")
    │
    ▼  (only if a topic is recognized)
DomainEnricher          →  harvest more entities/fields from that article
    │
    ▼
DiscoveryResult         →  recognized topic + candidate domain model
```

If no topic is recognized (e.g. offline, or nothing matched), discovery returns
the seed entities alone so the user still has something to review and edit.

## Usage

```csharp
using DomainDiscovery;

// cacheDirectory is where fetched Wikipedia pages are memoized on disk.
var pipeline = DomainDiscoveryPipeline.CreateDefault(cacheDirectory: ".dd-cache");

DiscoveryResult result = pipeline.Discover("a library that lends books to members");

if (result.Topic.IsRecognized)
    Console.WriteLine($"Topic: {result.Topic.Title} ({result.Topic.Confidence:P0})");

foreach (var entity in result.Model.Entities)
    Console.WriteLine($"{entity.Name} [{entity.Provenance}]");
```

`CreateDefault` wires the real Wikipedia HTTP client and the default NLE
analyzer. For tests or alternative sources, use the constructor directly and
supply your own `ISemanticAnalyzer`, `ITopicRecognizer`, and `IDomainEnricher`.

## Key types

| Type | Role |
| --- | --- |
| `DomainDiscoveryPipeline` | Orchestrates the four stages; `Discover(text)` returns a `DiscoveryResult`. |
| `DiscoveryResult` | The recognized `TopicResult` plus the `CandidateDomainModel`. |
| `CandidateDomainModel` / `CandidateEntity` | The output model. Each entity carries a `Provenance` (`Seed` = from the user's text, `Wiki` = harvested from an article), fields, and a confidence. |
| `SeedEntityExtractor` | Maps an NLE `SemanticResult` to seed entities (nouns only — verbs and attributes inform enrichment, not entities). |
| `WikipediaTopicRecognizer` | Matches seed entity names to a Wikipedia article. |
| `DomainEnricher` / `EntityHarvester` | Pull additional entities and fields from the matched article. |
| `ModelEditor` | Add / remove / rename entities on a candidate model — used by the CLI's interactive review loop. |
| `HttpWikipediaClient` / `WikipediaCache` | Fetch + on-disk cache for article content. Cache reads and best-effort writes are guarded so misses never throw. |

## Layout

| Folder | Contents |
| --- | --- |
| `SeedExtraction/` | `SeedEntityExtractor`, `NameNormalizer`. |
| `Recognition/` | `ITopicRecognizer` and the Wikipedia implementation. |
| `Enrichment/` | `IDomainEnricher`, `IEntityHarvester`, `TypeInference`. |
| `Wikipedia/` | `IWikipediaClient`, `HttpWikipediaClient`, `WikipediaCache`. |
| `Review/` | `ModelEditor` for interactive edits. |
| `Model/` | `CandidateDomainModel`, `TopicResult`, `SearchHit`. |

## Dependencies

Project reference to `NaturalLanguageEngine`. Wikipedia access uses the built-in
`HttpClient`; fetched pages are cached under the directory passed to
`CreateDefault`.

> **Note:** seed extraction is only as rich as NLE's lexicon, so short or
> unusual descriptions may yield few or no seed entities. See the CLI's review
> loop for adding entities by hand.

## Build & test

```bash
# from the repository root
dotnet build DomainDiscovery/DomainDiscovery.csproj
```

Covered by [`DomainDiscovery.Tests`](../DomainDiscovery.Tests/README.md), which
uses a fake Wikipedia client so tests run offline and deterministically.
