# Phrase learning: the CLI learns commands from how people retry them

2026-09-29. Agreed in conversation; this records what was built.

## The problem

Scored against the 69 paraphrases in `golden-set.tsv`, the AI command layer gets 16 right, 1 wrong
and declines 52. Nothing recorded what it failed to understand, `LearnedIntentMemory` recorded
outcomes that nothing ever read, and the layer was off by default, so a customer's copy never used it.

## Decisions

- **Learned phrasings ship in every copy, after review.** Nothing a single user types changes
  what another copy understands until a person has approved it and it is committed.
- **The AI layer is on by default.** It cannot act on a reading without asking (`AiConfirmPrompt`),
  and a piped run never uses it. A config that says `Enabled: false`, or cannot be read, keeps it off.
- **The index builds itself** whenever it was not built from exactly the corpus this build ships
  (compared by content hash), so a fresh install works and a new learned phrase reaches old installs.

## The loop

1. **Record.** A command the CLI did not understand, followed within 20 seconds by a working command
   sharing at least one word, becomes a candidate. The words the working command took as arguments
   are dropped, so "whip up a class Invoice" then "create class Invoice" gives
   `whip up a class -> CreateClass`. Stored in `<workspace>\.codegen\phrase-candidates.jsonl`
   (`PhraseCandidates`, fed by `LearningCoordinator`).
2. **Review.** `show learning` lists the candidates grouped and most-seen first, and writes
   `.codegen\phrase-candidates.tsv` in the shipped file's own format.
3. **Ship.** Approved lines go into `CodeIntelligence\Corpus\learned-phrases.tsv`, embedded in the
   assembly and harvested as `CorpusSourceKind.Learned`.
4. **Guard.** `GoldenSetTests.The_learned_phrases_do_not_lower_the_score` fails the build if the
   learned lines cost a correct answer or add a wrong one, and
   `The_golden_set_is_not_drawn_from_the_corpus` refuses a line that copies a golden paraphrase.

## Proof

`PhraseLearningEndToEndTests`: against the real model, "whip up a class Customer" is not understood
by the shipped corpus; after one miss-and-retry with "Invoice" and the resulting line, it is.
The gate was shown to fail on four deliberately wrong lines (16 -> 15 correct, 1 -> 2 wrong).
