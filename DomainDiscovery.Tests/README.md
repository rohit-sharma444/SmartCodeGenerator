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
| `Enrichment/` | `DomainEnricher`, `EntityHarvester`, `TypeInference`. |
| `Wikipedia/` | `WikipediaCache` and `HttpWikipediaClient` resilience (cache misses, fetch failures). |
| `Review/` | `ModelEditor` add/remove/rename edits. |
| `Model/` | `CandidateDomainModel` behavior. |

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
