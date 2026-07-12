# Shaping discovered entities without a curated allow-list

**Date:** 2026-07-12
**Branch:** `Seperation`
**Status:** Approved, not yet implemented

## The problem

`build app: develop pointofsale application` proposes four classes — Customer, Payment,
Order, Inventory. A point-of-sale app needs rather more.

The entity harvester surfaces twenty plausible candidates from the Wikipedia article.
`DomainFieldAssigner` then deletes sixteen of them, because `DomainFieldLexicon` — a
hand-curated table of about fifty entities — has no fields for them. Whatever is not
curated does not exist.

(An earlier and much larger loss, a tokenizer bug that swallowed most of the article
before the harvester ever saw it, was fixed in `fdc4a2d`. Object entities went from 56
to 3120 and harvested candidates from 9 to 20. The lexicon gate is what remains.)

## What the evidence rules out

Three strategies for getting field shapes out of the topic article have now been
measured, and all three fail:

1. **"X of Y" and possessive patterns** (PR #25) resolve to adjectives and sentence
   fragments.
2. **Verb-mediated has-a relations** (PR #25) score zero on the Recipe article: the
   subject of its one composition sentence is the relative pronoun "that", which the
   dependency parser cannot resolve back to "recipe".
3. **Definitional is-a sentences** (measured 2026-07-12): searching all 299 sentences of
   the Point of sale article for a definition of any of the twenty candidates — "a cash
   register is a device" — yields **zero hits**.

The third result explains the first two. An encyclopedia article defines *its own title*
and then uses the rest of its vocabulary without defining it. **The topic article cannot
supply per-entity shape.** No further cleverness applied to its prose will change that,
and this design does not try.

Two further measurements shaped the design:

- **Each candidate's own article does define it**, in its first sentence: Customer →
  "recipient of a good", Receipt → "document acknowledging…", Computer → "machine".
  But bare generic nouns — `Sale`, `Order`, `Store`, `Card`, `Point` — land on
  disambiguation pages and yield nothing. Those are exactly the nouns a POS app needs.
  Fetching per-noun summaries also drew HTTP 429 after roughly fourteen rapid requests.
- **The article's wikilinks are the missing knowledge.** One request returns 209
  mainspace link targets, disambiguated by human editors: Cash register, Barcode,
  Receipt, Credit card, Debit card, Payment terminal, Receipt printer, Invoice,
  Merchant, Self-checkout. Prose-noun frequency structurally cannot see these — they are
  multi-word concepts that tokenize into common nouns and normalize to junk.

## The design

### Split the one gate into two questions

`DomainFieldLexicon` currently answers two questions with one lookup: *is this noun a
domain class?* and *what fields does it have?* A miss on the second silently answers "no"
to the first. The design separates them and gives each its own knowledge source.

**Selection — who is a candidate?** Prose-frequency nouns (today's `EntityHarvester`,
unchanged) *unioned with* the topic article's wikilink targets.

**Shaping — what fields?** Strict precedence:

1. `DomainFieldLexicon`, when it knows the name. Curated shapes beat derived ones, and
   every existing shaping test keeps passing.
2. Otherwise the candidate's **own** article's first-sentence hypernym, mapped through a
   domain-independent archetype table. Receipt → "document" → `{Number, IssuedOn, Total}`.
3. Otherwise unshaped.

**The gate falls out of shaping.** An *abstract* hypernym is the drop signal: System →
"group of interacting elements", Business → "practice of making one's living", Data →
"collection of facts". These resolve to no archetype and are therefore not classes. A
domain-independent rule replaces a curated allow-list, which is what lets an arbitrary
topic work with no new curation.

An *abstract* hypernym ("not a class") and an *unknown* hypernym ("we don't know") are
different answers and are treated differently. Silence is not denial.

**Nothing is silently deleted.** The `model.Entities.Remove(entity)` in
`DomainFieldAssigner` goes away. Unshaped candidates become *suggestions* the user can
promote in the review loop.

### Components

| Component | Responsibility |
|---|---|
| `IWikipediaClient.FetchLinks(title)` | `prop=links&plnamespace=0`; cache key `links:{title}` |
| `IWikipediaClient.FetchLead(title)` | `prop=extracts&exintro=1&exsentences=1`; cache key `lead:{title}` |
| `LinkedConceptSource` | 209 links → a handful of candidates |
| `HypernymExtractor` | lead sentence → predicate-nominal head noun |
| `ArchetypeLexicon` | head noun → field shape, or *abstract*, or unknown |
| `EntityShaper` | applies precedence; partitions into shaped / suggested / rejected |
| `CandidateDomainModel.Suggestions` | second tier; `ModelEditor.AddEntity` promotes from it |

Both new client methods follow the existing "degrade to empty, never throw" contract, and
both use new cache keys, so every entry already in `.dd-cache` stays valid.

`LinkedConceptSource` keeps a link only if its title occurs in the article prose at least
twice. That salience test is what separates `Cash register` (everywhere) from `National
Retail Federation` (once). It also drops parenthetical titles (`Gondola (retail)`) and
over-long ones (`Automated sales suppression device`). Names go through the existing
`NameNormalizer`.

`ArchetypeLexicon` is the only curation, and it is domain-independent:

- device / machine / terminal → `{Name, Model, SerialNumber}`
- document / record / statement → `{Number, IssuedOn, Total}`
- person / recipient / buyer → `{FullName, Email, Phone}`
- business / organization / establishment → `{Name, Address}`
- item / product / good → `{Name, Sku, Price}`
- payment / transaction / tender → `{Amount, OccurredOn, Method}`
- card → `{Number, Holder, ExpiresOn}`
- **abstract heads** (concept, practice, field, discipline, group, collection, set,
  process, method, term, study) → rejected

### Data flow

`DomainEnricher.Enrich` gains the link source alongside the prose harvest.
`DomainDiscoveryPipeline` calls `EntityShaper.Shape(model, client)` where it currently
calls `DomainFieldAssigner.Apply(model)`.

Seed entities — the ones the user named — keep today's guarantee: never dropped, never
demoted to a suggestion, `DefaultFields` fallback when the lexicon has nothing.

### What the user sees

```
Candidate model (11 entities):
  - Customer [Wiki] { FullName:string, Email:string, Phone:string }
  - CashRegister [Wiki] { Name:string, Model:string, SerialNumber:string }
  - Receipt [Wiki] { Number:string, IssuedOn:DateTime, Total:decimal }
  ...
Also found - 'add <name>' to include: Merchant, Checkout, Software
Not classes: System, Data, Business
```

Software illustrates the three-way split: its lead ("Software consists of computer
programs…") has no copula, so no hypernym is derived. That is *unknown*, not *abstract* —
it is offered as a suggestion rather than rejected outright.

## Error handling

Every new knowledge source is additive, so any failure degrades to today's behaviour plus
suggestions — never to something worse than what ships now.

| Failure | Behaviour |
|---|---|
| Offline; `FetchLinks` fails | Links contribute nothing; prose harvest unaffected. Shaping falls back to lexicon-only; unshaped candidates become suggestions rather than deletions. |
| `FetchLead` times out or 429s | That candidate is unshaped → tier 2. Others unaffected. |
| Disambiguation lead ("Sale may refer to:") | Detected explicitly; no hypernym → tier 2, rather than a garbage archetype. |
| Topic unrecognised | Unchanged: seed-only path, `DefaultFields` fallback. |

Rate limiting is real — 429 arrived after ~14 rapid requests during measurement. Shaping
fetches one lead per **non-lexicon** candidate, capped by the harvester's existing limit
of twenty, plus one request for links. Requests are serialised with a small delay, and the
disk cache makes every re-run of a topic free. A cold first run is slower; that is the
price of the approach, and it is why the cache matters.

## Testing

TDD throughout. Every unit is a pure function over fixtures; only `HttpWikipediaClient`
touches the network, and tests use a fake `IWikipediaClient`.

- `HypernymExtractor` over the *real* lead sentences measured on 2026-07-12, not invented
  ones: Customer → `recipient`, Receipt → `document`, Restaurant → `business`, Computer →
  `machine`, Database → `collection`, System → `group`, Business → `practice`; plus a
  disambiguation lead → nothing.
- `ArchetypeLexicon`: known head → shape; abstract head → rejected; unknown head →
  neither. The three-way distinction is the thing worth pinning.
- `LinkedConceptSource` against a fixture of the POS article's 209 links and its text:
  `Cash register` and `Receipt` survive; `National Retail Federation`, `Gondola (retail)`
  and `Automated sales suppression device` do not.
- `EntityShaper`: lexicon beats archetype; unknown → `Suggestions`, not deleted; seed
  entities never dropped; abstract → rejected.
- `ModelEditor.AddEntity`: promoting a suggestion preserves provenance and confidence.
- Pipeline integration with a fake client over a POS fixture: tier 1 contains CashRegister
  and Receipt; System, Data, Software and Business are absent.

**End-to-end verification, not merely green tests.** Rebuild the CLI (a solution-level
`dotnet test` leaves `CodeGenerator/bin` stale), run `build app: develop pointofsale
application` against a seeded workspace, and read the candidate model. Success is
CashRegister / Receipt / CreditCard / PaymentTerminal proposed as classes, and System /
Data / Business absent from the proposed classes. Tests passing is not the claim; that
output is.

## Known limitation, accepted

The abstract-hypernym gate removes abstractions, not off-topic concrete nouns. On the
Recipe article, Century and Management are rejected ("period", "administration"), but
**Tablet** resolves to "mobile device" → a Device archetype, and would be proposed as a
class. Salience is the defence and the review loop is the backstop: the user types
`remove Tablet`.

This is the trade accepted when choosing self-shaping over curation — some junk gets
through to be pruned, in exchange for an arbitrary topic working without hand-curation.
Previously such a noun was silently deleted; now it is visible and removable.

Consequently `DomainFieldAssignerTests.Apply_DropsEntities_WhoseShapeIsUnknown` is
rewritten to the new three-way contract. That is a deliberate change of behaviour, not a
regression.
