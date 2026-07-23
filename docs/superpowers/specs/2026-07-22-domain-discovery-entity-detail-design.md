# Domain discovery: extract entities in detail

**Date:** 2026-07-22
**Branch:** `feat/domain-discovery-entity-detail` (off `main` at `99152c4`)
**Status:** approved

## Problem

`build app: <description>` discovers too few classes. The obvious ones are missing:
a hotel booking domain has no `Booking` and no `Room`; a hospital domain has no
`Doctor`, `Nurse`, `Admission` or `Bed`.

They are not missing because discovery never found them. They are found, and then
dropped into an unranked 50-to-100-item suggestion list the user cannot work through.

## Measurement

A probe ran the real pipeline against the full 7.2M-article dump store
(`Dump\enwiki-full.db`), and a second probe classified every candidate by which gate
removed it. Over twelve topic articles (Point of sale, Integrated library system,
Hospital, Hotel, Customer, Payroll, Restaurant, School, Bank, Warehouse, Airline,
Veterinarian):

| | count |
|---|---|
| candidates produced by the two sources | 557 |
| **surviving as classes** | **43** |
| killed by the archetype gate alone | 148 |
| killed by the lead gate alone | 44 |
| killed by both | 290 |
| rejected as abstract (correct) | 32 |

Per-domain worst cases: School 52 candidates → **1** class; Veterinarian 58 → **0**.

The lead gate (`DomainEnricher.DemoteWhatTheDefinitionDoesNotName`) is **not** the
bottleneck — it removes only 44 on its own, and what it removes is genuine noise
(Circumflex, Horse, Bunker, Stagecoach, Jpg). The bottleneck is
`ArchetypeLexicon`, which holds about thirty head nouns; any candidate whose own
Wikipedia definition resolves to a head outside those thirty is demoted to a
suggestion.

Census of the heads that blocked candidates, across the twelve articles:

```
 14 institution   6 facility    5 hotel      4 hospital   4 place    3 structure
 10 system        6 unit        5 specialty  4 school     4 part     3 program
  5 provision     4 airline     4 service    3 bank       2 space    2 account
```

and, separately, heads that are not nouns at all — `primarily(4) and(3) not(3)
or(3) made(3) any(3) humanly(2) undertaken(2) large(2) those(2)` — produced by
`HypernymExtractor` taking the first token after the copula.

Two shapes are visible in that census, and they call for different fixes:

1. **generic concrete heads the table simply lacks** (institution, facility, unit,
   place, part, service, structure, space, account) — curation, but
   domain-independent curation, which is what `ArchetypeLexicon` is already for;
2. **heads that name another candidate in the same run** (hotel, hospital, school,
   airline, bank) — no curation can help here and none is needed: `Motel` is
   defined as "a hotel", and `Hotel` is already shaped.

## Design

Five changes, all inside `DomainDiscovery/Enrichment`. The lead gate is untouched.

### 1. Widen `ArchetypeLexicon` from the census

Grow the head table from ~30 entries to ~150, keyed by the `EntityRole` each head
implies. The entries come from the measured blocking-head census, extended to the
obvious siblings of each measured head — not from imagination about what a domain
might contain.

- **Place** — institution, facility, building, structure, establishment, venue,
  premises, site, room, space, area, department, unit, centre/center, complex,
  warehouse, store, school, hospital, hotel, restaurant, city, location
- **Party** — professional, practitioner, specialist, employee, worker, staff,
  physician, doctor, nurse, teacher, student, pupil, member, user, client, guest,
  passenger, traveller, owner, supplier, vendor, manager
- **Item** — vehicle, aircraft, truck, material, substance, food, beverage, drug,
  medicine, tool, animal, book, part, piece, component
- **Document** — account, licence, certificate, contract, agreement, report, form,
  ticket, prescription
- **Transaction** — loan, sale, purchase, deposit, withdrawal, shipment, delivery
- **Event** (new role, see below) — booking, reservation, appointment, flight,
  trip, journey, visit, admission, stay, treatment, procedure, session, course,
  meeting, conference

`AbstractHeads` grows too, with the abstractions the census turned up: provision,
totality, risk, possibility, chance, phase, stage, experience, competition,
performance, approach, style.

The exact table is built during implementation by re-running the census probe over
a wider article set, so entries are added for heads that were measured to block
something, and each entry can be attributed to that measurement.

### 2. Subtype chaining

When a head resolves `Unknown` but equals (after singularization) the NAME of
another candidate in the same model, the candidate inherits that candidate's
`EntityRole` and archetype fields. (Name only — `ShapeCandidate` carries no source
title, so there is nothing else to match against. This costs little in practice:
a head is always a single lowercase word, while a source title is often
multi-word — "Cash register", "Integrated library system" — so title matching
would rarely fire even if it existed.)

This is what turns `Motel`, `BoutiqueHotel`, `CapsuleHotel` and `ResortHotel` —
all defined as "a hotel" — into Places, given only that `Hotel` is one. It recovers
about 25 candidates across the twelve domains with zero curation, and it compounds
with change 1: once `institution` is in the table, `School` shapes, and every
candidate defined as "a school" chains off it.

Resolution runs to a fixed point (a candidate may chain to one that itself chained)
with a visited-set cycle guard, since `A is a B` / `B is an A` pairs do occur.

### 3. Harden `HypernymExtractor`

The extractor takes the first token after the copula, so "A hotel rating is
*often* a ranking..." yields the head `often`, and "Business travel is travel
*undertaken* for work" yields `undertaken`. It gains a skip rule: adverbs (`-ly`,
plus a small closed list of common non-`-ly` adverbs such as *often*, *not*,
*primarily*) and participial or degree modifiers are stepped over, and scanning
continues to the real head noun. Conjunctions (`and`, `or`) end the phrase rather
than becoming the head.

This is a correctness fix independent of yield: today those candidates are not
merely unresolved, they are resolved *wrongly*, which also means a head that
happens to collide with a table entry could shape a candidate for the wrong reason.

### 4. A word the user typed is evidence

`EntityShaper` currently gives `Provenance.Seed` candidates no exemption, by
explicit design, on the grounds that an extracted seed is a guess. That is why
`build app: a hotel booking application` produces no `Booking` class: the user
typed the word and it was demoted anyway.

The rule changes for one verdict only. A `Seed` candidate whose verdict is
`Unknown` is **kept**, carrying `DefaultFields`, instead of being moved to
suggestions. `Abstract` still rejects it — so `system`, `application` and
`management` do not become classes — and `Shaped` is unaffected.

The reasoning the old comment relied on is sound about Wikipedia candidates and
wrong about typed ones: an `Unknown` verdict means "we have no opinion", and where
we have no opinion, the user having named the thing is the only evidence available.

### 5. New `EntityRole.Event`

Booking, appointment, reservation, flight, visit and admission are the spine of a
scheduling domain and no existing role fits them: they are not `Transaction` (no
amount, no method) and not `Document` (not a record issued to someone).

- `RoleTemplate` for `Event`: `Reference` (string), `StartsOn` (DateTime),
  `EndsOn` (DateTime), `Status` (string).
- Rank **4** in `EntityRole`'s ordering, alongside `Document` and `Transaction`.
  Rank 4 is the "may hold a foreign key" tier, which is exactly right: a booking
  references a Guest (Party, rank 1) and a Room (Place, rank 2). Equal ranks link
  in neither direction, so an Event never acquires an `InvoiceId` on rank alone.

## Components and data flow

Nothing moves between projects and no interface changes shape. The affected units,
each still independently testable:

| Unit | Change |
|---|---|
| `ArchetypeLexicon` | table growth; `Resolve` signature unchanged |
| `EntityShaper` | chaining pass; seed-`Unknown` retention |
| `HypernymExtractor` | modifier-skipping in `Extract` |
| `EntityRole` / `RoleTemplate` | new `Event` member, rank, template |
| `DomainEnricher` | unchanged |
| `LeadSection` | unchanged |

The chaining pass lives in `EntityShaper` because it needs the whole model — the
one place that already sees every candidate at once. It runs between the current
pass 2 (per-candidate verdict) and pass 3 (`Widen`), so a candidate that chains is
widened by its inherited role exactly as a directly-shaped one is.

Because chaining needs to see candidates that pass 2 would otherwise have removed,
pass 2 stops mutating the model as it goes: it records a verdict per candidate,
chaining resolves what it can, and removal to `Rejected` / `Suggestions` happens
once, afterwards. This also fixes a latent ordering dependency — today a candidate's
fate depends on where it sits in the list.

## Error handling

No new failure modes. A missing or unreadable candidate lead already yields an
empty head and an `Unknown` verdict; chaining simply gets one more chance to
resolve it before that verdict is acted on. Cycle guarding is stated above. The
store and client contracts are untouched, so a store that is absent or unindexed
fails exactly as it does now.

## Testing

Unit tests, in the existing `DomainDiscovery.Tests` layout:

- `ArchetypeLexiconTests` — a representative head per role resolves `Shaped` with
  that role; newly-added abstractions resolve `Abstract`; an unmeasured head still
  resolves `Unknown`.
- `HypernymExtractorTests` — the measured failures become the cases: "is often a
  ranking" → `ranking`, "is travel undertaken for work" → `travel`, "is made of" →
  no head, conjunction ends the phrase.
- `EntityShaperTests` — a candidate whose head names another candidate inherits its
  role and fields; a two-step chain resolves; an `A is a B` / `B is an A` cycle
  terminates; a `Seed` candidate with an `Unknown` verdict is kept with `Name`;
  a `Seed` candidate with an `Abstract` verdict is still rejected; verdict outcome
  no longer depends on list order.
- `RoleTemplateTests` / rank tests — `Event` carries its four fields and ranks 4.

End-to-end, against the real store, using the probe built during the
investigation (kept in the scratchpad, not committed): the twelve-domain census is
re-run and compared against the baseline recorded here.

## Success criteria

Measured over the same twelve domains, against the real dump store:

1. Classes rise from **43** to **at least 120**, and
2. no single domain yields fewer than **5** classes (today School yields 1 and
   Veterinarian 0), and
3. the noise names the lead gate currently blocks — Circumflex, Horse, Bunker,
   Jpg, Stagecoach — still do **not** appear as classes, and
4. `build app: a hotel booking application` yields a `Booking` class and a `Room`
   class, and
5. the full test suite stays green under the CI command
   (`SmartCodeGenerator.slnx`, Release, `-warnaserror`).

Criterion 1 is a yield target, not a precision target: the agreed trade-off is that
a visibly wrong class the user deletes with `remove` costs less than a silently
missing one.

## Out of scope

- **The lead gate.** It removes 44 candidates and they are genuine noise. Left
  exactly as it is.
- **Suggestion-list ranking and capping.** Real, but a separate concern; the lists
  shrink as a side effect of this work.
- **Type-inference accuracy** (`InvoiceNumber` → int, `Isbn` → bool). A known,
  separately-tracked axis.
- **`PropertyMiner` field noise** (`WordInvoice`, `TypicallySemiconductorMemoryChips`).
  Field quality, not entity yield.
- **Topic recognition and the supporting-article budget.** `MaxSupportingArticles`
  stays at 3.
