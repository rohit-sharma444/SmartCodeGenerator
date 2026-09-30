# DomainDiscovery

Turns a plain-English description of a system into a **candidate domain model** —
a set of entities (and, where possible, their fields and relationships) that a
later phase can scaffold into code. It combines the
[`NaturalLanguageEngine`](../NaturalLanguageEngine/README.md) with Wikipedia to
go from *"a library that lends books to members"* to a concrete list of entities
like `Book`, `Member`, `Loan`.

Wikipedia is read **from a local store by default** — see [Where the articles
come from](#where-the-articles-come-from).

This is a class library (`net10.0`) with no console I/O. The
[`CodeGenerator`](../CodeGenerator/README.md) CLI drives it through the
`build app: <description>` command. Discovery is **target-agnostic** — it stops
at the candidate model; the output kind is chosen only after the user approves,
when [`AppScaffolding`](../AppScaffolding/README.md) turns that same model into a
runnable app for the selected target (WPF, console, web API, class library, or
worker).

## Pipeline

```
free text
    │
    ▼
NLE analysis             →  entities, intent, relationships
    │
    ▼
SeedEntityExtractor      →  the nouns the user actually named (seed model)
    │
    ▼
WikipediaTopicRecognizer →  match the seed — and the user's own words — to an article
    │
    ▼  (only if a topic is recognized)
ArticleRefresher         →  bring just these articles up to date, BEFORE they are read
    │
    ▼
DomainEnricher           →  harvest entities/fields from every article the user named
    │
    ▼
EntityShaper             →  decide what is really a domain class, and assign its role
    │
    ▼
RelationshipMiner        →  mine relationships from those same articles
    │
    ▼
DiscoveryResult          →  recognized topic + candidate domain model
```

If no topic is recognized (nothing matched, or no source is reachable),
discovery shapes and returns the seed entities alone, so the user still has
something to review and edit.

Three things about that order are deliberate:

- **A domain is not one article.** *"track customers, payments and invoices"*
  names three concepts and only one can win the vote, so enrichment reads the
  winner *and* the other articles the user named (`TopicResult.AllTitles`). The
  merge is additive and the first article to define a class keeps it.
- **Shaping happens once, over everything, and only then does mining run.**
  Shaping decides what is a class at all; mining needs that verdict plus each
  class's `EntityRole`, so that a rejected or merely-suggested class never
  acquires a relationship.
- **Refreshing happens before the reads, not after.** A run opens some thirty
  articles out of 7.2 million, so those thirty are the only ones worth updating.

## Where the articles come from

`CreateDefault` picks the source by looking for the local store:

| Store at `WikipediaStoreLocation.Resolve()` | Client | Refresher |
| --- | --- | --- |
| present | `DumpWikipediaClient` — local, deterministic, no rate limit | `ArticleRefresher` — updates the ~30 articles a run reads |
| absent | `HttpWikipediaClient` — the live MediaWiki API, cached under `cacheDirectory` | none (it is already reading a live Wikipedia) |

## Built-in domains

`Bundled/Domains/*.json` are 25 hand-built domains, embedded in the assembly: each is the JSON shape Ollama
returns (`LlmDomainModel`) plus weighted match `terms`. `BundledDomainSource` scores a description against
them (a weight-3 term names the domain on its own; 3 points needed), runs the winner through
`LlmModelNormalizer` as `ModelOrigin.BuiltIn`, and adds the seed classes the domain lacks.
`DomainModelSourceFactory` puts it after Ollama and an installed store, before online Wikipedia.

To add a domain, add a file; `CatalogContentTests` holds every file to the rules: types the normaliser
knows, links written as `<Target>Id` on Document/Transaction/Event classes only (the only roles that may
hold one), no shared weight-3 term, and an ordinary description that finds it.

The dump is the intended source; the HTTP client survives as the answer to *"the
store hasn't been imported yet"*, because failing outright would leave a fresh
clone unable to run `build app:` at all. The store is built by
[`WikipediaDumpImporter`](../WikipediaDumpImporter/README.md).

> ⚠️ `CreateDefault` **throws `InvalidOperationException`** for a store that has
> rows but no search index, rather than reading it anyway. That is not a
> slow store, it is an unusable one: every lookup becomes a full table scan, and
> one discovery makes dozens — measured on the 18 GB store, a single exact read
> took 24 seconds and one prefix search took 17 minutes. An import (or an
> `--index-only` rebuild) killed before its last step leaves it this way; the
> exception says what to run.

## Keeping the store up to date

The daily Wikipedia increments are applied by a Windows Service,
`SmartCodeGenWikipediaUpdater`, **not** by the CodeGenerator CLI. Install it once, from an
elevated PowerShell prompt:

    .\tools\install-updater-service.ps1

It checks at machine start and roughly daily thereafter, waiting the interval between cycles rather
than from a fixed clock — so a cycle that overruns (a multi-day catch-up) pushes the next check back
by that much rather than risking two cycles running back to back. Progress is published to
`update-status.json` beside the store; milestones and failures go to the Application event log
under the source `SmartCodeGen Wikipedia Updater`. The CLI reports the dump date, service state,
and progress from the status file at startup. The Application event log is a separate record; check
it via Event Viewer or `Get-WinEvent` (`Get-EventLog` is deprecated and absent from PowerShell 7).

Without this service installed, nothing updates the store — the CLI will say so and repeat the
command above.

To remove it: `.\tools\uninstall-updater-service.ps1`.

## Usage

```csharp
using DomainDiscovery;

// cacheDirectory is the on-disk memo for the HTTP fallback; the dump client ignores it.
var pipeline = DomainDiscoveryPipeline.CreateDefault(cacheDirectory: ".dd-cache");

DiscoveryResult result = pipeline.Discover("a library that lends books to members");

if (result.Topic.IsRecognized)
    Console.WriteLine($"Topic: {result.Topic.Title} ({result.Topic.Confidence:P0})");

foreach (var entity in result.Model.Entities)
    Console.WriteLine($"{entity.Name} [{entity.Provenance}]");
```

Pass `dumpDatabasePath` to read a store somewhere else. For tests or alternative
sources, use the constructor directly and supply your own `ISemanticAnalyzer`,
`ITopicRecognizer`, `IDomainEnricher`, `EntityShaper`, `IWikipediaClient`, and
(optionally) `IArticleRefresher`.

## Key types

| Type | Role |
| --- | --- |
| `DomainDiscoveryPipeline` | Orchestrates the stages; `Discover(text)` returns a `DiscoveryResult`. `CreateDefault` wires the dump store or the HTTP fallback. |
| `DiscoveryResult` | The recognized `TopicResult` plus the `CandidateDomainModel`. |
| `CandidateDomainModel` / `CandidateEntity` | The output model. Each entity carries a `Provenance` (`Seed` = from the user's text, `Wiki` = harvested, `Data` = read from a data source the user supplied), fields, relationships, and a confidence. `Data` is produced by [`DataIngestion`](../DataIngestion/README.md), not by anything in this project — the type is shared so the review loop and the scaffolder see one model whichever path built it. |
| `SeedEntityExtractor` | Maps an NLE `SemanticResult` to seed entities (nouns only — verbs and attributes inform enrichment, not entities). |
| `WikipediaTopicRecognizer` | Matches seed names *and the user's raw text* to an article. The raw text matters: the domain is often named by a phrase (*"point of sale"*) that no single seed carries. |
| `DomainEnricher` / `EntityHarvester` | Pull additional entities and fields from an article. |
| `EntityShaper` | Decides what is genuinely a domain class and resolves each one's `EntityRole`. Reads each candidate's own article. |
| `RelationshipMiner` | Mines relationships from the articles the classes came from — a `Payee`↔`Payment` link is stated in the `Payment` article and nowhere else. |
| `ModelEditor` | Add / remove / rename entities — used by the CLI's interactive review loop. |
| `IWikipediaClient` | The seam. `DumpWikipediaClient` (local store) and `HttpWikipediaClient` (+ `WikipediaCache`) implement it; cache reads and best-effort writes are guarded so misses never throw. |
| `IArticleRefresher` | `ArticleRefresher` updates the handful of articles a run touches; `NoArticleRefresher` is the no-op used with the live API. |

## Layout

| Folder | Contents |
| --- | --- |
| `SeedExtraction/` | `SeedEntityExtractor`, `NameNormalizer`. |
| `Recognition/` | `ITopicRecognizer` and the Wikipedia implementation. |
| `Enrichment/` | `DomainEnricher`, `EntityHarvester`, `EntityShaper`, `RelationshipMiner`, `PropertyMiner`, `TypeInference`, `HypernymExtractor`, and the lexicons (`ArchetypeLexicon`, `DomainFieldLexicon`, `StopWords`). |
| `Wikipedia/` | `IWikipediaClient`, `HttpWikipediaClient`, `WikipediaCache`. |
| `Wikipedia/Dump/` | The offline store: `WikipediaStore`, `DumpWikipediaClient`, `WikitextStripper`, `DumpPageReader`, `ArticleRefresher`, `DumpFreshness`, `IncrementalUpdater`. |
| `Review/` | `ModelEditor`, `FieldSpec` for interactive edits. |
| `Model/` | `CandidateDomainModel`, `TopicResult`, `SearchHit`, `DefaultFields`. |

## Dependencies

Project reference to `NaturalLanguageEngine`. The offline store is SQLite
(`Microsoft.Data.Sqlite`) and dump decoding uses `SharpZipLib`; the HTTP
fallback uses the built-in `HttpClient` and caches pages under the directory
passed to `CreateDefault`.

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
