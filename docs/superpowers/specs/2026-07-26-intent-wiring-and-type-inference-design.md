# Intent wiring and field-type heuristics — design

**Date:** 2026-07-26
**Branch:** `feat/intent-wiring-and-type-inference` (off `main` `cf08631`)
**Status:** approved (amended 2026-07-26 — see "Corrected" note under Problem 1)

## Problem

Two independent defects, bundled because both are small and both are pure
correctness gain over code that already exists.

### 1. Three useful commands cannot be typed at all

**Corrected 2026-07-26, after the first version of this spec.** The original
problem statement said seven intents parse cleanly and then hit `Dispatch`'s
"not supported yet" fallback. That is wrong. They are not recognized in the
first place.

`CommandFront.Parse` (`NaturalLanguageBridge/CommandFront.cs:52-127`) is the
single place user text becomes a `ParsedCommand`, and it resolves intent from
exactly three sources:

1. `LiteralCommandTable` — 8 intents, exact whole-input match;
2. six hard-coded regex guards in `CommandFront` itself — the validation
   commands, `generate validator`, `use project <name>`,
   `classes in <project>`;
3. `NlpCommandMapper.ResolveIntent` → its `(verb, target)` `Table` — 23 intents.

`ResolveIntent` returns a `Table` hit or `IntentType.Unknown` and nothing else:
no learned-intent lookup, no positional fallback beyond the table.

**`IntentRepository` is not in the parse path.** It is initialized at startup
(`CodeGenerator/App/CodeGeneratorStarter.cs:80`) and readable through
`IntentRules`, but the only artifact derived from it is `NlpCommandMapper`'s
hand-transcribed table — `CodeGenerator.Tests/Bridge/IntentCorpus.cs:7`
describes itself as the cross-product of the default rules "restricted to the
22". Its 31 rules do not drive recognition. It is a vestigial store.

None of the three live sources contains `ClassInherit`, `RenameProperty`,
`ValidateProject`, or the four `Create*` architecture intents. So
`validate project` resolves to `Unknown` and the user is told *"Could not
determine intent"*. The existing
`CodeGenerator.Tests/Core/CommandOutcomeTests.cs:54-56` already documents this
for `CreateRepository`.

Consequence for this design: a `Handlers` entry alone accomplishes nothing
user-visible. Each command needs **recognition + extraction + handler +
implementation**.

The three worth building are `ClassInherit`, `RenameProperty` and
`ValidateProject`. The surrounding code for the rest is worse than merely
absent:

- The four architecture generators (`ServiceGenerator.cs`, `RepositoryGenerator.cs`,
  `ControllerGenerator.cs`, `ApiGenerator.cs`) emit unusable output.
  `ServiceGenerator` writes an empty class into a hard-coded
  `namespace Generated.Services`; `RepositoryGenerator` and `ControllerGenerator`
  emit code with **no namespace at all** and with the source file's indentation
  baked in (verbatim interpolated strings). `ApiGenerator` just calls the other two.
- `NaturalLanguageCodeEngine.cs:15-18` routes those four intents into
  `CodeGenerationEngine.Generate`, which handles only `CreateClass` and
  `CreateInterface` and returns the literal string `"// Unsupported command"`
  (`Core/CodeGenerator.cs:19`). That string is non-empty, so
  `GenerateAndSave`'s `IsNullOrWhiteSpace` guard would pass it straight to
  `GeneratedCodeService.Save` and write it to a `.cs` file.
- `ValidateProject` would reach `HandleCompileProject`
  (`NaturalLanguageCodeEngine.cs:106-110`), which returns
  `"Project compilation successful (stub)."` — wiring it as-is ships a lie.
- `RenameProperty` has no implementation anywhere: an enum value and a
  recognition rule, nothing else.
- **None of the seven has entity extraction.** `CommandEntityExtractor.Extract`
  has no `case` for `RenameProperty` (or for the four `Create*`), so there is
  nowhere for a new property name to come from. `ClassInherit` is the exception:
  its case already exists at `CommandEntityExtractor.cs:45`.

Nothing prevents this class of defect from recurring: no test asserts that a
recognizable intent has a handler.

### 2. Two type-inference heuristics with the same bug family

- `DomainDiscovery/Enrichment/TypeInference.Infer` serves the `build app:`
  enrichment path (`PropertyMiner.cs:304`, `ModelEditor.cs:100`).
- `CodeGenerationEngine/Core/CodeGenerator.cs:247` `InferTypeFromName` (private)
  serves the `create class` path via `ResolvePropertyType`.

Both match bare substrings, so both mistype fields:

| Field | Today | Cause |
|---|---|---|
| `Discount` | `int` | `"discount".Contains("count")`, and the `decimal` list lacks `discount` |
| `Isbn`, `Issue`, `Hash` | `bool` | `StartsWith("is")` / `StartsWith("has")` with no boundary |
| `InvoiceNumber` | `int` | `Contains("number")` |
| `Vat` | `DateTime` | `InferTypeFromName` only: `EndsWith("at")` |
| `Fluid` | `Guid` | `InferTypeFromName` only: `Contains("uid")` |

`ResolvePropertyType` also prints two `[CodeGenerationEngine] Heuristic inferred …`
debug lines to the user's console for every property without an explicit type.

## Constraints

- `CodeGenerationEngine` has **zero** project references — it is the leaf.
  `AppScaffolding` depends on it. So a handler in `CommandHandlingService`
  cannot reuse `AppScaffolding.RepositoryTemplate.Typed(...)`; that is circular.
  Only the `CodeGenerator` CLI layer references both.
- `DomainDiscovery` references only `NaturalLanguageEngine`. There is no project
  common to `DomainDiscovery` and `CodeGenerationEngine`, so a single shared
  inference class would require a new project and a new reference from the leaf.
- `CodeGenerator.Tests` references both `DomainDiscovery` and
  `CodeGenerationEngine` — it is the one place both heuristics are visible.
- `Handled` (the handler `bool`) means **success**, not recognition.
  `BatchCommand` rolls back on `false`. `Recognized` is carried separately by
  `PipelineOutcome` and drives the CLI's "did you mean" help.
- Emitted C# is verified by compiling it (`Compiler.Compile`), never by
  substring or `ParseText` assertions: Roslyn's error recovery re-attaches a
  stray member to the class node, so a syntax-tree assertion passes against a
  file that does not compile.

## Decisions

Three commands are made to work end to end — recognition, extraction, handler and
implementation. The four architecture intents are left unrecognized, and the fake
code behind them is deleted.

### Implemented

| Intent | Implementation |
|---|---|
| `ClassInherit` | Existing `ClassInheritanceService.AddInheritance`, after two fixes |
| `RenameProperty` | New `ClassPropertyRenameService` — declaration only |
| `ValidateProject`, `CompileProject` | New `ProjectCompiler.CompileWorkspace` |

**`ClassInherit`.** `ClassInheritanceService` has two latent bugs that wiring
would expose, both fixed here:

1. It takes the **first** `ClassDeclarationSyntax` in the file rather than
   finding the class by name. `ClassMemberEditor` (PR #56) already established
   by-name lookup as the pattern in this repo.
2. It **replaces** the base list via `WithBaseList`, so `class Order : IAuditable`
   silently loses `IAuditable`. It must append, and must be a no-op (reporting so)
   when the base type is already present.

Extraction already exists — `CommandEntityExtractor.cs:45` handles the
`ClassInherit` case and populates `BaseClass` via `ExtractInheritanceInfo`.

**`RenameProperty`.** Scope is the declaration in the named class only, matching
the precedent set by `rename class`, which renames the declaration and the file
and updates no usages (`ClassRenameService.cs`). The success message states
plainly that references were not updated. Requires:

- a `NewPropertyName` field on `ParsedCommand`;
- a `case IntentType.RenameProperty` in `CommandEntityExtractor` with a new
  `ExtractPropertyRenameInfo` covering `rename property <old> to <new> in <Class>`
  and the `on`/`of` variants, resolving the class from `SessionContext` when the
  `in <Class>` clause is absent.

**`ValidateProject`.** New `ProjectCompiler.CompileWorkspace(root)` feeds every
`.cs` file under the resolved root into **one** Roslyn compilation and reports
diagnostics as `file(line,col): error CSxxxx: message`. References come from the
existing `RuntimeReferenceResolver`. The root resolves as
`CurrentProject?.ProjectPath ?? WorkspaceRoot`, mirroring the PR #66 fix, so the
command works with no project selected. `CompileProject` shares the handler: it
is the same operation and costs nothing extra, though it has no recognition rule
and so is not user-reachable today.

Two limits are stated in the command's own output rather than hidden. References
come from the runtime set, not from the project's `PackageReference`s, so a
workspace using EF Core or WPF types will report unresolved-type diagnostics that
`dotnet build` would not. And a workspace of loose generated `.cs` files can
produce a large diagnostic list, so output is capped at the first 20 diagnostics
followed by a total count. `validate project` therefore reports
"compiles against the runtime reference set", not "builds".

Rejected: shelling out to `dotnet build`. Authoritative, but slow, requires a
selected project, and `ProcessRunnerTests` is already a known load-flake in this
suite.

### Recognition

Each of the three needs a recognition source, chosen to match how the existing
code already splits that job.

**`validate project` → `LiteralCommandTable`.** It takes no arguments, so the
exact-match table is exactly right. New entries: `validate project`,
`check project`, `compile project`. `CommandFront:61` returns a `ParsedCommand`
with only `Intent` set for literal hits, which is all this command needs.

**`rename property` and `class inherit` → regex guards in `CommandFront`.** Both
carry arguments, and argument-bearing forms are precisely what the existing
guards exist for — `AddValidationRe`'s comment says the mapper cannot cover them.
`NlpCommandMapper`'s `Table` is keyed on `(verb, target)` where target is the
whole remainder after the verb, so `rename property Sku to Code in Product`
cannot match a `["property"]` target except via NLE's Action-entity tagging,
which this repo's small lexicon makes unreliable.

- `RenamePropertyRe`: `^rename\s+property\s+(?<old>\w+)\s+to\s+(?<new>\w+)(?:\s+(?:in|on|of)\s+(?<class>\w+))?$`
- `ClassInheritRe`: `^(?:make\s+)?(?:class\s+)?(?<class>\w+)\s+(?:inherits?|extends|derives\s+from)\s+(?<base>\w+)$`

Both guards sit after the existing validation guards and before the mapper, and
both delegate field extraction to `CommandEntityExtractor.Extract` so prose
parsing stays in the extractor, as the layering comment on `CommandFront`
requires. `ClassInheritRe` must be anchored whole-sentence and must not begin
with `create`/`generate`, so it cannot steal `create a Customer class that
inherits Entity` from `CreateClass`, which already handles a base class at
generation time.

### Deleted, not wired

`CreateService`, `CreateRepository`, `CreateController` and `CreateApi` get no
handler. They have no recognition source, so a handler — even one printing an
honest pointer — would be unreachable dead code, and adding recognition purely
to print a pointer is not worth the surface. What does happen is that the fake
implementations behind them are deleted, so nobody can wire them up later
believing they work:

- delete `ServiceGenerator.cs`, `RepositoryGenerator.cs`, `ControllerGenerator.cs`,
  `ApiGenerator.cs` — zero callers remain after the switch arm below is removed;
- remove the four intents from the `NaturalLanguageCodeEngine.cs:15-18` switch,
  so `"// Unsupported command"` can never reach a file;
- delete the `HandleCompileProject` stub.

Rejected: rewriting the four generators to emit real code. The useful version of
`create repository Customer` is the one that emits a real CRUD repository, and
that template already exists as `AppScaffolding.RepositoryTemplate.Typed` — which
`CommandHandlingService` cannot reach without inverting the dependency or moving
the handler into the CLI layer. That is a branch of its own.

Rejected: deleting the four intents from `IntentType`. Its values are persisted
as integers by the learned-intent store, so removing a member renumbers the ones
after it and corrupts existing learned data.

### The regression gate

A new test in `CodeGenerator.Tests` asserts that every intent reachable from user
input has a handler. The reachable set is the union of the three live recognition
sources:

- `LiteralCommandTable`'s values,
- the intents produced by `CommandFront`'s regex guards,
- `NlpCommandMapper`'s `Table` values.

Each needs a small public read-only accessor; all three collections are private
today. The gate asserts `CommandHandlingService.IsSupported(intent)` for each.

This set is **already fully handled** before this branch, so the gate is a
regression guard, not a proof of this fix: it fails the build the next time a
command becomes typeable without a handler behind it. That is the invariant worth
holding, and it is the one that was never checked.

Deliberately **not** gated on `IntentRepository.GetAll()`. Those rules do not
drive recognition, so gating on them would demand handlers for intents no user
can type.

### Field-type heuristics

Both implementations are kept — no new project, no new reference from the leaf —
and pinned to one shared conformance corpus so they cannot silently drift again.

`InferTypeFromName` moves out of `Core/CodeGenerator.cs` into its own public
`CodeGenerationEngine/Core/FieldTypeHeuristics.cs`, and the two
`[CodeGenerationEngine] Heuristic inferred …` console lines in
`ResolvePropertyType` are deleted.

Both then match on **tokens** rather than substrings: split the field name on
camel/Pascal boundaries and match whole tokens, case-insensitively, in one shared
rule order:

1. a leading `Is` / `Has` / `Can` / `Should` / `Was` token → `bool`;
2. the name is exactly `Number` → `int`; a **trailing** `Number` token on a
   multi-token name → `string` (see below);
3. scan tokens **right to left** and take the first one found in a vocabulary
   table — the head noun decides. `TotalCost` is a cost, `OrderDate` is a date;
4. no token matches → `string`.

The right-to-left scan is what makes the rule order deterministic when two tokens
disagree, and it agrees with every existing test case: `TotalCost` → `Cost` →
`decimal`, `DueDate` → `Date` → `DateTime`, `StockCount` → `Count` → `int`,
`LateFine` → `Fine` → `decimal`. `HasLateFee` reaches `bool` from rule 1 before
`Fee` → `decimal` can apply.

Token matching removes the false positives. It also drops some true matches, so
the vocabularies grow to compensate: `Discount`, `Rate`, `Tax`, `Vat`,
`Subtotal` → `decimal`.

Two deliberate calls:

- **`<X>Number` → `string`; the bare name `Number` → `int`.** `InvoiceNumber`,
  `PhoneNumber` and `SerialNumber` are identifier codes, not integers. This
  mistypes `PageNumber`; the majority case wins, and correcting it is what the
  review loop is for — as `TypeInference`'s own doc comment says.
- **`*Id` → `int` in both.** `DomainDiscovery`'s copy has no `Id` rule, so mined
  `CustomerId` fields come out `string`. This matters beyond tidiness:
  `AppScaffolding/ForeignKeys.cs:28` requires `p.Type == "int"` to recognize a
  foreign key, so the fix makes FK detection work for mined fields.

The corpus lives in `CodeGenerator.Tests`. The two implementations return
different spellings for the same type (`DateTime` vs `System.DateTime`, `Guid` vs
`System.Guid`), so the corpus compares **normalized** results rather than
pretending the strings are identical.

All 11 existing `DomainDiscovery.Tests/Enrichment/TypeInferenceTests` cases still
pass under the new rules. `HasLateFee` is the one to watch: it needs the
bool-prefix rule to win over the `Fee` → `decimal` token.

## Testing

- `ClassInherit` and `RenameProperty` results are verified by **compiling** the
  edited file with `Compiler.Compile`. Substring and `ParseText` assertions
  cannot see the failures this code produces.
- `ClassInherit` gets a regression test for the dropped-interface bug
  (`class Order : IAuditable` + `Order inherits Entity` keeps both) and one for
  by-name selection in a file containing two class declarations.
- `RenameProperty`: renames in the named class, leaves a same-named property in
  another class alone, fails with a specific message when the class or property
  is absent.
- `ProjectCompiler`: clean workspace, a workspace with a real diagnostic
  (asserting file and line are reported), and an empty workspace.
- A characterization test asserting the corrected premise before anything is
  built on it: `CommandFront.Parse("validate project", "")` and
  `Parse("rename property Sku to Code in Product", "")` both resolve to
  `IntentType.Unknown` today. It is deleted in the same task that adds
  recognition, replaced by the positive assertions.
- Recognition tests per command, asserting `Parse(...)` yields the right intent
  **and** the right extracted fields, plus a guard that
  `create a Customer class that inherits Entity` still resolves to `CreateClass`
  and not to `ClassInherit`.
- The gate test described above.
- The conformance corpus, asserted against both heuristics.
- Every new handler is driven end-to-end through `CommandFront.Parse` →
  `Execute`, not from a hand-built `ParsedCommand`. The PR #56 lesson was that
  tests entering the pipeline downstream of the defect stay green while the
  defect ships.
- A real-CLI probe with piped stdin for `class inherit`, `rename property` and
  `validate project` before the branch is called done — probing the live CLI has
  found defects a green suite hid on three separate branches.
- CI parity before merge: `SmartCodeGenerator/SmartCodeGenerator.slnx`, Release,
  `-warnaserror`.

## Success criteria

1. Every intent reachable from `LiteralCommandTable`, `CommandFront`'s guards or
   `NlpCommandMapper` has a handler; the new gate test proves it.
2. `Order inherits Entity` adds the base type without dropping an existing
   interface, and the result compiles. `create a Customer class that inherits
   Entity` still routes to `CreateClass`.
3. `rename property Sku to Code in Product` renames the declaration in
   `Product` only, and the result compiles.
4. `validate project` reports real Roslyn diagnostics with file and line, and
   reports success only when the workspace compiles against the runtime
   reference set. No output claims a stubbed success.
5. No file containing `// Unsupported command` can be produced by any path, and
   the four fake generators are gone from the tree.
6. `Discount` → `decimal`, `Isbn` → `string`, `InvoiceNumber` → `string`,
   `CustomerId` → `int`, `Vat` → `decimal`, `Fluid` → `string`, in **both**
   heuristics, with the corpus asserting both.
7. No `[CodeGenerationEngine] Heuristic inferred …` line reaches the user's console.
8. Full solution suite green, Release `-warnaserror` clean.

## Out of scope

- Rewriting the four architecture generators to emit real code (needs the
  handler to move to the CLI layer, where `AppScaffolding` is visible), and
  giving those four intents any recognition source.
- Retiring `IntentRepository`. It is initialized at startup and persisted to
  disk while driving nothing, which is worth addressing — but it is a separate
  change with its own blast radius (`IntentRules`, the learned-intent store,
  `IntentCorpus`).
- Full workspace symbol rename for properties (needs `MSBuildWorkspace` or a
  hand-built compilation with resolved references).
- The other unhandled-but-unroutable intents (`DeleteMethod`, `RenameMethod`,
  `RemoveValidation`, `FindUnused*`, `GenerateConstructor`/`ToString`/`Equals`/
  `GetHashCode`/`Crud`, `GenerateComments`, `GenerateRepository`). They have no
  recognition rule, so they are unreachable and the gate does not cover them.
- `list methods of Customer` resolving the class to `"of"`
  (`CommandEntityExtractor.cs:70-73`) — a separate known defect.
