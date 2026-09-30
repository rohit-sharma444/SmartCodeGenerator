# Bundled domains: an offline fallback for `build app:` (Feature 6)

## Why

A customer who installs the tool has no Ollama model and no 18 GB Wikipedia store. Today
`build app: a library that lends books to members` then goes to the online Wikipedia API (slow,
rate-limited, needs a connection) or, offline, yields only the classes named in the description.
The tool should ship with enough built in to give a sensible starting model for common business
systems with nothing installed and no network.

Also in scope: `DomainDiscoveryPipeline.DefaultDumpDatabasePath` is hard-coded to
`D:\SmartCodeGenerator-Repository\Dump\enwiki-full.db`, which exists on no customer's machine,
and the startup warning about a missing store says things the code does not do.

## Decisions (Rohit, 2026-09-30)

- The fallback is **hand-built domains**, not a Wikipedia extract.
- Order when discovering from a description:
  1. Ollama, if it answers
  2. the Wikipedia store, if one is installed
  3. a bundled domain, if one matches
  4. the online Wikipedia API
  5. the classes named in the description itself

## Catalog

One embedded JSON resource per domain in `DomainDiscovery/Bundled/Domains/*.json`. Each is the
JSON shape Ollama already returns (`LlmDomainModel`), plus a name and match terms:

```json
{
  "name": "Library",
  "terms": { "library": 3, "librarian": 3, "book": 1, "lend": 1, "loan": 1, "borrow": 1, "member": 1 },
  "classes": [
    { "name": "Book", "role": "Item", "properties": [ { "name": "Title", "type": "string" }, ... ],
      "relationships": [] },
    { "name": "Loan", "role": "Transaction", "properties": [ ... ],
      "relationships": [ { "target": "Book", "role": "..." }, { "target": "Member", "role": "..." } ] }
  ]
}
```

Reusing the model's shape means reusing `LlmModelNormalizer.Normalize`, which already fixes
names, types and keys, settles relationships by role (Party, Place, Item, Document, Transaction, Event…) and records the decision trail. The only
change there: it takes the provenance and the decision reason as parameters, so a bundled class
says "from the built-in Library domain" in `why`, not "proposed by the model".

The first 25 domains: Library, Clinic, School, Online shop, Point of sale, Invoicing, Inventory,
Hotel, Restaurant, HR, Payroll, CRM, Helpdesk, Projects and tasks, Events, Gym, Car rental,
Property rental, Fleet, Banking, Expenses, Recruitment, Purchasing, Blog, Veterinary. Each has
4–8 classes. Adding a domain is adding a file; nothing else lists them.

## Matching

The description is lower-cased and split into words. Each word is reduced to a simple stem
(plural and verb endings: books→book, lends→lend, borrowing→borrow, enrolments→enrolment). A
domain scores the sum of the weights of its terms that appear. Weight 3 is for words that name
the domain on their own (library, clinic, hotel); weight 1 is for supporting words.

- A domain needs a score of **at least 3** to match, meaning its own name or three supporting words.
  "a book club" scores 1 for Library and does not match.
- The highest score wins. A tie goes to the domain with a weight-3 hit, then to catalog order.
- The outcome message says what matched and why: `Classes from the built-in Library domain
  (matched: library, lends, books).`

## Your own words are kept

A matched domain is merged with the classes the description names (the existing
`SeedEntityExtractor`, including PR #154's transaction verbs). "a library that lends books and
tracks fines" gives the Library domain's classes plus `Fine`. A seed that duplicates a domain
class is dropped. The domain's version wins, because it has fields.

## Where it plugs in

- New `BundledDomainSource : IDomainModelSource`. `Succeeded` is false when nothing matches, so
  the chain moves on.
- `DomainModelSourceFactory.Create` builds the chain in the agreed order. The `wikipedia` factory
  callers pass today splits in two, store and online, because the order puts the bundled domains
  between them. `CreateDefault` stops choosing between them silently.
- Both callers (`BuildAppCommand.HandleFromDescription`, `Web/Workflow/DiscoveryStep`) keep
  calling only the factory.
- A fallback's message still says why the earlier sources were passed over (existing `Annotate`).

## Store location

- New `StorePath` in `discovery.json`. When it is unset, the store is looked for at
  `%LOCALAPPDATA%\SmartCodeGenerator\Wikipedia\enwiki-full.db`.
- One resolver, `WikipediaStoreLocation.Resolve()`, is used by the CLI, the web page,
  `WikipediaDumpImporter` (its default output) and `WikipediaUpdateService`. The importer's
  default input dump moves to the same folder. `DefaultDumpDatabasePath` is removed.
- Your old `D:\SmartCodeGenerator-Repository\Dump` folder is empty. To use a store there again,
  set `StorePath`.

## Startup message

It is rewritten from the real order. With no store installed it says so in one line, and that
`build app:` will use Ollama, then the built-in domains, then online Wikipedia. It no longer
claims "zero classes" or "no fallback".

## Testing

- **Catalog:** every domain file loads and normalises with **zero rejections**. Every
  relationship target exists. No two domains share a weight-3 term.
- **Generated code compiles:** for each domain, the classes it produces are generated and compiled
  with Roslyn.
- **Matching:** one positive description per domain, plus near-misses that must not match
  ("a book club" matches nothing; "a hotel booking system" must pick Hotel even though "book" is a Library word), and ties.
- **Chain order:** fake sources prove each step is tried only when the one before gave no classes,
  and that the message names every skipped source.
- **Live:** with Ollama stopped (pointed at a dead port, per the rule of not stopping his
  services) and the network path unused, `build app: a library that lends books to members`
  reaches Review with the Library classes. Then run `tools/app-check` on two domains.

## Not in scope

Editing the catalog from the app. Fuzzy/semantic matching (the AI index could do this later).
More than 25 domains.
