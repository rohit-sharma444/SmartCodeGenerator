# Data Validation & Business Validation commands — design

**Date:** 2026-07-22
**Status:** Approved (brainstorming) — pending implementation plan

## Objective

Provide two independent commands in the Code Generator:

1. **Add Validation** (Data Validation) — field-level, single-property constraints.
2. **Add Business Validation** (Business Rules) — entity-level rules that span
   multiple fields or express domain logic.

The design must be **extensible**: a new validation type can be added without
changing any existing code (Open/Closed).

## Decisions locked during brainstorming

- **Boundary:** Data Validation is *field-level* (required, length, range, regex,
  allowed values, type). Business Validation is *entity-level* (cross-field /
  domain logic, e.g. `EndDate must be after StartDate`, `Discount must not exceed
  Price`).
- **Deliverable:** both commands **register rules now** (in the existing in-memory
  registry) and the existing `generate validator` command is **extended to emit
  both** data and business checks. Neither command touches the class file on disk
  by itself.
- **Extensibility scope:** a **single shared rule-type registry**. The existing
  data-validation checks are **migrated out of the if-chain** into registered rule
  types. Both commands and `generate validator` route through the registry.

## Current state (as found)

- Commands dispatch by `IntentType` → handler in
  `CodeGenerationEngine/Service/CommandHandlingService.cs` (`Handlers`
  dictionary). Intents are resolved in `NaturalLanguageBridge`:
  `CommandFront.Parse` → `LiteralCommandTable` (exact match) → regex guards → the
  NLE mapper → `CommandEntityExtractor` (field extraction).
- **Data validation already partly exists:**
  - `add validation <rule> on <property> in <class>` → intent `AddValidation`.
  - `CommandEntityExtractor.ExtractValidationInfo` reshapes the phrase into
    `"<Property> is <rule>"` and sets `ValidationText`.
  - `HandleAddValidation` calls `ValidationRuleParser.Parse` (a regex chain that
    produces `PropertyRule` objects) and stores them in the static
    `ValidationRegistry` keyed by class name.
  - `show validation` (`ShowValidation`) prints per-`PropertyRule` flags.
  - `generate validator` (`EntityExtractor.GenerateValidator`) pulls
    `ValidationRegistry.GetRules(class)` and calls
    `ValidationEngine.GenerateValidationPartial(ns, class, properties, rules)` — a
    **giant if-chain** — writing `<Class>.Validation.cs`.
- **The extensibility problem:** adding a validation type today requires editing
  both `ValidationRuleParser.Parse` (parsing) and
  `ValidationEngine.GenerateValidationPartial` (emission). That is exactly what
  this work removes.

## Architecture

### 1. Core abstraction — `CodeGenerationEngine/Core/Validation/`

```csharp
public enum ValidationCategory { Data, Business }

// A parsed rule that knows how to emit and describe itself.
public interface IValidationRule
{
    ValidationCategory Category { get; }
    string Describe();                 // for the show commands
    void Emit(ValidationEmitContext ctx); // appends guarded errors.Add(...) C#
}

// Recognizes one rule type's natural-language phrasing.
public interface IValidationRuleParser
{
    int Priority { get; }              // deterministic order (replaces implicit regex order)
    ValidationCategory Category { get; }
    bool TryParse(ValidationParseContext ctx, out IValidationRule? rule);
}
```

- `ValidationParseContext`: fragment text, target class name, optional target
  property (data rules), and known property names on the class (business rules
  resolve operands against these).
- `ValidationEmitContext`: a code writer (StringBuilder + indentation helpers) and
  the known properties, so `Emit` can produce property-access + `errors.Add(...)`
  statements consistent with today's generated output.

**Adding a new validation type = drop one new `IValidationRuleParser` +
`IValidationRule` class into this folder. No other file changes.**

### 2. Catalog — `ValidationRuleCatalog`

- Auto-discovers every `IValidationRuleParser` implementor in the engine assembly
  via reflection (parameterless ctor), ordered by `Priority`.
- `Parse(string text, ValidationCategory category, ValidationParseContext ctx)`
  runs the matching-category parsers in priority order and returns the matched
  `IValidationRule`s.
- Reflection discovery + explicit `Priority` gives genuine Open/Closed with stable,
  testable ordering. An **extensibility test** registers a dummy test-only rule
  type and asserts it is discovered **without editing catalog code**.

### 3. Migrating today's data logic into rule classes

Decompose the existing regex chain (`ValidationRuleParser.Parse`) and if-chain
(`ValidationEngine.GenerateValidationPartial`) into self-contained **data** rule
classes, each pairing a parser + rule:

- `RequiredRule` — `"<prop> is required"`
- `LengthRangeRule` — `"<prop> length must be between N and M"`
- `NumericRangeRule` — `"<prop> must be between N and M"`
- `GreaterThanRule` / `LessThanRule` — `"<prop> must be greater/less than N"`
- `RegexRule` — `"<prop> must be a valid email"`, `"<prop> must match <pattern>"`
- `AllowedValuesRule` — `"<prop> must be A, B, C"`

`ValidationEngine.GenerateValidationPartial` becomes a thin orchestrator: for each
stored `IValidationRule` it calls `Emit`. The separate runtime
`ValidationEngine.Validate(instance, …)` reflection path is a different concern and
is left as-is.

### 4. Storage — `ValidationRegistry`

Evolves to store `IValidationRule` per class (both categories):

- `Add(string className, IValidationRule rule)`
- `IReadOnlyList<IValidationRule> Get(string className)`
- `IReadOnlyList<IValidationRule> Get(string className, ValidationCategory category)`

`show validation` filters `Data`; `show business validation` filters `Business`;
each printed line comes from `rule.Describe()`.

### 5. Business rule types shipped

- **`ComparisonRule`** (flagship, cross-field): `"EndDate must be after
  StartDate"`, `"Discount must not exceed Price"`, `"X must be greater/less than
  Y"`. Both operands resolve to properties on the class; `Emit` produces a
  comparison guarded by null/parse checks.
- **`RequiredWhenRule`** (conditional): `"TrackingNumber is required when Status is
  Shipped"`. Demonstrates that the pattern supports more than one business shape.

### 6. Command wiring

- New intents `AddBusinessValidation`, `ShowBusinessValidation`, **appended** to
  `IntentType` (keeps persisted integer values stable — the learned-intent store
  serializes intents as integers).
- `LiteralCommandTable`: `add business validation`, `show/list/view business
  validation`.
- `CommandFront`: two regex guards (`AddBusinessValidationRe`,
  `ShowBusinessValidationRe`) placed **ahead of** the existing validation guards.
  (The existing `\badd\s+validation\b` / `\b(show|list|view)\s+validation\b`
  guards do not match `... business validation` because `add`/`show` is followed
  by `business`, not `validation` — but the business guards are ordered first to
  keep intent explicit and regression-proof.)
- `CommandEntityExtractor`: `ExtractBusinessValidationInfo` (captures class + rule
  text, entity-scoped, no single property) and a `ShowBusinessValidation` case
  (class name only).
- `CommandHandlingService`: `HandleAddBusinessValidation` and
  `HandleShowBusinessValidation` added to the `Handlers` dictionary.
  `HandleAddValidation` is unchanged in observable behavior (now routes through the
  catalog internally).
- `generate validator` emits **one `Validate()` partial** with a `// Data
  validation` section (per-property checks) and a `// Business rules` section
  (entity-level checks), both sourced from the store via `Emit`.
- New `CommandDictionary` entries for the business commands; regenerate
  `docs/commands.md` (`UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests`).

## Data flow

```
user text
  → CommandFront.Parse
      → LiteralCommandTable / CommandFront guards  (business guards first)
      → CommandEntityExtractor  (class, property, rule text → ParsedCommand)
  → CommandHandlingService.Execute → Handlers[intent]
      → HandleAddValidation / HandleAddBusinessValidation
          → ValidationRuleCatalog.Parse(text, category, ctx)  → IValidationRule[]
          → ValidationRegistry.Add(class, rule)
      → HandleShowValidation / HandleShowBusinessValidation
          → ValidationRegistry.Get(class, category) → rule.Describe()
  → generate validator (EntityExtractor.GenerateValidator)
      → ValidationRegistry.Get(class)
      → ValidationEngine.GenerateValidationPartial → rule.Emit(...) per rule
      → writes <Class>.Validation.cs
```

## Error handling

- Unrecognized rule text: the catalog returns no rule; the handler reports
  `Could not parse a validation rule from: '<text>'` and returns `false` (so the
  batch command counts it as not applied).
- Business rule referencing an unknown property: parser still produces the rule
  (names resolved lazily at emit time); `generate validator` emits a comment
  noting the unresolved operand rather than failing the whole file, mirroring the
  existing best-effort emission style.
- Emission wraps each rule's checks in `try/catch` exactly as the current
  generated `Validate()` does.

## Testing

- Per-parser `TryParse` unit tests (each data + business type).
- Per-rule `Emit` tests asserting the generated C# fragment.
- Catalog discovery + ordering test.
- **Extensibility test:** a dummy test-only `IValidationRuleParser` is discovered
  and used without touching catalog code.
- Bridge routing tests: text → correct intent, including that `add business
  validation …` resolves to `AddBusinessValidation` and does **not** trip the
  `AddValidation` guard.
- Handler tests for add/show of both categories.
- Round-trip `generate validator` test producing a `Validate()` with both a data
  section and a business section that compiles.
- All existing validation tests stay green; `docs/commands.md` regenerated.

## Out of scope (YAGNI)

- Emitting validation directly to disk from the `add …` commands (deferred to
  `generate validator`, per decision).
- Refactoring the runtime `ValidationEngine.Validate(instance, …)` reflection path.
- Async / DB-backed business rules (e.g. uniqueness across a table).
