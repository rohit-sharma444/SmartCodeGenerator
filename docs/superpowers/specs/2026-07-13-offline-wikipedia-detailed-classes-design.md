# Offline Wikipedia, and the detailed classes it pays for

**Date:** 2026-07-13
**Status:** Approved, ready for planning

## Why

Two requests, and they turn out to be one piece of work.

Discovery reads Wikipedia over the MediaWiki API. That is slow (a 150 ms throttle between
uncached requests), fragile (HTTP 429 arrives well before a real domain's candidates are all
in — the reason `FetchLeads` batches at all), and every failure path degrades to an empty
result, so a bad network quietly thins the discovered model instead of failing loudly.

Separately, the classes we generate are thin. A class gets three or four fields from the
curated `DomainFieldLexicon`, or three from an `ArchetypeLexicon` shape guessed from a single
lead sentence.

The link between the two: the reason `EntityShaper` guesses from one sentence is that it
**cannot afford to read each candidate's own article**. Twenty candidates means twenty article
fetches, and that is exactly what the API's rate limit forbids. Offline, that cost is a local
seek. **The dump is what makes detailed classes possible**, not merely a speed fix.

## What we have, measured

The dump is already on disk at `D:\SmartCodeGenerator-Repository\Dump`:

| Fact | Value |
| --- | --- |
| File | `enwiki-latest-pages-articles.xml.bz2` |
| Size | 25,452,565,841 bytes — exactly the published size, so the download is intact |
| Variant | **single-stream**, not `-multistream` |
| Compression ratio | 3.83x, measured — the dump expands to roughly 97 GB of XML |
| bz2 decode | 23.6 MB/s of output (6.2 MB/s of input), single-threaded, CPU-bound |
| Raw disk read | 126 MB/s — not the bottleneck |
| Full sequential decode | ~69 minutes |

The single-stream variant is the fact that decides the architecture. A single-stream bz2 is one
continuous compressed stream: there is no way to seek to "Point of sale" without decompressing
from byte zero. **The dump therefore cannot be queried directly.** It must be imported once into
a store that supports lookup by title.

(The `-multistream` variant *can* be seeked, via a 283 MB companion index. We are not using it:
it would mean a second 26.5 GB download, and the one-time import below buys something the
multistream dump does not — the wikitext is stripped to prose once, at import, rather than on
every read.)

Redirect pages are present in the dump as `<redirect title="Computer accessibility" />`. This
matters: the redirect map is an editor-curated synonym list, and it is what replaces the API's
fuzzy `opensearch` ranking.

## Part A: the offline Wikipedia layer

`IWikipediaClient` does not change. Its four methods and its never-throw contract stay exactly
as they are, so nothing downstream of it is affected. We add a second implementation and a
one-time importer.

### DumpImporter

A new console entry point, run once. One thread decompresses the bz2 with a managed decoder and
feeds an `XmlReader`; a pool of workers strips wikitext to plain prose and writes to the store.
The bz2 decode is the hard ceiling at ~69 minutes and everything else rides alongside it, so
expect **1.5-2 hours, once**.

It is resumable: it records the last committed page id, so a crash or a Ctrl-C costs the run's
tail, not the whole run.

Stripping wikitext to prose is the job `explaintext=1` used to do for us, and it is the
quality-critical part of this work. Everything downstream — `HypernymExtractor`,
`EntityHarvester`, `RelationshipMiner`, and the new `PropertyMiner` — reads the prose this
step produces.

**The importer preserves list markers.** A `*` bullet in wikitext survives into the stored body
as a `* ` line prefix. This is a deliberate departure from what the API gave us: flat
`explaintext` destroyed list structure, and `PropertyMiner` (Part B) depends on it.

### The store

A single SQLite file, `Dump/enwiki.db`.

- `pages(title PK, lead, body, links)` — `body` and `links` held as one Deflate-compressed
  blob. Roughly 10-15 GB.
- `redirects(from PK, to)` — roughly 11M rows.
- A normalised-title index, for exact and prefix lookup.

Mainspace (`ns=0`) only.

### DumpWikipediaClient

Implements `IWikipediaClient` against SQLite. No throttle, no 429, no timeout, no cache
directory.

- `Search` resolves in order: exact title, then redirect, then normalised title, then prefix.
  Deterministic, unlike the API's fuzzy ranking.
- `FetchExtract`, `FetchLead`, `FetchLeads`, `FetchLinks` are single indexed reads. `FetchLeads`
  keeps its batch signature — it is now simply an `IN` clause, and the API-batching rationale in
  its doc comment no longer applies to this implementation.

### Wiring

`DomainDiscoveryPipeline.CreateDefault` uses `DumpWikipediaClient` whenever the store exists —
which, per the instruction to always use the dump, is the normal case. `HttpWikipediaClient`
stays in the tree, with its tests, as the fallback when the store has not been built yet. It is
no longer the default.

### Housekeeping

`Dump/` is inside the git repo and is **not** currently ignored — 25 GB of untracked file at the
repo root, one `git add .` away from disaster. It goes into `.gitignore`, along with the `.db`.

## Part B: detailed classes

Detail arrives in five layers, in precedence order. Layers 1, 4 and 5 exist today; 2 is widened;
3 is new.

1. **`DomainFieldLexicon`** — the curated table. Unchanged, and still wins on conflict.
2. **`RoleTemplate`** (new) — a fuller baseline per `EntityRole`. Document gets Number,
   IssuedOn, Status; Transaction gets Amount, Currency, OccurredOn, Method; Party gets FullName,
   Email, Phone, Address; and so on. `ArchetypeLexicon` keeps its real job — resolving the role
   and the Shaped/Abstract/Unknown verdict from a hypernym — and stops doubling as a stingy
   three-field shape table.
3. **`PropertyMiner`** (new) — reads each candidate's own full article and mines the properties
   it states. See below.
4. **`RelationshipMiner`** — unchanged. Still emits the `<Target>Id` foreign key.
5. **`Id` and audit fields** (`CreatedOn`, `ModifiedOn`) — added in `DomainModelMapper`, not in
   discovery. They are what a running app needs, not what the domain says, and that boundary is
   worth keeping clean.

### PropertyMiner

Two evidence sources, in confidence order:

- **List enumerations.** "An invoice typically contains: `*` invoice number `*` date of issue
  `*` seller…". These appear on exactly the concept articles the discovery path lands on, and
  they are the highest-confidence signal available. They are also the reason the importer keeps
  list markers.
- **Prose enumerations.** A containment verb (contains, includes, comprises, records, shows)
  followed by a noun-phrase list.

Each mined phrase goes through the existing `NameNormalizer`, then identifier validation, then
`TypeInference` for its C# type.

Gates, so the mine does not fill classes with rubbish:

- stop-word rejection
- a word-count cap per phrase
- dedupe against fields the entity already has
- a ceiling of roughly 8 mined properties per class
- the CS0542 guards `RelationshipMiner` already applies: never the class's own name, never a
  bare `Id`

**No evidence is a no-op.** A candidate whose article yields nothing ends up exactly as well
shaped as it is today. This must never make a model worse than the one it replaces.

### Navigation properties

`CandidateEntity.Relationships` is populated by `RelationshipMiner` and then **ignored** by
`DomainModelMapper` today. We use it: `Order.Customer` forward, `Customer.Orders` inverse, both
marked `[JsonIgnore]`.

Nav props are not free in this codebase, and three things must change or they break the
generated app:

- **`CodeGenerationEngine`.** `PropertyDefinition` gains an `Attributes` list; `ClassDefinition`
  gains a `Usings` list; `RoslynClassGenerator.Generate` emits both. It emits **no** using
  directives today, which is a latent bug already — the `[Required]` attribute it writes has no
  `using System.ComponentModel.DataAnnotations;` behind it.
- **`MainWindowTemplate`.** The generated grid is `AutoGenerateColumns="True"`, so today a
  `public Customer Customer` would render a column of type names and a `List<Payment>` a column
  reading `System.Collections.Generic.List'1[Payment]`. `OnAutoGeneratingColumn` gains a guard
  that cancels any column whose type is not a simple display type.
- **`JsonStoreTemplate` and `MainViewModel`.** `JsonStore` persists with `System.Text.Json`, and
  `Order.Customer` -> `Customer.Orders` -> `Order` is a reference cycle that **throws on save**.
  `[JsonIgnore]` keeps persistence foreign-key-only, and a generated `RelationshipResolver`
  populates the nav props from the FKs once every store has loaded.

### What a generated class becomes

```csharp
public class Order
{
    public int Id { get; set; }

    // role template (Document)
    public string Number { get; set; }
    public DateTime IssuedOn { get; set; }
    public string Status { get; set; }

    // mined from the article
    public DateTime DeliveryDate { get; set; }
    public string ShippingTerms { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; }

    // relationships
    public int CustomerId { get; set; }
    [JsonIgnore] public Customer Customer { get; set; }
    [JsonIgnore] public List<Payment> Payments { get; set; }

    // audit
    public DateTime CreatedOn { get; set; }
    public DateTime ModifiedOn { get; set; }
}
```

## Testing

- **Importer** — against a 40 MB sample of real dump XML (937 pages, redirects included),
  already extracted. Covers wikitext stripping, list-marker preservation, redirect capture, and
  namespace filtering.
- **DumpWikipediaClient** — against a small purpose-built SQLite fixture. Covers each of the
  four `IWikipediaClient` methods and the search resolution order.
- **PropertyMiner** — against real article text, including an article that yields nothing, to
  pin the no-op contract.
- **Nav props** — a `DomainModelMapper` test asserting `[JsonIgnore]` and the inverse
  collection, and a generated-app test that a save round-trips without a cycle exception.
- **End to end** — a `build app:` run that compiles and launches the generated WPF app.

## Risks

- **The import is a one-time 1.5-2 hour cost.** Resumability is a requirement, not a nicety.
- **Wikitext stripping is where quality lives.** A bad stripper degrades every consumer
  downstream of it, silently. It gets the most test attention.
- **`PropertyMiner` can mine rubbish.** The gates above, plus the existing review loop, are the
  defence. The no-op contract is what bounds the downside.
- **The store's size is an estimate.** 10-15 GB is projected from the 3.83x ratio, not measured.
  The importer reports actual size on completion.

## Out of scope

- Infobox parsing. Infoboxes describe instances (a specific company), not the class-level
  concepts discovery works with.
- Wikidata. A separate corpus and a separate project.
- Parallel bz2 block decoding. It could cut the import to ~15 minutes on a multicore box, but it
  needs a bit-level block splitter, and the import runs once.
