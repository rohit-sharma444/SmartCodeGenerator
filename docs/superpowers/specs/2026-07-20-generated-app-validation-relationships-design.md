# Generated-app validation & relationship surfacing — design

**Date:** 2026-07-20
**Status:** Approved (design)
**Feature:** 1 of 9 (see task list "Feature 1: Emit validation + surface relationships in generated app")

## Goal

`build app: <description>` produces a runnable CRUD app whose model classes already
carry foreign keys and `[JsonIgnore]` navigation properties, but:

1. **No validation.** No property carries any `System.ComponentModel.DataAnnotations`
   attribute, and nothing in the generated app ever validates a record.
2. **Relationships are invisible in the UI.** The console `Add()` prompts for a raw
   `CustomerId (Int32):` integer; the WPF `DataGrid` shows the foreign-key int as an
   editable number and cancels the navigation column entirely. A user editing an
   `Order` has no way to *pick* a `Customer` — they must know its numeric id.

This feature makes validation **real** (attributes **and** enforcement) and makes
relationships **editable** (a picker instead of a raw id) in the two interactive
front-ends (console, WPF).

Non-goals: no change to `CandidateField`, the discovery pipeline, or the NLE; no new
target; WebApi/Worker/ClassLibrary front-ends are unchanged (their model classes still
gain the attributes via the shared core, but no enforcement wiring is added).

## Verified starting state

- `AppScaffolding/AppScaffolder.cs:25` — `WriteClasses` renders every model class through
  `RoslynClassGenerator.Generate(definition, namespaceName)`. Anything set on the
  `ClassDefinition`/`PropertyDefinition` is emitted.
- `CodeGenerationEngine/Core/RoslynClassGenerator.cs:137-182` — renders parameterized
  validation from `PropertyDefinition.Rules` (`required`→`[Required]`,
  `maxlength`→`[StringLength(n)]`, `minlength`→`[MinLength(n)]`) and bare attributes from
  `PropertyDefinition.Attributes` (`Attribute(IdentifierName(a))` — **name only, no
  arguments**). Usings come from `ClassDefinition.Usings` (`:40`).
- `CodeGenerationEngine/Models/PropertyRule.cs` — settable `Name`, `MaxLength`,
  `MinLength` (plus more). Generator switches on `Name.ToLowerInvariant()`.
- `AppScaffolding/DomainModelMapper.cs` — the only place model→`ClassDefinition` mapping
  happens; already sets `Attributes = { "JsonIgnore" }` and adds a using. This is where
  validation attributes are added.
- `AppScaffolding/ConsoleMenuTemplate.cs` — `EntityMenu<T>` is generic/reflection-driven;
  `Add()` prompts a raw value per scalar property (`Editable()` excludes `Id`, audit
  fields, and non-scalars, so the FK int is prompted as a bare number). Only holds its own
  `Repository<T>`.
- `AppScaffolding/ConsoleProgramTemplate.cs` — `Program` constructs a
  `new {name}Repository()` per entity; the place to build a cross-entity resolver.
- `AppScaffolding/MainWindowTemplate.cs:67-89` — `OnAutoGeneratingColumn` code-behind
  already locks `Id` read-only and cancels non-displayable (navigation) columns. This is
  where a foreign-key column is turned into a combo box.
- `AppScaffolding/ViewModelTemplates.cs` — `EntityViewModel<T>.Save()` persists via
  `JsonStore<T>.SaveAll`; the place to enforce validation before writing.

## Part A — Inferred validation attributes (`DomainModelMapper`)

Attributes are inferred deterministically from a property's name and type as the model is
mapped. No new NLP; `CandidateField` is untouched. A class that gains any attribute also
gains `using System.ComponentModel.DataAnnotations;` in `ClassDefinition.Usings`.

Emission channels (both already supported by the generator):
- `[Required]`, `[StringLength(n)]` → `PropertyDefinition.Rules`
  (`new PropertyRule { Name = "required" }`, `new PropertyRule { Name = "maxlength", MaxLength = 200 }`).
- `[Key]`, `[EmailAddress]`, `[Phone]` → `PropertyDefinition.Attributes` (bare, no args).

Rules, applied per property after fields/nav/audit are in place:

| Property archetype | Detection | Attributes emitted |
|---|---|---|
| `Id` | name == `Id` (ordinal-ignore-case), type `int` | `[Key]` |
| Primary name field | name ∈ {`Name`, `Title`} (case-insensitive), type `string` | `[Required]`, `[StringLength(200)]` |
| Email field | type `string`, name contains `email` | `[EmailAddress]`, `[StringLength(200)]` |
| Phone field | type `string`, name contains `phone` | `[Phone]`, `[StringLength(200)]` |
| Other string | type `string`, none of the above | `[StringLength(200)]` |
| Foreign-key int (`<Target>Id`) | handled as a relationship in Part C | *(none — int is already non-nullable)* |
| Audit fields, navigation props | `CreatedOn`/`ModifiedOn`; `[JsonIgnore]` props | *(none — nav props keep `[JsonIgnore]`)* |

`StringLength` default is **200**; the single email/phone/name/other bucket keeps the rule
set small and predictable. Precedence: a string field is classified by the first matching
row top-to-bottom (name field → email → phone → other), so each string gets exactly one
`StringLength` and at most one specialized attribute.

The mapper gains a small pure helper (e.g. `InferValidation(PropertyDefinition, isPrimaryNameField)`)
so the rules are unit-testable without scaffolding a project.

## Part B — Validation enforcement (interactive front-ends)

Attributes are inert unless something runs `Validator`. Both interactive fronts validate
before persisting and surface the failure instead of writing a bad record.

- **Console** (`ConsoleMenuTemplate`, `EntityMenu<T>.Add()`): after populating the new
  item, run `Validator.TryValidateObject(item, new ValidationContext(item), results, validateAllProperties: true)`.
  On failure, print each `ValidationResult.ErrorMessage` and return **without** calling
  `_repository.Create` ("Not added.").
- **WPF** (`ViewModelTemplates`, `EntityViewModel<T>.Save()`): before `SaveAll`, validate
  every record; if any fails, set `Status` to the first error (prefixed with which record)
  and **skip the write**. Add/Delete of in-memory rows is unchanged.

Both use `System.ComponentModel.DataAnnotations`, already referenced by the model classes
and available in the default framework — no package.

WebApi/Worker/ClassLibrary: model attributes present (shared core), no enforcement wiring
added in this feature.

## Part C — Relationship surfacing (interactive front-ends)

A foreign key is an `int` property named `<Target>Id` whose sibling `<Target>` property is
typed as one of the generated entity classes. Both fronts detect it by reflection and let
the user pick an existing target row instead of typing an id.

**Shared display convention** (identical rule, computed by reflection at runtime in each
front): the label for a target row is the value of its display property — the first of
`Name`, `Title`, then the first `string` property that is not an audit field, else `Id`.

- **Console:** `EntityMenu<T>` receives a resolver from `Program` —
  `Func<Type, IReadOnlyList<(int Id, string Label)>>` (or a small registry type) — that
  lists rows for any entity type. `ConsoleProgramTemplate` builds it from the per-entity
  repositories it already constructs and passes it to each `EntityMenu`. In `Add()`, a
  foreign-key property prompts with a numbered list of `(Id — Label)` rows; the user picks
  a number and the id is stored. Empty target list ⇒ message and the field is left at 0.
- **WPF:** `OnAutoGeneratingColumn` detects a foreign-key column and replaces the
  auto-generated text column with a `DataGridComboBoxColumn`:
  `SelectedValuePath = "Id"`, `DisplayMemberPath = <display property>`,
  `SelectedValueBinding = new Binding("<Target>Id")`, `ItemsSource` = the target type's
  live items obtained from the `MainViewModel` tab of that type (reachable via the window's
  `DataContext`). Stays fully generic — no per-entity XAML or code. The navigation column
  itself is still cancelled (unchanged).

If the WPF combo proves brittle at build time, the documented fallback is console-only
relationship UX for this feature, with the WPF combo deferred (flagged, not forced).

## Testing

- **Mapper (unit):** for a representative model, assert the exact rule/attribute set per
  archetype (Id→`[Key]`; `Name`→`[Required]`+`[StringLength(200)]`; an `Email` string→
  `[EmailAddress]`; a plain string→`[StringLength(200)]`; FK/audit/nav→none) and that the
  DataAnnotations using is present exactly when an attribute was added. Test the pure
  `InferValidation` helper directly.
- **Compile:** extend the generated-project build tests so a scaffolded **console** app
  compiles with the new picker + enforcement, and (Windows-only, matching existing WPF
  coverage) a scaffolded **WPF** app compiles with the combo column + `Save` enforcement.
- **Structure:** source-level assertions on the emitted templates — console `Add` contains
  the `Validator.TryValidateObject` call and the foreign-key picker branch; WPF code-behind
  contains the `DataGridComboBoxColumn` branch; `EntityViewModel.Save` contains the
  validate-before-write guard.
- **Enforcement logic:** where the validate-then-reject decision is extractable to a pure
  helper, unit-test it (valid ⇒ proceed; invalid ⇒ messages + no write). Runtime UIA is
  **not** used for WPF — verification is compile + structure, per project convention.

## Files touched (anticipated)

- `AppScaffolding/DomainModelMapper.cs` — inferred validation attributes + DataAnnotations
  using (Part A).
- `AppScaffolding/ConsoleMenuTemplate.cs` — enforcement in `Add` + foreign-key picker
  (Parts B, C).
- `AppScaffolding/ConsoleProgramTemplate.cs` — build and inject the cross-entity resolver
  (Part C).
- `AppScaffolding/ViewModelTemplates.cs` — `Save` enforcement (Part B).
- `AppScaffolding/MainWindowTemplate.cs` — combo column in `OnAutoGeneratingColumn`
  (Part C).
- Tests under `CodeGenerator.Tests` (AppScaffolding lives there for tests): mapper unit
  tests, console/WPF compile tests, template structure tests.

## Backward compatibility

- The shared Models/Data core keeps the same file set and shapes; only attributes are
  added to properties.
- Piped/redirected `build app:` … `approve` is unchanged (no new prompts).
- WebApi/Worker/ClassLibrary outputs change only by the model attributes; no behavioral
  change to their front-ends.
- Every generated target must still compile and `dotnet test` must stay green
  (CI runs `-warnaserror`).
