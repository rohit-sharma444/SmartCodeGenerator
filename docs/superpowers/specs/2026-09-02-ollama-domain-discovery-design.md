# Discovering a domain from a local LLM instead of Wikipedia

Date: 2026-09-02
Branch: `ollama-domain-discovery`

## The problem

The Discover stage asks Wikipedia what a domain contains. It extracts seed nouns with the
NLE, searches for an article, and mines classes and properties out of encyclopedia prose with
`EntityHarvester`, `PropertyMiner` and `TypeInference`.

That source is wrong for the job. Wikipedia describes *concepts*; it was never written to
describe *data models*. The failures are recorded rather than theoretical: one-word seeds land
on disambiguation pages, and seed extraction is capped by the NLE lexicon, so `build app:`
often extracts few or no classes for a description a developer would find obvious.

An LLM asked "what classes does a point-of-sale system need?" answers that question directly.
Measured on this machine, `qwen2.5:3b` returned Sale, Product, Customer, Employee, SaleItem
and Transaction with typed properties in 64 seconds; `llama3.2:3b` returned Product, Customer,
Order, OrderItem and Payment in 72 seconds. Both are better starting points than the current
path produces for the same sentence.

## Why Ollama specifically

No API key is available, and the tool is to be sold. That rules out anything requiring the
customer to hold an OpenAI/Anthropic/Gemini key at first run, and rules out shelling out to the
`claude` CLI (which would require every customer to buy an Anthropic subscription and install a
developer tool).

Ollama is keyless, offline, free per use, MIT-licensed, and installable alongside the product:
roughly 1 GB for Ollama plus ~2 GB for a 3B model. That is a real deployment cost but a smaller
one than the status quo, which points at a 20 GB SQLite dump.

**Open licensing question, to settle before shipping commercially:** the licence of the chosen
*model* is separate from Ollama's. Llama 3.2 permits commercial use with attribution
conditions; some Qwen2.5 sizes ship under a research-only licence and others under Apache 2.0.
The default model's licence must be read, not assumed, before it is bundled.

## What is NOT changing

- The CSV / Excel / JSON / SQL data-source path. It never touches discovery.
- The Review, Build and scaffolding stages. They consume `CandidateDomainModel` and continue to.
- The dump importer and the Wikipedia update service. They remain a local speed optimisation.
- `EntityRole` and `EntityRoleRank`. See "Rules the LLM does not get to overrule".

## Design

### 1. `IDomainModelSource`

A new interface in `DomainDiscovery`:

```csharp
public interface IDomainModelSource
{
    DiscoveryOutcome Discover(string description);
}

public sealed record DiscoveryOutcome(
    CandidateDomainModel Model,
    string SourceLabel,     // "qwen2.5:3b (Ollama)" or "Wikipedia"
    bool Succeeded,
    string? Message);       // why it fell back, when it did
```

**It returns `DiscoveryOutcome`, not the existing `DiscoveryResult`, and that is deliberate.**
`DiscoveryResult` carries a `TopicResult`, whose `IsRecognized` is defined as
`!string.IsNullOrEmpty(ArticleUrl)` — a Wikipedia article URL. The Ollama path has no article
and never will, so reusing that type would make every successful LLM discovery report itself as
*unrecognised*, and `DiscoverStage.razor:97` would wrongly print "No topic recognized online"
over a perfectly good class model.

`DiscoveryOutcome` also carries the source label that section 5 requires on screen, so one type
serves both needs. `WikipediaDomainModelSource` maps its `TopicResult` into this shape;
`DiscoveryResult` and `TopicResult` themselves are left untouched for the existing pipeline.

Two implementations:

- `WikipediaDomainModelSource` — wraps today's `DomainDiscoveryPipeline` with no behaviour change.
- `OllamaDomainModelSource` — the new path.

`DiscoveryStep.Discover` (`CodeGenerator/Web/Workflow/DiscoveryStep.cs`) and `BuildAppCommand`
(`CodeGenerator/App/BuildAppCommand.cs`) resolve a source instead of calling
`DomainDiscoveryPipeline.CreateDefault` directly. Both surfaces use the same source, so the web
page and the command line cannot disagree about what a description contains.

### 2. Talking to Ollama

`OllamaClient` posts to `http://localhost:11434/api/chat` with `stream:false`,
`temperature:0`, and a JSON schema in the `format` field. Schema-constrained output is native to
Ollama and was verified working against both installed 3B models, which is what makes parsing
reliable at this model size.

The requested schema, per class: `name`, `role` (one of the `EntityRole` values), `purpose`
(one line), `properties` (`name` plus C# `type`), and `relationships` (`target` plus `role`).

Two failure modes this must handle, both established by measurement:

- **Timeout.** `HttpClient` defaults to 100 seconds. The probes above took 64-72 s for a *small*
  domain, so the default would kill a real request. Timeout is set to 10 minutes and is
  configurable.
- **Cold start.** The first call includes loading ~2 GB of weights into RAM. A warm-up request
  is issued while the Discover panel paints its label, so the visible wait excludes model load.

### 3. Normalisation is a required stage, not polish

Neither model's raw output is usable as-is. Measured: `llama3.2:3b` emitted `customer_id`,
`order_date` and the type `datetime`; `qwen2.5:3b` emitted a `SaleItem` with no key and both a
`Sale` and an overlapping `Transaction`.

`LlmModelNormalizer` therefore:

1. Puts identifiers through the existing `NameNormalizer` (`ToEntityName`, `ToFieldName`).
2. Maps model type names onto C# types (`datetime` to `DateTime`, `int`, `decimal`, `string`).
   An unrecognised type falls back to `string` rather than emitting an uncompilable type name.
3. Converts `customer_id`-shaped scalar properties into relationships, instead of leaving a
   stray `int` beside a relationship that means the same thing. If the resulting relationship is
   then refused by `CanReference` (section 4), the property is **kept as a plain scalar** rather
   than vanishing — a refused relationship must not silently delete information the model gave us.
4. Ensures every class has a key, adding an `Id` of type `int` where the model supplied none.
5. Drops classes whose name does not survive `NameNormalizer.IsValidIdentifier`.

This stage is deterministic, so it carries the test coverage. The LLM call itself does not.

### 4. Rules the LLM does not get to overrule

`EntityRoleRank.CanReference` states that only a `Document`, `Transaction` or `Event` may carry
a foreign key. Its own comments record why: a real run that relied on rank alone produced
`Restaurant.CustomerId`, `CashRegister.CustomerId` and `Inventory.ComputerId`.

That is domain logic, not Wikipedia-scraping logic, and a 3B model will reproduce exactly those
mistakes. So every relationship the LLM proposes is filtered through `CanReference` before it
reaches the model. **The LLM proposes; the existing rules dispose.** The
`Restaurant.CustomerId` case becomes a regression test on this path.

### 5. Source selection and fallback

A new `DiscoveryConfig` in `DomainDiscovery`, following the pattern `AiConfig` already
establishes: a JSON file beside the binary, never throwing, a missing or unreadable file
yielding defaults.

```json
{ "Source": "Ollama", "Model": "qwen2.5:3b",
  "Endpoint": "http://localhost:11434", "TimeoutMinutes": 10 }
```

`DomainModelSourceFactory` probes `GET /api/tags` — a millisecond call — and falls back to
`WikipediaDomainModelSource` when Ollama is unreachable or the configured model is not pulled.

The fallback is reported, never silent. The Discover panel states which source and model
produced the classes, on success and on fallback alike. A customer whose Ollama service has
stopped must not simply observe the tool quietly getting worse. This also avoids the silent
config-override shape that caused the `EffectiveLayout` defect.

### 6. Keeping `why` answerable

`why <ClassName>` reads `CandidateDomainModel.Decisions`. Bypassing the Wikipedia miners would
otherwise leave that trail empty on the new path.

Each accepted class gets a `DecisionRecord` carrying a new `DecisionReasons` constant, with the
model's own one-line `purpose` as its evidence. Classes the normaliser drops are recorded in
`Rejected` with the reason they were dropped.

### 7. Testing

- Unit tests drive a fake `IDomainModelSource`. The suite stays fast and deterministic and does
  not require Ollama.
- `LlmModelNormalizer` gets direct tests over captured model output, including the
  snake_case, bad-type, missing-key and `Restaurant.CustomerId` cases.
- Fallback selection is tested with an unreachable endpoint.
- One integration test calls real Ollama and skips itself when `/api/tags` does not answer, so
  CI and other machines are not broken by a missing install.

## Risks

- **Quality varies with the model and the machine.** A 3B model is the floor. Output is a
  starting point for the Review stage, which is where the user corrects it — as today.
- **Speed.** ~1 minute per discovery on this CPU-only machine, longer for large domains or
  bigger models. The Discover panel already counts seconds and runs the work on a background
  task, so no UI rework is needed, but the label text should set the expectation.
- **Non-determinism.** `temperature:0` reduces it but does not remove it. Scripted `build app`
  runs will not be byte-identical across runs on the Ollama source; a run needing determinism
  should pin the source to Wikipedia.
- **Model licensing.** Unresolved; see above. Blocks commercial bundling, not development.
