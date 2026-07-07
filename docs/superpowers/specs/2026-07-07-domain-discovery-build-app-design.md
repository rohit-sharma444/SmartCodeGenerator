# Domain Discovery — Build a Business App from Natural Language — Design

Date: 2026-07-07
Status: Approved (pending spec review)
Area: new `DomainDiscovery` project + `CodeGenerationEngine` (scaffolding) + `CodeGenerator` (CLI) + reuses `NaturalLanguageEngine`, `NaturalLanguageBridge`

## 1. Goal

Add a feature to `CodeGenerator` that turns a free-form English description into a
runnable business application. The user types a plain-language description; the
existing `NaturalLanguageEngine` extracts *seed* entities; a new stage recognizes
the topic/domain from those entities using the web, enriches the entity model with
the standard entities/fields that domain implies, lets the user review and approve
the model, and then scaffolds a complete, runnable .NET console application on disk.

Example:

```
CodeGenerator >> build app: track books that members borrow and return, with late fines
```

→ seed entities `{Book, Member, borrow, return, Fine}` → recognized topic `Library`
→ enriched model adds `{Loan, Reservation, Author, Catalog, ...}` → user approves →
a runnable console app is generated under the workspace.

## 2. Guiding principles

- **Hand-built, no LLM.** All "intelligence" comes from the existing 7-stage
  `NaturalLanguageEngine` plus web *text* mining. No third-party AI service.
- **The engine does the work.** Topic recognition and enrichment run the user's
  own NLP pipeline over fetched Wikipedia prose; this feature is the reason the
  from-scratch engine exists.
- **Human-in-the-loop before code.** Web mining of prose is imperfect by nature.
  Nothing is written to disk until the user reviews and approves the entity model.
- **Deterministic and offline-repeatable.** Fetched articles are cached to disk so
  re-runs (and the test suite) never depend on the network after the first fetch.

## 3. Current state (what exists and is reused)

- `NaturalLanguageEngine` — 7-stage pipeline (normalize → tokenize → morphology →
  POS → phrase → dependency → semantic) producing an `AnalysisContext` with
  entities, relationships, and intent. Reused **unchanged** to (a) extract seed
  entities from the user's text and (b) mine entities from fetched article prose.
- `NaturalLanguageBridge` — wires an NLP result to generation instructions. Reused
  to translate the approved model into generation calls.
- `CodeGenerationEngine` — Roslyn-based services already generate: `create console
  project`, `create class … with properties`, `add validation`, `generate
  validator`. Reused as the base of scaffolding; extended with new templates.
- `CodeGenerator` — interactive CLI. Extended with the `build app:` command.

## 4. Data flow

```
free-form NL text ("build app: ...")
   │
   ▼
NaturalLanguageEngine (existing) ─────► SEED CandidateDomainModel
   │                                     (entities user actually mentioned)
   ▼
TopicRecognizer (NEW)
   wiki-search seed entity terms ──────► IWikipediaClient (search API)
   best article = topic ───────────────► TopicResult { title, articleUrl, confidence }
   │
   ▼
DomainEnricher (NEW)
   fetch article prose (cached) ───────► IWikipediaClient (REST/extract API)
   run 7-stage engine on prose ────────► reuses NaturalLanguageEngine
   harvest + merge with seed ──────────► CandidateDomainModel (seed ∪ enriched)
   │
   ▼
Review & Confirm (CLI)  ── edit / approve / regenerate ──► approved model
   │
   ▼
NaturalLanguageBridge (existing) ──────► generation instructions
   │
   ▼
IApplicationScaffolder (NEW, CodeGenerationEngine)
   reuse: console project, classes, properties, validators
   new templates: JSON store, CRUD service, console-menu Program.cs
   │
   ▼
/GeneratedApp  →  dotnet run
```

Topic recognition and enrichment share one Wikipedia access layer
(`IWikipediaClient`); the entities that seed the search are the same article that
is mined for enrichment.

## 5. Components and interfaces

All new discovery components live in a **new `DomainDiscovery` project** that
references `NaturalLanguageEngine`. It carries the network + caching concerns that
do not belong in `NaturalLanguageBridge`'s pure wiring role.

### 5.1 `IWikipediaClient` (DomainDiscovery)
- `SearchAsync(IEnumerable<string> terms) → IReadOnlyList<SearchHit>` — Wikipedia
  search/opensearch API.
- `FetchExtractAsync(string articleTitleOrUrl) → string` — plain-text article
  extract via the REST/extract API.
- Wraps `HttpClient`. **Disk cache** keyed by request so repeated calls and tests
  are offline and deterministic. A fake implementation backed by fixture files is
  used in tests; nothing in the suite touches the network.

### 5.2 `ITopicRecognizer` (DomainDiscovery)
- `Recognize(IReadOnlyCollection<string> seedEntities) → TopicResult`.
- `WikipediaTopicRecognizer` builds a query from the seed entity terms, calls
  `IWikipediaClient.SearchAsync`, and picks the best hit. `TopicResult` carries
  the article title, URL, and a confidence score derived from search rank / term
  overlap.

### 5.3 `IDomainEnricher` and `IEntityHarvester` (DomainDiscovery)
- `IDomainEnricher.Enrich(TopicResult topic, CandidateDomainModel seed) →
  CandidateDomainModel`. Fetches the article extract via `IWikipediaClient`, runs
  it through the `NaturalLanguageEngine`, then delegates to `IEntityHarvester` and
  merges the result with the seed.
- `IEntityHarvester.Harvest(AnalysisContext) → CandidateDomainModel` — the isolated
  extraction concern: reads the engine's output, takes candidate entities from noun
  phrases and relationships from dependency/semantic output, frequency-ranks
  candidates, and applies **type-inference heuristics** for fields:
  - name contains `price|cost|amount|total|fee|fine` → `decimal`
  - name contains `date|time|created|updated|due` → `DateTime`
  - name contains `quantity|count|number|qty|age` → `int`
  - name starts `is|has` → `bool`
  - otherwise → `string`
- `Enrich` merges harvested entities/fields with the seed model, de-duplicating by
  name and keeping **provenance** on every entity/field (`Seed` vs `Wiki` + source
  URL) and a confidence score.

### 5.4 `CandidateDomainModel` (DomainDiscovery)
- `Entities`: each has `Name`, `Fields` (`Name`, inferred `Type`, `Provenance`),
  `Relationships` (target entity + role/verb), `Provenance`, `Confidence`.
- Serializable so the review loop can round-trip edits and so it can be handed to
  the bridge.

### 5.5 Review & Confirm loop (CodeGenerator CLI)
- Renders the recognized topic and the enriched model with per-entity confidence
  and provenance, then accepts: `approve`, `edit`, `remove <entity>`,
  `add <entity>`, `rename <old> <new>`, `regenerate`, `cancel`.
- Doubles as the safety net for thin/noisy extraction: the user can hand-add
  entities the miner missed before any code is generated.

### 5.6 `IApplicationScaffolder` (CodeGenerationEngine)
- `Generate(CandidateDomainModel approved, string targetDir) → GenerationResult`.
- Orchestrates existing generation (console project, one class + properties per
  entity, validators) and adds three **new templates**:
  - `IJsonStoreTemplate` — a per-entity JSON store using `System.Text.Json`; loads
    on start, saves on change; data files under `/GeneratedApp/data/<entity>.json`.
  - `ICrudServiceTemplate` — a `<Entity>Service` with Create/Read/Update/Delete
    over the JSON store.
  - `IConsoleMenuTemplate` — a `Program.cs` presenting a top-level menu of entities
    and a per-entity add/list/edit/delete submenu.

## 6. CLI trigger

A dedicated verb, chosen over auto-detecting intent on every line to avoid
confusion with the existing structured commands:

```
build app: <free-form description>
```

Everything after `build app:` is the free text fed to the engine. The command is
registered alongside the existing CLI commands and routes into the discovery
pipeline. `help` lists it.

## 7. Generated application layout

```
/GeneratedApp                     (one console project; folders below are inside it)
  GeneratedApp.csproj
  Program.cs                      (console menu host)
  /Models        <Entity>.cs      (one per approved entity)
  /Validators    <Entity>Validator.cs
  /Data          JsonStore.cs, <Entity>Store.cs
  /Services      <Entity>Service.cs (CRUD)
  /data          <entity>.json     (created at runtime)
```

`dotnet run` in `/GeneratedApp` launches the menu.

## 8. Error handling

- **No seed entities extracted** (text too vague) → report it and enter the review
  loop with an empty model so the user can hand-enter entities, or cancel.
- **Topic recognition low-confidence / multiple candidates** → show the top N
  article titles and let the user pick or refine before enrichment.
- **Network failure / offline** → if the article is cached, proceed from cache;
  otherwise degrade gracefully to generating from the seed model only, with a clear
  message. Never crash.
- **Weak enrichment** → whatever was found is still shown in the review loop; the
  user edits up from there.

## 9. Testing

- `IEntityHarvester` / `DomainEnricher` tested against **captured fixture article
  text** (checked-in) with a fake `IWikipediaClient`; asserts expected entities and
  inferred field types. No network.
- Type-inference heuristics unit-tested in isolation.
- `WikipediaTopicRecognizer` tested with a fake `IWikipediaClient` returning fixed
  search hits; asserts topic selection and confidence ordering.
- Review-loop command parsing tested with scripted input.
- `IApplicationScaffolder` tested by generating into a temp directory and running
  `dotnet build` on the output — compiling output is the pass condition.

## 10. Build order (vertical slices)

Because "full runnable app from prose" is ambitious, build as slices, each
demoable on its own:

1. `build app:` → seed extraction → `TopicRecognizer` → **print** recognized topic
   and article. Proves web recognition end to end.
2. `DomainEnricher` → **print** the enriched `CandidateDomainModel` + the review
   loop (edit/approve). Still no code generation.
3. `IApplicationScaffolder`: **models + validators** from the approved model (pure
   reuse of existing generation).
4. **JSON store + CRUD services** templates.
5. **Console-menu `Program.cs`** template → `dotnet run` works.

## 11. Out of scope (YAGNI)

- No LLM / third-party AI integration.
- No Web API or GUI host (console menu only).
- No relational DB / EF Core / migrations (JSON files only).
- No non-Wikipedia sources in this iteration (structured sources such as
  schema.org are a possible later enrichment source, behind the same interface).
- No authentication, multi-user, or deployment concerns in the generated app.
