# WikipediaDumpImporter

Builds and maintains the **local Wikipedia store** — the SQLite database that
[`DomainDiscovery`](../DomainDiscovery/README.md) reads instead of calling the
Wikipedia API. It takes the ~25 GB compressed `enwiki` XML dump, strips each
article's wikitext down to prose, and writes the result to
`Dump\enwiki-full.db` (~20 GB, ~7.2M articles).

The store is what makes `build app: <description>` work offline, deterministically,
and without the API's rate limit.

This is a console app (`net10.0`). It is **not** part of the normal build-and-run
loop — you run it once to create the store, and occasionally afterwards to
repair or update it.

## Usage

```bash
dotnet run --project WikipediaDumpImporter -- [dump] [store] [options]
```

Both positional arguments are optional and default to the paths the rest of the
system expects:

| Positional | Default |
| --- | --- |
| `dump` | `D:\SmartCodeGenerator-Repository\Dump\enwiki-latest-pages-articles.xml.bz2` |
| `store` | `DomainDiscoveryPipeline.DefaultDumpDatabasePath` (`D:\SmartCodeGenerator-Repository\Dump\enwiki-full.db`) |

## Modes

With no options, it imports the dump into the store. The options select a
different job instead:

| Option | What it does |
| --- | --- |
| *(none)* | Full import. Hours. Resumable. Builds the search index at the end. |
| `--index-only` | Rebuilds the search index over the store as it stands, without touching the dump. **The repair for a killed import** — see below. |
| `--dump-date=yyyyMMdd` | Records which dump run the store was built from, then continues with the import. |
| `--set-dump-date=yyyyMMdd` | Records the date against an existing store and stops. For a store imported before the date was being recorded. |
| `--update` | Downloads and applies Wikimedia's daily increments to bring the store forward. The expensive path — read the warning below first. |

## What the import does

```
enwiki-*.xml.bz2
      │
      ▼
DumpPageReader.ReadBz2     →  streams RawPage (bz2 decode + XmlReader walk, one thread)
      │
      ▼
ImportRunner.Prepare       →  mainspace only; strip wikitext to prose  (fanned across cores)
      │
      ▼
WikipediaStore             →  batched writes, 2000 rows per transaction
      │
      ▼
CreateSearchIndexes        →  one sorted pass, at the very end
```

Pages that are not mainspace, and articles that strip to nothing (a stub that
was pure template), are counted as skipped rather than stored. Redirects are
stored separately from articles.

### Why it takes hours

The design doc predicted 1.5–2 hours on the assumption that bz2 decode was the
ceiling. Measured on the real file, it isn't: a single-threaded import managed
only ~400 pages/s, some fifteen times below what the decoder alone can do. The
real cost is **per-page wikitext stripping**, which is why `ImportRunner` fans
the strip out across cores and leaves one for the reader.

`AsOrdered()` on that parallel stage is load-bearing, not decoration: the writer
advances the resume point to the last page id it committed, so results must
arrive in dump order or a crash could resume *past* a page that was never
written.

### Resumability, and the index hazard

The run is resumable. Page ids increase monotonically through the dump, so
"skip anything at or below the last committed id" is a correct resume rule, and
each batch commits its rows and its resume point together — a crash can only
ever re-import work, never lose it. A resumed run prints where it picks up.

The **search index is built at the end**, because maintaining the title index
across millions of random inserts dragged the first import from ~790 pages/s
down to ~180. The consequence matters:

> ⚠️ **A killed import leaves the store unindexed, and an unindexed store is
> unusable in practice** — every read becomes a full table scan. Fix it with
> `--index-only`, which rebuilds the index without decoding 25 GB again. The
> same applies to `--index-only` itself: it *drops* the indexes before
> rebuilding them, so killing it mid-pass leaves the store in exactly the state
> it was meant to repair. Let it finish.

## Keeping the store fresh

A full dump is generated over several days, so its content is older than its
filename. The store records the date of the **dump run** (not the download, not
the import) so that anything can tell how far behind it has fallen.

> ⚠️ **Never `--set-dump-date` forward to skip pending increments.** Re-applying
> an increment the store already contains merely overwrites a page with itself;
> skipping one loses an edit for good. Err early.

Two paths bring the store forward, and the cheap one is the default:

- **`ArticleRefresher`** (in `DomainDiscovery`, runs per discovery) refreshes
  the handful of articles a discovery actually reads. This is the normal path.
- **`--update`** downloads Wikimedia's daily `pages-meta-hist-incr` files. Each
  day is ~780 MB and a store is routinely a fortnight behind, so this is
  typically ~10 GB — and most of it is thrown away, because the increments carry
  every namespace and the full revision history of each changed page while this
  store keeps only the latest revision of article space. It prints the cost
  before spending it.

`--update` requires a recorded dump date (there is no "since" without one) and a
reachable Wikimedia. Each day is downloaded, applied, and deleted before the
next is fetched, into an `incr` folder beside the store, so the disk never holds
more than one. The store's date advances only once a day has fully landed, so a
run stopped part-way resumes at the day it was on.

`--update` is not a status command: it is the thing that spends the 10 GB.

## Key types

| Type | Where | Role |
| --- | --- | --- |
| `ImportRunner` | this project | Turns a `RawPage` stream into store rows: strips, filters to mainspace, batches, resumes. Returns `ImportStats`. |
| `DumpPageReader` | `DomainDiscovery` | Streams `RawPage` out of the bz2 dump, lazily — the XML expands to ~97 GB and cannot be materialised. |
| `WikitextStripper` | `DomainDiscovery` | Wikitext → prose. The per-page cost, and so the thing worth parallelising. |
| `WikipediaStore` | `DomainDiscovery` | The SQLite store: schema, batched writes, index create/drop, dump date, resume point. |
| `DumpFreshness` | `DomainDiscovery` | Asks Wikimedia which increments have been published since the store's date. Checks; does not download. |
| `IncrementalUpdater` | `DomainDiscovery` | Applies those increments, a day at a time, committed as it goes. |

## Dependencies

Project reference to [`DomainDiscovery`](../DomainDiscovery/README.md), which
owns every dump type — this project is the console front end that sequences
them. bz2 decoding comes from `SharpZipLib` via that reference.

## Build

```bash
# from the repository root
dotnet build WikipediaDumpImporter/WikipediaDumpImporter.csproj
```

The import logic is covered by [`DomainDiscovery.Tests`](../DomainDiscovery.Tests/README.md)
(`Wikipedia/Dump/`), which uses small in-memory dumps rather than the real file.
