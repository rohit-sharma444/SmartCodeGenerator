# Domain discovery entity detail — measured results

**Date:** 2026-07-22
**Branch:** `feat/domain-discovery-entity-detail`
**Measured against:** the real 20 GB dump store, `Dump\enwiki-full.db` (7.2M articles)

## Result — full pipeline, twelve descriptions

**Total: 160 classes across 12 domains. Worst domain: 7.**

Five of these had a full-pipeline baseline measured during the investigation, so they
are the valid like-for-like comparison:

| Description | Before | After |
|---|---:|---:|
| a point of sale system for a retail shop | 11 | **22** |
| a library management system | 7 | **13** |
| a hospital patient records system | 3 | **17** |
| a hotel booking application | 5 | **22** |
| an online store selling books to customers | 8 | **16** |
| **subtotal (like-for-like)** | **34** | **90** |

The other seven had no pipeline baseline: payroll 7, restaurant 10, school 11, bank 12,
warehouse 12, airline 11, veterinary clinic 7.

## A correction to the success criterion

The spec's criterion 1 read "classes rise from **43** to at least **120**". Those are
measurements by **two different instruments**, and quoting them together would flatter
the result:

- **43** came from a *per-article funnel probe* that reimplemented the shaper's two
  gates outside the pipeline — no seed entities, no supporting articles, no merge.
- The figures above come from the *full pipeline*, `Discover(description)`.

That funnel probe is also now out of date with respect to the shipped code: it models
neither the own-name fallback nor `SubtypeChaining`. Any number it produced now would
understate the result while looking like a like-for-like comparison, so it is not
re-quoted. It did its job — locating the bottleneck — and is not a release metric.

The honest headline is therefore **34 → 90 on the five directly comparable domains**
(2.6×), with 160 across all twelve.

## Verdict on each success criterion

| # | Criterion | Result |
|---|---|---|
| 1 | ≥120 classes (from 43) | **Pass on the pipeline measure (160).** The "43" baseline was a different instrument — see the correction above. Like-for-like: 34 → 90. |
| 2 | No domain below 5 classes | **Pass.** Worst is 7 (payroll, veterinary clinic). Baseline worst was 0 and 1. |
| 3 | Circumflex, Horse, Bunker, Jpg, Stagecoach still not classes | **Pass.** None appears in any of the twelve models. |
| 4 | `a hotel booking application` yields Booking and Room | **Pass**, and better than asked: `Booking[Event]` with real scheduling fields, plus `Room[Place]`, `Stay[Event]` and `Guest[Party]`. |
| 5 | Full suite green under CI parity | **Pass.** `-warnaserror` Release build: 0 warnings, 0 errors. 442 DomainDiscovery tests; full suite green. |

## What the final review changed, and what it cost

The whole-branch review found that `ArchetypeLexicon` was only ever consulted with the
head noun extracted from a candidate's article, and **never with the candidate's own
name**. That was the missing signal. Adding it as a fallback (head first, then own
name, then chaining) took the total from 140 to 160 and — more importantly — turned
thin `Name`-only classes into properly shaped ones:

| | before the fallback | after |
|---|---|---|
| Booking | `Booking[None]` — one `Name` field | `Booking[Event]` — Reference, StartsOn, EndsOn, Status |
| Reservation | `Reservation[None]` | `Reservation[Event]` (8 fields) |
| Record | `Record[None]` | `Record[Document]` |
| Shop / Store / Sale | `[None]`, one field each | `Shop[Place]`, `Store[Place]`, `Sale[Transaction]` |
| Administration | kept as a junk class | correctly **rejected** (abstract) |

An earlier draft of this document argued that `Booking` and `Point` were
indistinguishable — both are Wikipedia disambiguation pages, so neither yields a head
noun — and concluded the seed exemption had to be all-or-nothing. **That conclusion was
wrong**, and it was wrong because it only examined one signal. The candidate's own name
separates them cleanly: `booking` is in the archetype table, `point` is not.

What remains is genuinely irreducible and much smaller: `Point[None]` and
`Application[None]`, one `Name` field each, in the POS and hotel models. Both are words
the user typed, neither has a usable definition, and neither is in the table. They cost
one `remove` each in the review loop, which is the agreed trade.

## Other observations (not defects, not in scope)

- **Topic recognition is now the weakest link.** `a school administration system`
  recognizes *Head teacher* (0.67), which is why that model contains `Head[Item]`.
  `a hospital patient records system` recognizes *Hospital* at 0.25 confidence. The
  spec put topic recognition out of scope and `MaxSupportingArticles` stayed at 3.
  This is the obvious next lever.
- **Some roles are still questionable:** `Retail[Transaction]`, `Retailer[Transaction]`,
  `Purchase[Place]`, `CoachingInn[Item]`. All are visible and removable.
- **Suggestion lists are still long** (14–94 per domain). Explicitly out of scope; they
  did shrink where classes grew (Hotel 53 → 36, Hospital 61 → 38).

## Reproducing

The probe is a throwaway console project in the session scratchpad (deliberately not
committed) referencing `DomainDiscovery.csproj`, with three modes: the default
twelve-description pipeline run, `--funnel <articles...>` for the per-article gate
census, and `--leads <names...>` for per-name lead/head/verdict inspection. Its source
is reproduced in Task 6 of the plan.
