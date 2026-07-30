# A name the user types keeps the shape they typed

## The problem

`add CashRegister` creates an entity called `Cashregister`, and the review loop hides it:

```
> add CashRegister
Added 'CashRegister'.          <- echoes what was typed
  - Cashregister [Seed] ...    <- what the model actually got
```

`ModelEditor.AddEntity` normalizes with `NameNormalizer.ToEntityName`, which reduces a phrase to
its head noun and rebuilds it as `char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()`.
That is right for prose — "library member" should become `Member` — but a name the user typed is
not prose. It is already the class name they want, and lowercasing everything after its first
character destroys the compound.

Two other faults share the cause:

- `add Cash register` silently creates `Cash`. The review loop parses with
  `line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries)` and passes only `parts[1]`, so the
  rest of the name is dropped without a word. This is the same split trap `WhyLines` already
  solves for `why`.
- `rename <old> <new>` flattens the new name identically, via the same normalizer.

## Why it matters now

Discovery's new `why` verb prints an instruction:

```
> why FairTrade
FairTrade
  shaping          rejected   its definition resolves to an abstraction
  Add it anyway with: add Fairtrade
```

The bug predates that verb — `ModelEditor` is untouched by the decision-analysis branch — but
`why` is the first thing that tells a user to run `add <name>`, so it is now reachable by
following our own advice.

## The coupling, which is the whole point

`DecisionReport.ForName` prints its suggestion via `NameNormalizer.ToCompoundEntityName`, which
splits on *the non-alphanumerics present in its input*. For a separator-free PascalCase token
that separator set is empty, so the whole token is one "word" and is lowercased after its first
character — `FairTrade` becomes `Fairtrade`.

Today that flattening happens on both sides, so the printed advice and its result agree
(`add Fairtrade` really does yield `Fairtrade`). **Fixing either side alone creates a mismatch
instead of removing one:**

| Change | `why` prints | `add` produces | Agree? |
| --- | --- | --- | --- |
| Neither (today) | `add Fairtrade` | `Fairtrade` | yes, but neither matches the displayed `FairTrade` |
| `ForName` only | `add FairTrade` | `Fairtrade` | **no** |
| `AddEntity` only | `add Fairtrade` | `Fairtrade` | yes, but `why` still advises the flattened name |
| Both | `add FairTrade` | `FairTrade` | yes, and matches what is displayed |

So both halves land together, in one branch.

## Design

### `NameNormalizer.ToUserTypedEntityName`

A third sibling to `ToEntityName` (prose → head noun) and `ToCompoundEntityName` (wikilink title →
compound), for the one path where the text came from a person.

1. Split on non-alphanumerics **and** on PascalCase boundaries — an uppercase letter that follows
   a lowercase letter or digit starts a new segment. An uppercase letter following another
   uppercase letter does **not**, so a run of capitals survives intact: `CashREGISTER` splits to
   `Cash` + `REGISTER`, and `ID` stays `ID`.
2. Capitalize each segment's first character; leave the rest exactly as typed.
3. Singularize the last segment only, preserving its case.

| Input | Output |
| --- | --- |
| `CashRegister` | `CashRegister` |
| `cash register` | `CashRegister` |
| `Books` | `Book` |
| `BookCategories` | `BookCategory` |
| `widget` | `Widget` |
| `2ndthing` | `2ndthing` (then rejected by `IsValidIdentifier`, as today) |

**Case-preserving singularization.** `Singularize` operates in lowercase, so its result cannot
simply be pasted over the original. Where the singular is a prefix of the lowercased segment,
truncate the original to that length (`Books` → `Book`). The `ies → y` rule is the one case that
is not a prefix, so keep the original's leading characters and append the final `y`
(`Categories` → `Categor` + `y`).

### What changes, and what deliberately does not

`ToEntityName` keeps its behavior and all ~10 call sites — `SeedEntityExtractor`,
`EntityHarvester`, `ArchetypeLexicon`, `HypernymExtractor`, `SubtypeChaining`,
`WikipediaTopicRecognizer` and `ModelEditor`'s own lookups. Discovery is not touched. Only the two
places that mint a name from user input change:

- `ModelEditor.AddEntity`
- `ModelEditor.RenameEntity` (the new name; the old name stays a lookup)

**Why the lookups are safe.** `ModelEditor.Find` compares `OrdinalIgnoreCase`, and for a
separator-free token the new normalizer differs from `ToEntityName` only in *case*. So an entity
created as `CashRegister` is still found by `remove CashRegister`, `field CashRegister Price` and
`rename CashRegister Till`, all of which normalize the typed name to `Cashregister` first.

### Reporting the name that was actually created

`AddEntity` gains an overload with `out string? addedName`; the existing two-argument signature
delegates to it, so no existing caller or test changes. The out-parameter is necessary rather
than cosmetic: promoting a suggestion keeps the *suggestion's* own name, so the created name is
not always what the normalizer returned. `BuildAppCommand` reports that value instead of echoing
`parts[1]`.

### The review loop

- `add` joins `parts.Skip(1)` before calling, exactly as `WhyLines` does, so `add Cash register`
  reaches the normalizer whole.
- `rename` already receives its new name intact — `Split(' ', 3)` puts the remainder in `parts[2]`
  — so it needs no rejoining, only the new normalizer.
- `DecisionReport.ForName` uses `ToUserTypedEntityName` for its fallback, so its printed command
  round-trips.

## Success criteria

1. `add CashRegister` creates `CashRegister`, and the confirmation names what was created.
2. `add Cash register` creates `CashRegister`, not `Cash`.
3. `add Books` still creates `Book` — singularization is preserved.
4. `rename Member CashRegister` renames to `CashRegister`.
5. **Round trip:** for any name `why` prints an `add` command for, running that command produces
   an entity with exactly that name. This is the invariant the branch exists to establish.
6. `remove`, `field` and `rename` still resolve an entity created with interior capitals.
7. Discovery output is unchanged: `ToEntityName` and every pipeline behavior are untouched.
8. Release `-warnaserror` clean and the whole suite green.

## Testing notes

- `ForName_OffersTheActionThatTakesARejectedOrSuggestedNameBack` currently uses a `"Cash register"`
  record. The whole-branch review on PR #69 established that a `Rejected`/`Suggested` record can
  never hold that spelling in production — seed and harvested names come from `ToEntityName`,
  wikilink names from `ToCompoundEntityName`, and none contain a separator. So that test exercises
  a branch which never fires while the branch that does fire is untested. Re-point it at a record
  spelling the pipeline can actually produce, and keep a separate case for separator-bearing input.
- The round-trip criterion deserves a test that composes the two halves rather than asserting each
  in isolation, since agreement between them is the actual requirement.
