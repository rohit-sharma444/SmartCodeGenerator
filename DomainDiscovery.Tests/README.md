# DomainDiscovery.Tests

Test suite for [`DomainDiscovery`](../DomainDiscovery/README.md). Uses **xUnit**
(`net10.0`).

Tests run **offline and deterministically**: a `FakeWikipediaClient` and a
`StubAnalyzer` (under `Fakes/`) stand in for the real Wikipedia HTTP client and
NLE, so no network access is required.

## Organization

| Folder | Focus |
| --- | --- |
| *(root)* | `DomainDiscoveryPipeline` end-to-end orchestration. |
| `SeedExtraction/` | `SeedEntityExtractor` — mapping NLE results to seed entities. |
| `Recognition/` | `WikipediaTopicRecognizer` — matching seeds to an article. |
| `Enrichment/` | `DomainEnricher`, `EntityHarvester`, `EntityShaper`, `RelationshipMiner`, `TypeInference`. |
| `Wikipedia/` | `WikipediaCache` and `HttpWikipediaClient` resilience (cache misses, fetch failures, timeouts). |
| `Wikipedia/Dump/` | The offline store: `WikipediaStore` and `DumpWikipediaClient`, over small temporary databases. |
| `Llm/` | The Ollama source: client and streaming, the two-phase flow, partial-JSON recovery, the normaliser and key-field rules. `OllamaIntegrationTests` needs a live server. |
| `Bundled/` | The 25 built-in domains: catalogue loading, matching and stemming, and `CatalogContentTests`, which holds every domain file to the rules. |
| *(root)* | Also `DomainModelSourceFactoryTests` (the fallback order), `DiscoveryConfigTests`, `WikipediaStoreLocationTests` and `WikipediaDomainModelSourceTests`. |
| `Review/` | `ModelEditor` add/remove/rename edits. |
| `Model/` | `CandidateDomainModel` behavior. |
| `Fakes/` | `FakeWikipediaClient`, `StubAnalyzer` — the stand-ins that keep the suite offline. |
| `Fixtures/` | `sample-dump.xml` — a small MediaWiki export, so the dump reader is tested without the 25 GB file. |

## Running

```bash
# from the repository root

# run this suite
dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj

# a single class
dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj \
  --filter "FullyQualifiedName~WikipediaCacheTests"
```

Or run the whole solution: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`.
