# Intent wiring and field-type heuristics — design

**Date:** 2026-07-26
**Branch:** `feat/intent-wiring-and-type-inference` (off `main` `cf08631`)
**Status:** approved

## Problem

Two independent defects, bundled because both are small and both are pure
correctness gain over code that already exists.

### 1. Seven commands parse successfully, then dead-end

`IntentRepository` holds recognition rules for 31 intents.
`CommandHandlingService.Handlers` (`CodeGenerationEngine/Service/CommandHandlingService.cs:66-102`)
covers 24 of them. The remaining seven parse cleanly and then hit `Dispatch`'s
fallback at `:117`, which prints `Intent '<X>' is not supported yet.`:

`ClassInherit`, `CreateService`, `CreateRepository`, `CreateController`,
`CreateApi`, `RenameProperty`, `ValidateProject`

The surrounding code is worse than merely absent:

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
- **None of the seven have entity extraction.** `CommandEntityExtractor.Extract`
  has no `case` for any of them, so `ClassName` falls back to
  `SessionContext.CurrentClass` and there is nowhere for a service name,
  controller name, or new property name to come from.

Nothing prevents this class of defect from recurring: no test asserts that a
routable intent has a handler.

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

Three intents are implemented properly; four are made honest.

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

### Made honest

`CreateService`, `CreateRepository`, `CreateController` and `CreateApi` share one
handler that prints a specific pointer and returns `false`:

```
'create repository Customer' isn't implemented.
Use 'build app: <description>' — it generates real per-entity repositories,
controllers and an API host.
```

`false` is correct: the command did not do the thing, so `BatchCommand` must not
count it as applied. `Recognized` stays `true`, so the CLI does not additionally
print "did you mean".

The fake paths are then deleted so they cannot be re-wired by accident:

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

Rejected: deleting the four intents from `IntentType` and `IntentRepository`.
`IntentType` values are persisted as integers by the learned-intent store, and a
recognized command answering with a pointer is more useful than an unrecognized
one.

### The regression gate

A new test in `CodeGenerator.Tests` takes the union of

- intents with a rule in `IntentRepository.GetAll()` (already public), and
- intents in `NlpCommandMapper`'s verb/target table (private today — needs a
  small public read-only accessor),

and asserts `CommandHandlingService.IsSupported(intent)` for each. After this
branch that set is fully covered, so any future intent that gains a recognition
rule without a handler fails CI. All 23 of `NlpCommandMapper`'s intents are
already handled; the gate's only current violations are the seven above.

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
- The four pointer handlers: assert the message names `build app:` and that the
  handler returns `false`.
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

1. No intent with a recognition rule in `IntentRepository` or `NlpCommandMapper`
   prints "not supported yet"; the new gate test proves it.
2. `create <X> class inherit <Y>` adds the base type without dropping an existing
   interface, and the result compiles.
3. `rename property Old to New in Customer` renames the declaration in
   `Customer` only, and the result compiles.
4. `validate project` reports real Roslyn diagnostics with file and line, and
   reports success only when the workspace compiles against the runtime
   reference set. No output claims a stubbed success.
5. `create repository Customer` names `build app:` and returns `false`; no file
   containing `// Unsupported command` can be produced by any path.
6. `Discount` → `decimal`, `Isbn` → `string`, `InvoiceNumber` → `string`,
   `CustomerId` → `int`, `Vat` → `decimal`, `Fluid` → `string`, in **both**
   heuristics, with the corpus asserting both.
7. No `[CodeGenerationEngine] Heuristic inferred …` line reaches the user's console.
8. Full solution suite green, Release `-warnaserror` clean.

## Out of scope

- Rewriting the four architecture generators to emit real code (needs the
  handler to move to the CLI layer, where `AppScaffolding` is visible).
- Full workspace symbol rename for properties (needs `MSBuildWorkspace` or a
  hand-built compilation with resolved references).
- The other unhandled-but-unroutable intents (`DeleteMethod`, `RenameMethod`,
  `RemoveValidation`, `FindUnused*`, `GenerateConstructor`/`ToString`/`Equals`/
  `GetHashCode`/`Crud`, `GenerateComments`, `GenerateRepository`). They have no
  recognition rule, so they are unreachable and the gate does not cover them.
- `list methods of Customer` resolving the class to `"of"`
  (`CommandEntityExtractor.cs:70-73`) — a separate known defect.
