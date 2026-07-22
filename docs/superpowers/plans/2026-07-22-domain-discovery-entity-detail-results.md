# Domain discovery entity detail — measured results

**Date:** 2026-07-22
**Branch:** `feat/domain-discovery-entity-detail`
**Measured against:** the real 20 GB dump store, `Dump\enwiki-full.db` (7.2M articles)

## A correction to the success criterion first

The spec's criterion 1 read "classes rise from **43** to at least **120**". Those two
numbers turn out to be measurements by **different instruments**, and comparing them
directly would flatter the result:

- **43** came from a *per-article funnel probe*: for one topic article, how many of its
  candidates survive both gates. No seed entities, no supporting articles, no merge.
- **140** (below) comes from the *full pipeline*: `Discover(description)`, which also
  reads supporting articles and includes the user's own seed nouns.

Both numbers are reported below, each against its own baseline. The pipeline number is
the one that reflects what `build app:` actually puts in front of a user.

## Result 1 — full pipeline, the twelve descriptions

**Total: 140 classes across 12 domains. Worst domain: 5.**

Five of these had a full-pipeline baseline measured during the investigation, so they
are directly comparable:

| Description | Before | After |
|---|---:|---:|
| a point of sale system for a retail shop | 11 | **19** |
| a library management system | 7 | **12** |
| a hospital patient records system | 3 | **15** |
| a hotel booking application | 5 | **20** |
| an online store selling books to customers | 8 | **13** |
| **subtotal (like-for-like)** | **34** | **79** |

The other seven had no pipeline baseline: payroll 6, restaurant 8, school 11, bank 12,
warehouse 9, airline 10, veterinary clinic 5.

## Result 2 — per-article funnel, the same instrument as the 43

| | Before | After |
|---|---:|---:|
| candidates produced | 557 | 558 |
| **surviving as classes** | **43** | **87** |
| killed by the archetype gate alone | 148 | 87 |
| killed by the lead gate alone | 44 | 102 |
| killed by both | 290 | 232 |
| rejected as abstract | 32 | 50 |

The archetype gate's solo kills fell by 41%, which was the point. The lead gate's solo
kills *rose* (44 → 102) — not a regression: candidates that used to die at the archetype
gate now survive it and meet the lead gate instead. The lead gate itself is unchanged.

## Verdict on each success criterion

| # | Criterion | Result |
|---|---|---|
| 1 | ≥120 classes (from 43) | **Pass on the pipeline measure (140); 87 on the funnel measure.** See the correction above — the criterion mixed instruments. On the funnel instrument the honest figure is 87, a 2.0× rise, not ≥120. |
| 2 | No domain below 5 classes | **Pass.** Worst is 5 (veterinary clinic). Baseline worst was 0 (Veterinarian) and 1 (School). |
| 3 | Circumflex, Horse, Bunker, Jpg, Stagecoach still not classes | **Pass.** None appears in any of the twelve models. |
| 4 | `a hotel booking application` yields Booking and Room | **Pass.** Both present. |
| 5 | Full suite green under CI parity | **Pass.** `-warnaserror` Release build: 0 warnings, 0 errors. Tests: 228 + 434 + 414 = **1076 passed, 0 failed**. |

## The measured trade-off, stated plainly

The seed-evidence rule (a noun the user typed, with an `Unknown` verdict, is kept) is
what produces the `Booking` class. It also produces empty, `Name`-only classes for
`Point`, `Sale`, `Shop`, `Application`, `Store` and `Administration`.

**These cannot be separated.** Probing each seed's article:

```
Point          disambig=True   head=''  Unknown  | Point or points may refer to:
Shop           disambig=True   head=''  Unknown  | Shop may refer to:
Application    disambig=True   head=''  Unknown  | Application may refer to:
Booking        disambig=True   head=''  Unknown  | Booking may refer to:
Room           disambig=False  head='structure'  Shaped
```

`Booking` — the class the whole change exists to produce — is itself a disambiguation
page, carrying byte-for-byte the same signal as `Point` and `Application`. A
"reject disambiguation seeds" rule would drop `Booking` along with the junk. So the
exemption is all-or-nothing on this axis, and it was taken deliberately: the agreed
trade is that a visible spurious class costs one `remove` in the review loop, while a
silently missing `Booking` costs the user a class they explicitly asked for.

Worth noting `Room` needed no exemption — it shapes properly, via the widened table.

## Other observations (not defects, not in scope)

- **Topic recognition is now the weakest link.** `a school administration system`
  recognized *Head teacher* (0.67), which is why that model contains `Head[Item]`.
  `a hospital patient records system` still recognizes *Hospital* at 0.25 confidence.
  The spec put topic recognition out of scope and `MaxSupportingArticles` stayed at 3;
  this is the obvious next lever.
- **Some roles are questionable:** `Retail[Transaction]`, `Retailer[Transaction]`,
  `CoachingInn[Item]`, `Kitchen[Item]`. They come from heads resolving via chaining or
  via a table entry that is right more often than not. All are visible and removable.
- **Suggestion lists are still long** (16–98 per domain). Explicitly out of scope; they
  did shrink where classes grew (Hotel 53 → 38, Hospital 61 → 40).

## Reproducing

The probe is a throwaway console project in the session scratchpad (deliberately not
committed) referencing `DomainDiscovery.csproj`, with three modes: the default
twelve-description pipeline run, `--funnel <articles...>` for the per-article gate
census, and `--leads <names...>` for per-name lead/head/verdict inspection. Its source
is reproduced in Task 6 of the plan.
