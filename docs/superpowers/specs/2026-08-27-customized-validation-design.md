# Customized validation from the chat tree — design

**Date:** 2026-08-27
**Status:** approved, not yet implemented
**Branch:** `custom-validation-page`

## Problem

The tool can already turn a sentence into a validation check. `ValidationRuleCatalog`
(`CodeGenerationEngine/Core/Validation/ValidationRuleCatalog.cs`) holds a set of parsers, each of
which recognizes one phrasing and knows how to emit its own C#. `add validation must be between 18
and 65 on Age in Employee` goes through `CommandFront.Parse`, which composes the fragment
`Age must be between 18 and 65`, and the catalog turns it into a `NumericRangeRule`.

Three things stop that from being usable.

**It is only reachable by typing a command in the exact shape.** The rule text has to be a fragment
the parser recognizes, wrapped in a command whose `on <property> in <class>` tail has to be right.
The chat page draws a full Solution Explorer tree of every class in the workspace
(`ProjectTreeView.razor`, `TreeFolderView.razor`), and nothing in it offers validation. The class is
on screen, named, with its properties listed underneath it, and the only way to validate it is to
retype its name into the composer.

**The catalog is narrower than the English people write.** Nine rule types ship. `greater than` and
`less than` are strict-only, so "Age must be at least 18" does not parse as a data rule. There is a
length *range* rule but no standalone bound, so "Name must be at most 50 characters" does not parse.
There is no sign check, no zero check, no substring check, no case check, no date-relative check,
and no arithmetic between properties. All of those are ordinary things to want, and all of them
currently come back as `Could not parse a validation rule from: '...'`.

**The rules never reach the class.** `ValidationRegistry` is a static in-memory dictionary. Rules
live there until `generate validator` writes them out, and `EntityExtractor.GenerateValidator` writes
a sibling file `<Class>.Validation.cs` containing `public partial class <Class>`. `create class`
emits `public class {className}` (`CodeGenerationEngine/Core/CodeGenerator.cs:59`) — no `partial`.
The two halves have never been compiled together: every test in
`CodeGenerator.Tests/Core/Validation/` hand-writes the declaring half as
`namespace Acme { public partial class Booking { ... } }` before compiling. Against a class this
tool actually created, that pair is CS0260 and the project does not build.

## Goal

Right-click a class in the chat page's tree, type the validation in plain English, and have the
check land in that class — in its own file, in a `Validate()` method, next to the checks that are
already there. The catalog is widened first so that the English a person actually types is the
English the tool understands, and the page is framed around what the catalog can do rather than
around a free-text box that quietly fails.

## Scope

**In:**

- Roughly twenty new rule parsers across three tiers: numeric and text bounds, named formats and
  relative dates, and cross-property business rules.
- Two fixes to existing parsers that the new ones would otherwise collide with.
- `IValidationRuleParser.Examples`, so the page's "phrasings I understand" panel and the catalog
  cannot drift apart.
- A writer that splices a check into the class's own source file with Roslyn, creating `Validate()`
  the first time and extending it after.
- A validation page served under the chat host's existing token guard, reached by right-clicking a
  class row.
- Marking a class `partial` when `generate validator` writes its sibling file — see "The adjacent
  defect".

**Deliberately not in scope:**

- **The `<Class>.Validation.cs` route.** The page does not use it, and `generate validator` keeps
  writing it. Two writers, one file each, no shared state. The single change to that command is the
  `partial` fix above, which is what stops its output from failing to compile.
- **Right-clicking anything but a class row.** Property rows stay plain spans. The page lists the
  class's properties, which is what naming one in a sentence needs.
- **Uniqueness and cross-object rules.** "Email must be unique" cannot be checked from inside one
  instance's `Validate()`. It needs the collection, and the collection is not there.
- **The AI layer.** It ships default-off, so a design that leans on it is a design that fails for
  almost everyone. The catalog is the mechanism.
- **Free-text fallback.** A sentence the catalog cannot parse writes nothing at all. See "Refusing".

## Part 1 — the catalog

### The two collisions to fix first

`AllowedValuesRuleParser` is the catch-all at priority 100, matching `<prop> must be <anything>`. It
already refuses a single bare token (`raw.Contains(',')` and the " or " / " and " test), so
"Price must be positive" falls through to no rule at all rather than becoming a wrong one. What it
does swallow is any *conjoined* sentence no specific parser matched:
"Age must be greater than 0 and less than 100" splits on " and " and becomes
`Age must be one of: greater than 0, less than 100` — a check that can never pass, reported as a
success.

The fix is in the **guard**, not the split: a fragment counts as an allowed-values list only when it
carries a comma or an " or ". " and " in a rule sentence nearly always joins two rules, and the
catalog's own splitting already gives the user `;` and newlines for that. The **split** keeps " and "
as a separator, because by then the guard has established the fragment is a list, and
"Draft, Sent and Paid" is how people write one — dropping it there fuses the last two values into
the single literal "Sent and Paid", which is the same class of silently-wrong check in the other
direction.

`ComparisonRuleParser` (business, priority 20) matches `<left> must be at least <right>` where
`right` is `\w+` — which matches digits. Once the data-side `at least` parser exists, a sentence
parsed under the business category turns "Age must be at least 18" into a comparison against a
property called `18`, and emits `// business rule skipped: references an unknown property`. The fix
is to exclude an all-digit operand: `(?<right>(?!\d+$)\w+)`.

### Tier 1 — numbers and text

| Phrasing | Rule | Priority |
|---|---|---|
| `<P> must be at least <n>` · `no less than` · `greater than or equal to` | `MinValueRule` (`>=`) | 35 |
| `<P> must be at most <n>` · `no more than` · `must not exceed` · `less than or equal to` | `MaxValueRule` (`<=`) | 35 |
| `<P> must be positive` · `must be negative` | `SignRule` | 36 |
| `<P> must not be negative` · `must be zero or greater` | `SignRule` | 36 |
| `<P> must not be zero` | `NonZeroRule` | 36 |
| `<P> must be a whole number` · `must be an integer` | `IntegralRule` | 37 |
| `<P> must be numeric` · `must be a number` | `NumericRule` | 37 |
| `<P> must be at least <n> characters` | `MinLengthRule` | 18 |
| `<P> must be at most <n> characters` · `no longer than` · `must not exceed <n> characters` | `MaxLengthRule` | 18 |
| `<P> must be exactly <n> characters` | `ExactLengthRule` | 18 |
| `<P> must start with "X"` · `must end with "X"` | `AffixRule` | 62 |
| `<P> must contain "X"` · `must not contain "X"` | `ContainsRule` | 62 |
| `<P> must be uppercase` · `must be lowercase` | `CaseRule` | 63 |
| `<P> must be digits only` · `letters only` · `alphanumeric` | `CharacterClassRule` | 64 |

The length parsers sit at 18, ahead of `LengthRangeRule` at 20 and well ahead of the numeric bounds
at 35, and the numeric bound patterns anchor on `$` after the number — so the trailing word
`characters` is what keeps "at most 50 characters" out of `MaxValueRule`.

### Tier 2 — formats and dates

The format rules are sugar over `RegexRule`, the way `EmailRuleParser` already is: they parse to a
`RegexRule` with a fixed pattern, and describe themselves as one.

| Phrasing | Emits | Priority |
|---|---|---|
| `<P> must be a valid phone number` | `RegexRule` | 65 |
| `<P> must be a valid URL` | `RegexRule` | 65 |
| `<P> must be a valid postcode` | `RegexRule` (UK) | 65 |
| `<P> must be a valid GUID` | `RegexRule` | 65 |
| `<P> must be a valid IP address` | `RegexRule` | 65 |
| `<P> must be a valid date` | `ValidDateRule` (`DateTime.TryParse`) | 66 |
| `<P> must be in the past` · `must be in the future` | `RelativeDateRule` | 70 |
| `<P> must not be in the past` · `must not be in the future` | `RelativeDateRule` | 70 |
| `<P> must be at least <n> years ago` | `YearsAgoRule` | 71 |
| `<P> must be within the last <n> days` | `WithinDaysRule` | 71 |

The date rules read the property's declared type out of `ValidationEmitContext.Properties`. When it
is `DateTime` or `DateTime?` the emitted check compares the value directly. Only when it is not does
it fall back to `DateTime.TryParse`, and then with `CultureInfo.InvariantCulture` and
`DateTimeStyles.None` — never the loose overload, which reads `03/04/2026` as whichever of March and
April the machine's culture prefers.

### Tier 3 — cross-property business rules

| Phrasing | Rule | Priority |
|---|---|---|
| `Either <A> or <B> must be provided` | `EitherOrRule` | 12 |
| `<A> must equal <B> times <C>` · `plus` · `minus` · `divided by` | `ArithmeticRule` | 15 |
| `<A> must equal <B>` · `must be the same as <B>` | `SameAsRule` | 16 |
| `<A> must be different from <B>` · `must not be the same as <B>` | `SameAsRule` | 16 |

These resolve their operands against `ValidationEmitContext.HasProperty` at emit time, as
`ComparisonRule` and `RequiredWhenRule` already do. Unlike those two, an unresolved operand reaching
the page is a refusal rather than a skipped-rule comment — see "Refusing".

### Examples belong to the parser

`IValidationRuleParser` gains `IReadOnlyList<string> Examples { get; }`. Every parser states the
phrasings it accepts, the page's panel is built by asking the catalog, and there is no second list to
forget to update. This is a breaking change to a public interface with about thirty implementations
after this branch, and it is worth it: a hand-maintained list of accepted phrasings on a page whose
whole job is to tell you what it accepts would be wrong within a month.

## Part 2 — writing into the class

`ClassValidationWriter` (new, `CodeGenerationEngine/Core/Validation/`) takes a class file, a parsed
rule and the class's properties, and returns either the changed source or a refusal.

**Shape of what it writes.** The method the tool owns:

```csharp
    public System.Collections.Generic.List<string> Validate()
    {
        var errors = new System.Collections.Generic.List<string>();

        // Age must be at least 18
        try
        {
            if (decimal.TryParse(((object)this.Age)?.ToString(), …, out var __n))
                if (__n < 18m) errors.Add("Age must be >= 18.");
        }
        catch (System.Exception) { }

        return errors;
    }
```

Each check is one `try` block preceded by the rule's own `Describe()` as a comment. The comment is
load-bearing twice over: it is what a person reads to see what the class validates, and it is what
the writer reads to know a rule is already there. The `try` wrapper and the `__n` local match what
`ValidationEngine.EmitSection` already emits, and because every check has its own block the locals
cannot collide however many rules accumulate.

**How it splices.** Parse the file, find the `ClassDeclarationSyntax` (or `RecordDeclarationSyntax`)
by name, and either insert a new method as the last member or insert the new statements immediately
before the existing `return errors;`. `ValidationEmitContext.Line` writes at a fixed twelve spaces;
since every line it produces shares that base, the block is re-indented by a single delta computed
from the class declaration's own indentation. Nothing else in the file is touched — no whole-file
formatting pass, which would reformat code the user wrote.

**When it refuses, and writes nothing:**

| Condition | Why |
|---|---|
| The named property is not on the class | The defect shape this project keeps hitting: a writer that skips a property whose name is present, reports success, and changes nothing. |
| A `Validate()` exists that is not this shape — different return type, expression-bodied, or no `errors` local | It is the user's method. The tool does not edit it. |
| The class is a positional record with no body | Turning `record Foo(int A);` into a braced body is a larger edit than this branch should make. |
| The rule is already present (same `Describe()`) | Reported as already there. |
| The file resolves outside the workspace root | The page takes a path in its query string. See "Part 3". |
| The changed file does not parse, or `Compiler.Compile` reports an error | Roll back; write nothing. |

The last row is the one that makes this safe to run against a file someone wrote by hand: the writer
compiles what it produced before it puts it on disk, and a failure leaves the file exactly as it was.

## Part 3 — the page

**The route sits under the guard.** `LoopbackHost.TryStart` takes one route segment and 404s any
request to it without the session token; `ChatWebHost` passes `"chat"`. The check is
`Path.StartsWithSegments("/chat")`, so a page at **`/chat/validation`** is covered and a page at
`/validation` would be served, unguarded, to every process on the machine — and this page writes
source files. The route is `/chat/validation`, and the token is carried forward from the current
URL's `t` parameter when the chat page navigates.

**The target travels in the query.** `?t=<token>&class=<Name>&file=<url-encoded path>`. The file
rather than the name alone, because two projects in one solution can both declare a `Customer` —
the same reason the tree already keys its open classes by path. Before anything is written the path
is resolved and checked to be under `Workspace.Root`.

**Right-click.** Class rows in `TreeFolderView.razor` get `@oncontextmenu` with
`@oncontextmenu:preventDefault="true"` and raise the class up to `ChatPage`, which shows a one-item
menu at the pointer — *Add validation…* — and navigates on click. One item, because one is what
there is; a menu rather than navigating straight off the right-click, because a right-click that
moves the page without asking is a right-click people learn to avoid.

**What the page shows.**

- The class, and the file it lives in.
- Its properties, from `EntityExtractor.ExtractProperties` — the names a sentence can refer to.
- The rules already on the class, read back from the `// …` comments in `Validate()`.
- A textarea, and an **Add validation** button.
- A collapsible panel of accepted phrasings, built from `IValidationRuleParser.Examples`.
- A link back to the chat.

**What happens on submit.** The text goes through `ValidationRuleCatalog.Parse` under the data
category first and the business category second — the categories are tried, never chosen by the
user, because the rule's own parser is what knows which it is. A parsed rule is registered in
`ValidationRegistry` as well as written, so `show validation <Class>` in the same session agrees with
the file. The page stays where it is after a successful add, with the new rule now listed, so a
second rule does not mean navigating back and right-clicking again. A turn is appended to the chat
transcript recording what was added and to which file, so the conversation remains the record of
what the tool did.

**Refusing.** A sentence no parser matches is reported on the page, with the class's properties and
the accepted phrasings beside it, and nothing is written. So is a rule naming a property the class
does not have. The page never reports an addition that did not happen — the failure this project has
now hit repeatedly is a green tick over an unchanged file.

## The adjacent defect

`generate validator` writes `public partial class <Class>` beside a class that `create class` wrote
as `public class <Class>`. That is CS0260, and no test catches it because every test supplies the
declaring half itself, already marked `partial`.

Since this branch is building a Roslyn writer that edits a class declaration in place, the fix is a
few lines on top of it: when `GenerateValidator` writes the sibling file, add `partial` to the class
declaration if it is missing. It is in scope, it is separately committed, and it is called out here
because it is a behaviour change to an existing command that nobody asked for — it is included
because the alternative is a command that reports "Validator generated" over a project that no
longer builds.

## Testing

- **Per new parser:** it parses its phrasings, it rejects near-misses, and `Describe()` round-trips.
- **Per new rule:** the emitted C# compiles, through `Compiler.Compile` against a class declaring the
  property — never a substring assertion. This project has shipped emitted code that passed
  substring tests and did not compile.
- **The two collision fixes:** red-then-green. "Age must be greater than 0 and less than 100" must
  stop producing an allowed-values rule; "Age must be at least 18" must stop producing a business
  comparison against a property called `18`.
- **The writer:** creates `Validate()`; extends it; refuses each row of the refusal table; detects
  duplicates; and leaves every line it did not add byte-identical.
- **End to end:** a class written by `create class`, a rule added through the writer, and the whole
  file compiled.
- **The CLI path:** the existing `AddValidationPhrasingTests` shape, extended, so the new phrasings
  work from `add validation …` too. They are the same catalog; a page-only test would not prove it.
- **The page:** the pieces that are not the UI — target parsing, the workspace-root guard, the
  refusal messages — are unit-tested. The page itself is verified by running it: `chat` refuses
  piped and argv runs, so the route is opened by a throwaway MSTest probe that starts `ChatWebHost`,
  and looked at. A right-click menu, a render mode and a navigation that carries a token are none of
  them things a bUnit test can see.

CI gives no verdict on this repo — every GitHub Actions run since 2026-08-25 dies in seconds against
an account spending limit — so a local `dotnet test` across all test projects is the verdict.

## Risks

**The catalog gets big.** About twenty new parsers, each with a regex, is a lot of surface for
phrasings to overlap on. The priority column above is the whole defence, and the collision tests are
what hold it: every new parser gets a test that the sentences belonging to its neighbours do *not*
match it.

**The writer edits files the user wrote.** Compile-before-write and rollback are what make that
acceptable, plus the byte-identical assertion on untouched lines. The refusal table is deliberately
wide: anything that is not obviously the tool's own method is left alone.

**The page cannot be unit-tested into correctness.** Four defects on the Blazor chat branch compiled
clean and appeared only in a running page, the worst being a missing `@rendermode`. The page is
looked at before it is called done.
