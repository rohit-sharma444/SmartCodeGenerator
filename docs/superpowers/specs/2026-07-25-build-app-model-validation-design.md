# Per-model validation classes in `build app:`

**Date:** 2026-07-25
**Status:** Approved (design)
**Branch:** `feat/build-app-model-validators` (off `main` at `28f2e1d`)

## Problem

`build app:` generates model classes but no validation classes. It does emit validation
*declaratively*: `DomainModelMapper.AddValidation` (`AppScaffolding/DomainModelMapper.cs:195`)
infers DataAnnotations attributes onto properties — `[Key]` on `Id`, `[Required]` and
`[MaxLength(200)]` on `Name`/`Title`, `[MaxLength(200)]` on every string, and
`[EmailAddress]`/`[Phone]` by property name.

Those attributes are enforced in exactly two places, both via
`Validator.TryValidateObject`: `ConsoleMenuTemplate.Add` and `ViewModelTemplates`. That leaves
three gaps:

1. **No validation class.** There is nothing to unit-test, nothing to call from library code,
   and nowhere to put a rule that attributes cannot express.
2. **Three write paths validate nothing.** The Web API (`WebApiProgramTemplate.MapEntity`,
   minimal APIs, which perform no automatic model-state check), the worker, and
   `Repository<T>.Create`/`Update` itself all write whatever they are handed.
3. **No home for business rules.** A cross-field or relationship rule has no place to live in a
   generated app.

A separate `generate validator` command already emits a `partial class <Model>` with a
`Validate()` method (`ValidationEngine.EmitValidationPartial`), driven by rules the user types at
the CLI. `build app:` does not use that path at all.

## Decisions

Settled with Rohit during brainstorming, recorded here because each one closes off alternatives:

### Exactly which baseline rules are emitted

"Attribute-equivalent baseline" means one check per rule that
`DomainModelMapper.InferValidation` already records, and nothing more:

| Source on `PropertyDefinition` | Emitted check |
|---|---|
| `Rules` contains `required` | `string.IsNullOrWhiteSpace(item.X)` → `"X is required."` |
| `Rules` contains `maxlength` (`MaxLength = 200`) | `item.X is { Length: > 200 }` → `"X must be 200 characters or fewer."` |
| `Attributes` contains `EmailAddress` | non-empty and contains `@` with at least one character either side |
| `Attributes` contains `Phone` | non-empty and every character is a digit, space, or one of `+-()` |
| `Attributes` contains `Key` | **no check.** The store assigns `Id` in `Create`, so validating it would reject every new record |
| Navigation property (another class, or `List<>`) | no check. Not a string and carries no `Id` name |

The email and phone checks are deliberately loose. They exist to match the behaviour
`[EmailAddress]`/`[Phone]` already give the console and WPF front-ends today, not to be
authoritative format validators.

### Decision table

| # | Decision | Rejected alternative |
|---|---|---|
| 1 | A **separate `<Model>Validator` class** per model, with self-contained checks | A `partial class <Model>.Validate()` matching `generate validator`; or delegating to `Validator.TryValidateObject` |
| 2 | Enforced **in `Repository<T>.Create`/`Update`** | Front-ends only; or emit-but-wire-nothing |
| 3 | Rules: **attribute-equivalent baseline + foreign-key presence** | Numeric sanity and date sanity — both explicitly out of scope |
| 4 | **Generated partial + write-once custom hook** | Write-once whole file; or always overwrite |
| 5 | `Create`/`Update` return **`SaveResult<T>`** | `TryCreate`/`TryUpdate` overloads; or throwing `ValidationException` |

Decision 3 carries a cost Rohit accepted knowingly: the FK check needs to reach the store, so a
validator is not a dependency-free plain object. The `IEntityLookup` seam described below is what
keeps it unit-testable anyway.

## Architecture

A new `Validation/` folder in each generated app, beside the existing `Models/` and `Data/`.

Rules are **derived, not re-inferred.** `DomainModelMapper.AddValidation` already records
`required` and `maxlength` in `PropertyDefinition.Rules` and `EmailAddress`/`Phone` in
`PropertyDefinition.Attributes`. The emitter reads those same structures, so the attributes on a
model and the code in its validator cannot drift apart. Adding a rule to `InferValidation` makes
it appear in both.

Front-end templates are generic over `T` (`EntityMenu<T>`, `MapEntity<T>`, `EntityViewModel<T>`),
so they cannot name `CustomerValidator` directly. Resolution goes through a
`ValidatorFactory.Create<T>()` that mirrors the existing `StoreFactory.Create<T>()`. This is what
keeps the change small: `Repository<T>` resolves its validator in a field initializer next to
`_store`, and no front-end needs per-entity wiring.

## Components

### New: `AppScaffolding/ValidatorTemplate.cs`

Emits into `<App>/Validation/`:

| File | Regenerated | Contents |
|---|---|---|
| `IValidator.cs` | every run | `IValidator<T> { IReadOnlyList<string> Validate(T item); }` |
| `IEntityLookup.cs` | every run | `bool Exists<TEntity>(int id) where TEntity : class, new()` |
| `StoreEntityLookup.cs` | every run | Implements `IEntityLookup` over `StoreFactory.Create<TEntity>()` |
| `ValidatorFactory.cs` | every run | `Create<T>()`, a type-switch over the app's entities |
| `<Model>Validator.Generated.cs` | every run | Inferred rules, plus the `ValidateCustom` hook call |
| `<Model>Validator.Custom.cs` | **written once** | Empty `ValidateCustom` stub for hand-written rules |

`.Generated.cs`, not `.g.cs`: a WPF app's SDK writes its own `*.g.cs` into `obj/`, and reusing
that suffix in the project directory invites confusion.

Shape of a generated validator:

```csharp
// Validation/CustomerValidator.Generated.cs  — regenerated every run
public sealed partial class CustomerValidator : IValidator<Customer>
{
    private readonly IEntityLookup _lookup;

    public CustomerValidator() : this(new StoreEntityLookup()) { }
    public CustomerValidator(IEntityLookup lookup) => _lookup = lookup;

    public IReadOnlyList<string> Validate(Customer item)
    {
        var errors = new List<string>();
        if (item is null) { errors.Add("Customer is null."); return errors; }

        // Data validation  (from PropertyDefinition.Rules / .Attributes)
        try
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                errors.Add("Name is required.");
            if (item.Name is { Length: > 200 })
                errors.Add("Name must be 200 characters or fewer.");
        }
        catch (System.Exception) { }

        // Relationships  (from the shared ForeignKeys helper)
        try
        {
            if (item.RegionId != 0 && !_lookup.Exists<Region>(item.RegionId))
                errors.Add("RegionId does not refer to a known Region.");
        }
        catch (System.Exception) { }

        ValidateCustom(item, errors);
        return errors;
    }

    partial void ValidateCustom(Customer item, List<string> errors);
}
```

```csharp
// Validation/CustomerValidator.Custom.cs  — written once, never overwritten
public sealed partial class CustomerValidator
{
    /// <summary>Hand-written rules. Regeneration never touches this file.</summary>
    partial void ValidateCustom(Customer item, List<string> errors)
    {
    }
}
```

### New: `Data/SaveResult.cs`

```csharp
public sealed class SaveResult<T>
{
    public bool Success { get; init; }
    public bool NotFound { get; init; }
    public T? Item { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public Dictionary<string, string[]> AsDictionary();   // for Results.ValidationProblem
}
```

`NotFound` stays distinct from `Errors` because `Update` already overloads `false` to mean "no
such record", and collapsing "invalid" into that would lose information the Web API needs to
choose between 404 and 400.

`Errors` is a flat `string` list — messages are human-readable sentences, not keyed by property,
matching what `generate validator` already produces. `AsDictionary` therefore returns every
message under a **single empty-string key**, which `Results.ValidationProblem` renders as
top-level errors rather than per-field ones. Keying errors by property name would mean threading a
property name through every rule and is not worth it for this change; if per-field API errors are
wanted later, that is the follow-up.

### Modified

- **`RepositoryTemplate.Base`** — adds `_validator = ValidatorFactory.Create<T>()` beside the
  existing `_store` initializer. `Create` and `Update` validate first and return `SaveResult<T>`.
  `GetAll`/`GetById`/`Delete` are unchanged.
- **`ConsoleMenuTemplate`** — drops `Validator.TryValidateObject`, prints `result.Errors`.
- **`ViewModelTemplates`** — the WPF save path calls `ValidatorFactory.Create<T>()` **directly**.
  `EntityViewModel<T>.Save()` writes through `_store.SaveAll(...)` and never touches
  `Repository<T>`, so repository-level enforcement does not reach it. WPF is the one target that
  needs its own validator call rather than inheriting one.
- **`WebApiProgramTemplate`** — `POST`/`PUT` return `Results.ValidationProblem` on invalid,
  `NotFound` on missing, `Created`/`NoContent` on success.
- **`TestProjectTemplates`** — `RoundTripTests` updated for the new return type; new generated
  `<Model>ValidatorTests.cs` per entity.
- **`AppScaffolder.Scaffold`** — writes the validation files into `Validation/`; the `.Custom.cs`
  files go through an existence guard, reusing the pattern already applied to the `.csproj`.

### Refactor: shared foreign-key detection

`SeedDataTemplate.ForeignKeyTargets`/`ForeignKeyFieldNames`
(`AppScaffolding/SeedDataTemplate.cs:80`) already compute exactly the relationships the
validators need. Lift that logic into a shared `AppScaffolding/ForeignKeys.cs` and have both
`SeedDataTemplate` and `ValidatorTemplate` call it. This is not opportunistic refactoring — a
second copy of the heuristic would drift, and the FK definition ("an `int <Nav>Id` whose sibling
`<Nav>` is one of the app's entity types") must stay identical to the one the console picker and
seeder already use.

## Data flow

```
CandidateDomainModel
  → DomainModelMapper.ToClassDefinitions      (attributes + PropertyDefinition.Rules)
  → ValidatorTemplate                         (reads Rules/Attributes + ForeignKeys)
  → Validation/*.cs
  → ValidatorFactory.Create<T>()              (resolves by type)
  → Repository<T>.Create/Update               (enforces, returns SaveResult<T>)
  → front-end renders result.Errors
```

## Error handling

- The FK rule does I/O and can throw. Every rule block is wrapped in `try`/`catch`, and an
  **unverifiable FK is skipped, not reported as invalid** — the same best-effort stance
  `ValidationEngine.EmitSection` already takes. A store that cannot be read must not manufacture
  validation failures.
- `Validate(null)` returns a single "is null" error rather than throwing.
- A validator with no foreign keys still gets both constructors, so `ValidatorFactory` and the
  generated tests treat every entity uniformly.
- If a user deletes a `.Custom.cs`, the app still compiles: an unimplemented `partial void` and
  its call site are both elided by the compiler. Deleting the stub is therefore a safe way to opt
  out, and a subsequent `build app:` run rewrites it.
- File writes stay best-effort, matching `AppScaffolder.WriteSource`: a validation file that
  cannot be written is counted in the `expected` total, so `ScaffoldResult.Success` reports the
  shortfall instead of claiming a clean build.

## Testing

1. **Emitter unit tests** (`CodeGenerator.Tests`) — a `PropertyDefinition` carrying a rule in,
   the expected check line out; FK targets produce a lookup call; a model with no rules still
   produces a compiling validator.
2. **Roslyn compile tests** — every emitted validator goes through `Compiler.Compile`. Substring
   and `ParseText` assertions do not catch `CS0023`-class defects or literal-injection bugs in
   emitted code; compilation is the assertion. Rules with a string literal (the email message,
   an entity name) are the specific risk.
3. **Integration** — `dotnet test` against a freshly generated app for console, webapi and
   worker, extending the existing generated-output integration test.
4. **Generated-app tests** — the emitted `<Model>ValidatorTests.cs` must pass inside the
   generated `.Tests` project, using a fake `IEntityLookup` so no store is needed.
5. **Gate** — full `SmartCodeGenerator.slnx` in **Release with `-warnaserror`**. The
   `CodeGenerator` Debug build hides analyzer errors and runs only one of the three test
   projects, so a green local Debug run is not evidence.

## Out of scope

- Numeric and date rule families (decision 3).
- The `generate validator` command and its `partial class <Model>.Validate()` output. This is a
  second, independent mechanism; unifying the two is deliberately not attempted here.
- Type-inference accuracy in `TypeInference.Infer`, a separately tracked defect.
- Localisation of error messages.

## Consequences

- **Generated-app behaviour changes.** After this, every write path validates, including the Web
  API and worker, which today write unvalidated. Records that previously saved will now be
  rejected. That is the intent, but it is a behavioural change to output, not a pure addition.
- **Generated signatures change.** `Create` and `Update` change return type. Everything affected
  is generated code, so no hand-written user code breaks — but the templates and the generated
  tests that call them must move in the same commit.
- Regenerating an app whose models gained fields picks up the new inferred rules, while
  hand-written rules in `.Custom.cs` survive.

## Success criteria

1. `build app:` on a discovered domain emits `Validation/` with a validator per model, and the
   app compiles.
2. Every emitted validator compiles under `Compiler.Compile` in the test suite.
3. A record violating a required or max-length rule is rejected by `Repository.Create`, and the
   error surfaces in the console, WPF and Web API front-ends.
4. A non-zero FK pointing at no record is rejected; the same FK is accepted when the target
   exists; an unreadable store rejects nothing.
5. Rules hand-written in `.Custom.cs` survive a second `build app:` run, while
   `.Generated.cs` is refreshed.
6. Full solution green in Release with `-warnaserror`.
