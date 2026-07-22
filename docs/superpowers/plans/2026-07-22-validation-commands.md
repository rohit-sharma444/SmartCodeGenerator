# Data Validation & Business Validation commands — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship two independent Code Generator commands — `add validation` (data, field-level) and `add business validation` (business rules, entity-level) — on a single rule-type registry that new validation types plug into without editing existing code.

**Architecture:** A `CodeGenerationEngine.Core.Validation` subsystem defines `IValidationRule` (a parsed rule that emits and describes itself) and `IValidationRuleParser` (recognizes one rule type's phrasing). `ValidationRuleCatalog` reflection-discovers every parser in the engine assembly, ordered by `Priority`. Both commands parse through the catalog, store `IValidationRule`s in `ValidationRegistry`, and `generate validator` emits a single `Validate()` partial (a data section + a business section) by asking each stored rule to emit itself. The pre-existing data-validation if-chain and its only (dead) consumer are removed.

**Tech Stack:** C# / .NET 10, MSTest, Roslyn (already referenced). Reflection for discovery.

## Global Constraints

- Target framework: `net10.0` (repo `Directory.Build.props`).
- Engine library project: `CodeGenerationEngine` (assembly name `CodeGenerationEngine`). New files live under `CodeGenerationEngine/Core/Validation/`, namespace `CodeGenerationEngine.Core.Validation`.
- Tests: MSTest in `CodeGenerator.Tests`. Classes that touch the static `SessionContext.CurrentClass` (anything driving `CommandEntityExtractor.Extract` or `CommandHandlingService.Execute`) MUST be `[DoNotParallelize]` — the assembly runs method-parallel.
- `IntentType` values are persisted as integers by the learned-intent store: **only append** new members at the end of the enum; never reorder.
- All commands run from the repository root `D:\SmartCodeGenerator-Repository` (the git root is the parent of the `SmartCodeGenerator` solution folder).
- `docs/commands.md` is generated from `CommandDictionary`; regenerate with `UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests` and commit the result.
- Commit after every task. Branch is already `feat/validation-commands`.
- Generated validation code is best-effort: each rule's emitted checks are wrapped in `try { … } catch (System.Exception) { }` by the emitter, matching the existing generator's tolerance for runtime reflection errors.

---

## Task 1: Validation core contracts + catalog

**Files:**
- Create: `CodeGenerationEngine/Core/Validation/ValidationCategory.cs`
- Create: `CodeGenerationEngine/Core/Validation/ValidationParseContext.cs`
- Create: `CodeGenerationEngine/Core/Validation/ValidationEmitContext.cs`
- Create: `CodeGenerationEngine/Core/Validation/IValidationRule.cs`
- Create: `CodeGenerationEngine/Core/Validation/IValidationRuleParser.cs`
- Create: `CodeGenerationEngine/Core/Validation/ValidationRuleCatalog.cs`
- Test: `CodeGenerator.Tests/Core/Validation/ValidationRuleCatalogTests.cs`

**Interfaces:**
- Produces:
  - `enum ValidationCategory { Data, Business }`
  - `interface IValidationRule { ValidationCategory Category { get; } string Describe(); void Emit(ValidationEmitContext ctx); }`
  - `interface IValidationRuleParser { int Priority { get; } ValidationCategory Category { get; } bool TryParse(ValidationParseContext ctx, out IValidationRule? rule); }`
  - `sealed class ValidationParseContext { string Fragment; string ClassName; string? TargetProperty; IReadOnlyList<string> KnownProperties; }`
  - `sealed class ValidationEmitContext { IReadOnlyList<PropertyDefinition> Properties; void Line(string code); bool HasProperty(string name); }`
  - `class ValidationRuleCatalog` with `static ValidationRuleCatalog Default { get; }`, `ctor(IEnumerable<IValidationRuleParser>)`, `IReadOnlyList<IValidationRuleParser> Parsers { get; }`, and `IReadOnlyList<IValidationRule> Parse(string text, ValidationCategory category, ValidationParseContext ctx)`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Core/Validation/ValidationRuleCatalogTests.cs`:

```csharp
using CodeGenerationEngine.Core.Validation;

namespace CodeGenerator.Tests.Core.Validation;

[TestClass]
public class ValidationRuleCatalogTests
{
    // Two stub parsers verify ordering and category filtering without depending
    // on any shipped rule type.
    private sealed class StubRule : IValidationRule
    {
        public ValidationCategory Category { get; init; }
        public string Text { get; init; } = "";
        public string Describe() => Text;
        public void Emit(ValidationEmitContext ctx) => ctx.Line($"// {Text}");
    }

    private sealed class HighPriorityParser : IValidationRuleParser
    {
        public int Priority => 10;
        public ValidationCategory Category => ValidationCategory.Data;
        public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
        {
            rule = new StubRule { Category = ValidationCategory.Data, Text = "high" };
            return true; // matches anything
        }
    }

    private sealed class BusinessParser : IValidationRuleParser
    {
        public int Priority => 20;
        public ValidationCategory Category => ValidationCategory.Business;
        public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
        {
            rule = new StubRule { Category = ValidationCategory.Business, Text = "biz" };
            return true;
        }
    }

    private static ValidationParseContext Ctx() =>
        new() { Fragment = "", ClassName = "C", KnownProperties = System.Array.Empty<string>() };

    [TestMethod]
    public void Parse_only_runs_parsers_of_the_requested_category()
    {
        var catalog = new ValidationRuleCatalog(new IValidationRuleParser[]
        {
            new HighPriorityParser(), new BusinessParser()
        });

        var data = catalog.Parse("anything", ValidationCategory.Data, Ctx());

        Assert.HasCount(1, data);
        Assert.AreEqual("high", data[0].Describe());
    }

    [TestMethod]
    public void Default_discovers_every_parser_in_the_engine_assembly()
    {
        // Reflection discovery must never silently skip a parser type. This keeps
        // the catalog honest as new rule classes are added in later tasks.
        var assembly = typeof(IValidationRuleParser).Assembly;
        var expected = assembly.GetTypes()
            .Count(t => typeof(IValidationRuleParser).IsAssignableFrom(t)
                        && !t.IsInterface && !t.IsAbstract
                        && t.GetConstructor(System.Type.EmptyTypes) != null);

        Assert.AreEqual(expected, ValidationRuleCatalog.Default.Parsers.Count);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~ValidationRuleCatalogTests"`
Expected: FAIL — build error, types `ValidationRuleCatalog` / `IValidationRuleParser` do not exist.

- [ ] **Step 3: Write minimal implementation**

`ValidationCategory.cs`:
```csharp
namespace CodeGenerationEngine.Core.Validation;

public enum ValidationCategory { Data, Business }
```

`ValidationParseContext.cs`:
```csharp
namespace CodeGenerationEngine.Core.Validation;

/// <summary>Everything a parser needs to turn one rule fragment into an
/// <see cref="IValidationRule"/>. Operand resolution against
/// <see cref="KnownProperties"/> is optional at parse time (business rules
/// resolve lazily at emit time).</summary>
public sealed class ValidationParseContext
{
    public required string Fragment { get; init; }
    public required string ClassName { get; init; }
    public string? TargetProperty { get; init; }
    public IReadOnlyList<string> KnownProperties { get; init; } = System.Array.Empty<string>();
}
```

`ValidationEmitContext.cs`:
```csharp
using System.Linq;
using System.Text;
using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core.Validation;

/// <summary>Sink a rule writes its generated C# into. <see cref="Line"/> appends
/// one statement inside the generated <c>Validate()</c> method body.</summary>
public sealed class ValidationEmitContext
{
    private readonly StringBuilder _sb;

    public ValidationEmitContext(StringBuilder sb, IReadOnlyList<PropertyDefinition> properties)
    {
        _sb = sb;
        Properties = properties;
    }

    public IReadOnlyList<PropertyDefinition> Properties { get; }

    public void Line(string code) => _sb.AppendLine("            " + code);

    public bool HasProperty(string name) =>
        Properties.Any(p => string.Equals(p.Name, name, System.StringComparison.OrdinalIgnoreCase));
}
```

`IValidationRule.cs`:
```csharp
namespace CodeGenerationEngine.Core.Validation;

/// <summary>A parsed validation rule that knows how to describe itself (for the
/// show commands) and emit its own check into a generated Validate() method.</summary>
public interface IValidationRule
{
    ValidationCategory Category { get; }
    string Describe();
    void Emit(ValidationEmitContext ctx);
}
```

`IValidationRuleParser.cs`:
```csharp
namespace CodeGenerationEngine.Core.Validation;

/// <summary>Recognizes one rule type's natural-language phrasing. Implementors are
/// auto-discovered by <see cref="ValidationRuleCatalog"/>; add a new type by
/// dropping a new implementation into this folder — no registration edits.</summary>
public interface IValidationRuleParser
{
    /// <summary>Lower runs first. Keeps a broad catch-all (e.g. allowed-values)
    /// from swallowing a more specific pattern.</summary>
    int Priority { get; }
    ValidationCategory Category { get; }
    bool TryParse(ValidationParseContext ctx, out IValidationRule? rule);
}
```

`ValidationRuleCatalog.cs`:
```csharp
using System.Linq;
using System.Reflection;

namespace CodeGenerationEngine.Core.Validation;

/// <summary>Holds the registered rule parsers and turns rule text into
/// <see cref="IValidationRule"/>s. <see cref="Default"/> reflection-discovers every
/// parser in the engine assembly, so a new rule type is picked up automatically.</summary>
public sealed class ValidationRuleCatalog
{
    private static readonly Lazy<ValidationRuleCatalog> _default = new(DiscoverFromAssembly);

    public ValidationRuleCatalog(IEnumerable<IValidationRuleParser> parsers)
    {
        Parsers = parsers.OrderBy(p => p.Priority)
                         .ThenBy(p => p.GetType().Name, System.StringComparer.Ordinal)
                         .ToList();
    }

    public static ValidationRuleCatalog Default => _default.Value;

    public IReadOnlyList<IValidationRuleParser> Parsers { get; }

    public IReadOnlyList<IValidationRule> Parse(
        string text, ValidationCategory category, ValidationParseContext ctx)
    {
        var rules = new List<IValidationRule>();
        if (string.IsNullOrWhiteSpace(text))
            return rules;

        foreach (var fragment in Split(text))
        {
            var fragCtx = new ValidationParseContext
            {
                Fragment = fragment,
                ClassName = ctx.ClassName,
                TargetProperty = ctx.TargetProperty,
                KnownProperties = ctx.KnownProperties
            };

            foreach (var parser in Parsers.Where(p => p.Category == category))
            {
                if (parser.TryParse(fragCtx, out var rule) && rule != null)
                {
                    rules.Add(rule);
                    break; // first match wins for this fragment
                }
            }
        }

        return rules;
    }

    // Split on newlines and ';', trim, and drop a trailing sentence period without
    // harming decimals mid-string (e.g. "between 3.5 and 10").
    private static IEnumerable<string> Split(string text) =>
        text.Replace("\r", "")
            .Split(new[] { '\n', ';' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().TrimEnd('.').Trim())
            .Where(s => s.Length > 0);

    private static ValidationRuleCatalog DiscoverFromAssembly()
    {
        var parsers = typeof(IValidationRuleParser).Assembly.GetTypes()
            .Where(t => typeof(IValidationRuleParser).IsAssignableFrom(t)
                        && !t.IsInterface && !t.IsAbstract
                        && t.GetConstructor(System.Type.EmptyTypes) != null)
            .Select(t => (IValidationRuleParser)System.Activator.CreateInstance(t)!);

        return new ValidationRuleCatalog(parsers);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~ValidationRuleCatalogTests"`
Expected: PASS (2 tests). `Default` finds 0 shipped parsers so far — the count-based assertion still holds.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/Validation CodeGenerator.Tests/Core/Validation/ValidationRuleCatalogTests.cs
git commit -m "feat: validation rule contracts + reflection-discovered catalog"
```

---

## Task 2: Registry stores IValidationRule

**Files:**
- Modify: `CodeGenerationEngine/Core/ValidationRegistry.cs` (full rewrite — 29 lines today)
- Test: `CodeGenerator.Tests/Core/Validation/ValidationRegistryTests.cs`

**Interfaces:**
- Consumes: `IValidationRule`, `ValidationCategory` (Task 1).
- Produces: `static class ValidationRegistry` with `void Add(string className, IValidationRule rule)`, `IReadOnlyList<IValidationRule> Get(string className)`, `IReadOnlyList<IValidationRule> Get(string className, ValidationCategory category)`, and `void Clear()` (test hygiene for the static store).

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Core/Validation/ValidationRegistryTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;

namespace CodeGenerator.Tests.Core.Validation;

[TestClass]
[DoNotParallelize]
public class ValidationRegistryTests
{
    private sealed class FakeRule : IValidationRule
    {
        public ValidationCategory Category { get; init; }
        public string Describe() => Category.ToString();
        public void Emit(ValidationEmitContext ctx) { }
    }

    [TestInitialize]
    public void Reset() => ValidationRegistry.Clear();

    [TestMethod]
    public void Get_by_category_filters_stored_rules()
    {
        ValidationRegistry.Add("Order", new FakeRule { Category = ValidationCategory.Data });
        ValidationRegistry.Add("Order", new FakeRule { Category = ValidationCategory.Business });

        Assert.HasCount(2, ValidationRegistry.Get("Order"));
        Assert.HasCount(1, ValidationRegistry.Get("Order", ValidationCategory.Data));
        Assert.HasCount(1, ValidationRegistry.Get("Order", ValidationCategory.Business));
    }

    [TestMethod]
    public void Get_is_case_insensitive_and_empty_for_unknown_class()
    {
        ValidationRegistry.Add("Order", new FakeRule { Category = ValidationCategory.Data });
        Assert.HasCount(1, ValidationRegistry.Get("order"));
        Assert.IsEmpty(ValidationRegistry.Get("Missing"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~ValidationRegistryTests"`
Expected: FAIL — build error, `ValidationRegistry.Add(string, IValidationRule)` / `Get` / `Clear` do not exist (today's registry stores `PropertyRule` via `AddRule`/`GetRules`).

- [ ] **Step 3: Write minimal implementation**

Replace the entire contents of `CodeGenerationEngine/Core/ValidationRegistry.cs`:

```csharp
using System.Linq;
using CodeGenerationEngine.Core.Validation;

namespace CodeGenerationEngine.Core;

/// <summary>In-memory store of validation rules per class, keyed case-insensitively.
/// Holds both data and business rules; the command layer filters by category.</summary>
public static class ValidationRegistry
{
    private static readonly Dictionary<string, List<IValidationRule>> _rules =
        new(System.StringComparer.OrdinalIgnoreCase);

    public static void Add(string className, IValidationRule rule)
    {
        if (string.IsNullOrWhiteSpace(className) || rule == null)
            return;

        if (!_rules.TryGetValue(className, out var list))
        {
            list = new List<IValidationRule>();
            _rules[className] = list;
        }

        list.Add(rule);
    }

    public static IReadOnlyList<IValidationRule> Get(string className) =>
        _rules.TryGetValue(className ?? string.Empty, out var list)
            ? list
            : System.Array.Empty<IValidationRule>();

    public static IReadOnlyList<IValidationRule> Get(string className, ValidationCategory category) =>
        Get(className).Where(r => r.Category == category).ToList();

    public static void Clear() => _rules.Clear();
}
```

> This breaks the three current callers (`CommandHandlingService.HandleAddValidation`, `HandleShowValidation`, `EntityExtractor.GenerateValidator`) which use the old `AddRule`/`GetRules` + `PropertyRule`. They are repointed in Task 8. Until then the engine will not build via those files — that is expected; this task's own test compiles because it does not touch them. **Run this task's test with the `--filter` shown; do not run the full suite until Task 8.**

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~ValidationRegistryTests"`
Expected: FAIL TO BUILD if the whole `CodeGenerationEngine` project is compiled. To keep this task green in isolation, ALSO apply the three caller repoints from Task 8 now is NOT required — instead, temporarily comment out the bodies is NOT allowed. Resolve by ordering: **do Task 8's caller edits as part of this step if the project fails to build.** See note below.

> **Build-ordering note:** Because `ValidationRegistry`'s old API had three in-repo callers, this task and Task 8's caller repoints must land together to keep the tree buildable. Simplest execution: complete Steps 1–3 here, then immediately do Task 8 Steps for the three caller edits, then run both test filters. If you prefer strictly green-between-tasks, merge Task 2 and Task 8 into one commit. The plan keeps them separate for review clarity; the executor may combine the commit.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/ValidationRegistry.cs CodeGenerator.Tests/Core/Validation/ValidationRegistryTests.cs
git commit -m "feat: ValidationRegistry stores IValidationRule with category filtering"
```

---

## Task 3: RequiredRule (first data rule) + prove auto-discovery

**Files:**
- Create: `CodeGenerationEngine/Core/Validation/Rules/RequiredRule.cs`
- Test: `CodeGenerator.Tests/Core/Validation/Rules/RequiredRuleTests.cs`

**Interfaces:**
- Consumes: `IValidationRule`, `IValidationRuleParser`, `ValidationParseContext`, `ValidationEmitContext`, `ValidationRuleCatalog` (Task 1).
- Produces: `sealed class RequiredRule : IValidationRule` and `sealed class RequiredRuleParser : IValidationRuleParser` (Priority 10, Data).

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Core/Validation/Rules/RequiredRuleTests.cs`:

```csharp
using System.Text;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class RequiredRuleTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Employee" };

    [TestMethod]
    public void Parses_is_required_phrasing()
    {
        var parser = new RequiredRuleParser();
        Assert.IsTrue(parser.TryParse(Ctx("Name is required"), out var rule));
        Assert.AreEqual(ValidationCategory.Data, rule!.Category);
        Assert.AreEqual("Name is required", rule.Describe());
    }

    [TestMethod]
    public void Does_not_parse_unrelated_text()
    {
        var parser = new RequiredRuleParser();
        Assert.IsFalse(parser.TryParse(Ctx("Name must match foo"), out _));
    }

    [TestMethod]
    public void Emits_a_required_check()
    {
        var parser = new RequiredRuleParser();
        parser.TryParse(Ctx("Name is required"), out var rule);
        var sb = new StringBuilder();
        rule!.Emit(new ValidationEmitContext(sb, new List<PropertyDefinition>()));
        var code = sb.ToString();
        StringAssert.Contains(code, "this.Name");
        StringAssert.Contains(code, "Name is required.");
    }

    [TestMethod]
    public void Is_discovered_by_the_default_catalog()
    {
        Assert.IsTrue(ValidationRuleCatalog.Default.Parsers.Any(p => p is RequiredRuleParser));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~RequiredRuleTests"`
Expected: FAIL — `RequiredRule` / `RequiredRuleParser` do not exist.

- [ ] **Step 3: Write minimal implementation**

Create `CodeGenerationEngine/Core/Validation/Rules/RequiredRule.cs`:

```csharp
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

public sealed class RequiredRule : IValidationRule
{
    private readonly string _property;
    public RequiredRule(string property) => _property = property;

    public ValidationCategory Category => ValidationCategory.Data;
    public string Describe() => $"{_property} is required";

    public void Emit(ValidationEmitContext ctx)
    {
        ctx.Line($"var __v = (object)this.{_property};");
        ctx.Line("if (__v == null || string.IsNullOrWhiteSpace(__v.ToString()))");
        ctx.Line($"    errors.Add(\"{_property} is required.\");");
    }
}

public sealed class RequiredRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern =
        new(@"^(?<prop>\w+)\s+is\s+required$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 10;
    public ValidationCategory Category => ValidationCategory.Data;

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        if (m.Success)
        {
            rule = new RequiredRule(m.Groups["prop"].Value);
            return true;
        }
        rule = null;
        return false;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~RequiredRuleTests"`
Expected: PASS (4 tests). The `ValidationRuleCatalogTests.Default_discovers_...` count test still passes (it counts dynamically).

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/Validation/Rules/RequiredRule.cs CodeGenerator.Tests/Core/Validation/Rules/RequiredRuleTests.cs
git commit -m "feat: RequiredRule data validation type, auto-discovered"
```

---

## Task 4: Emitter — ValidationEngine.EmitValidationPartial

**Files:**
- Modify: `CodeGenerationEngine/Core/ValidationEngine.cs` (add one method near the top of the class)
- Test: `CodeGenerator.Tests/Core/Validation/EmitValidationPartialTests.cs`

**Interfaces:**
- Consumes: `IValidationRule`, `ValidationCategory`, `ValidationEmitContext` (Task 1); `PropertyDefinition` (`CodeGenerationEngine.Models`); `RequiredRule` (Task 3, for the test).
- Produces: `public static string ValidationEngine.EmitValidationPartial(string ns, string className, IReadOnlyList<PropertyDefinition> properties, IReadOnlyList<IValidationRule> rules)`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Core/Validation/EmitValidationPartialTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation;

[TestClass]
public class EmitValidationPartialTests
{
    [TestMethod]
    public void Emits_a_partial_class_with_a_data_section()
    {
        var props = new List<PropertyDefinition> { new() { Name = "Name", Type = "string" } };
        var rules = new List<IValidationRule> { new RequiredRule("Name") };

        var code = ValidationEngine.EmitValidationPartial("Acme", "Employee", props, rules);

        StringAssert.Contains(code, "namespace Acme");
        StringAssert.Contains(code, "public partial class Employee");
        StringAssert.Contains(code, "public System.Collections.Generic.List<string> Validate()");
        StringAssert.Contains(code, "// Data validation");
        StringAssert.Contains(code, "Name is required.");
        StringAssert.Contains(code, "catch (System.Exception)");
        StringAssert.Contains(code, "return errors;");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~EmitValidationPartialTests"`
Expected: FAIL — `ValidationEngine.EmitValidationPartial` does not exist.

- [ ] **Step 3: Write minimal implementation**

Add these `using`s at the top of `CodeGenerationEngine/Core/ValidationEngine.cs` (keep existing ones):
```csharp
using System.Linq;
using CodeGenerationEngine.Core.Validation;
```

Add this method inside the `ValidationEngine` class (e.g. immediately after the opening brace, before `Validate`):

```csharp
/// <summary>
/// Emits a partial class with a single <c>Validate()</c> that runs every stored
/// rule. Data rules emit first (a "// Data validation" section), then business
/// rules ("// Business rules"). Each rule's checks are wrapped in try/catch so one
/// bad rule cannot abort the rest — best-effort, matching the generator's style.
/// </summary>
public static string EmitValidationPartial(
    string ns,
    string className,
    IReadOnlyList<PropertyDefinition> properties,
    IReadOnlyList<IValidationRule> rules)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"namespace {ns}");
    sb.AppendLine("{");
    sb.AppendLine($"    public partial class {className}");
    sb.AppendLine("    {");
    sb.AppendLine("        public System.Collections.Generic.List<string> Validate()");
    sb.AppendLine("        {");
    sb.AppendLine("            var errors = new System.Collections.Generic.List<string>();");

    var ctx = new ValidationEmitContext(sb, properties);

    EmitSection(sb, ctx, "// Data validation",
        rules.Where(r => r.Category == ValidationCategory.Data));
    EmitSection(sb, ctx, "// Business rules",
        rules.Where(r => r.Category == ValidationCategory.Business));

    sb.AppendLine("            return errors;");
    sb.AppendLine("        }");
    sb.AppendLine("    }");
    sb.AppendLine("}");
    return sb.ToString();
}

private static void EmitSection(
    System.Text.StringBuilder sb,
    ValidationEmitContext ctx,
    string header,
    IEnumerable<IValidationRule> rules)
{
    var list = rules.ToList();
    if (list.Count == 0)
        return;

    sb.AppendLine("            " + header);
    foreach (var rule in list)
    {
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        rule.Emit(ctx);            // each rule's locals are scoped to this block
        sb.AppendLine("            }");
        sb.AppendLine("            catch (System.Exception) { }");
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~EmitValidationPartialTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/ValidationEngine.cs CodeGenerator.Tests/Core/Validation/EmitValidationPartialTests.cs
git commit -m "feat: category-sectioned Validate() emitter over IValidationRule"
```

---

## Task 5: Remaining data rules (length, numeric range, greater/less than, regex, allowed values)

> **Superseded during review:** the data-rule emitters below must cast to object before ToString — use ((object)this.{prop})?.ToString(), not this.{prop}?.ToString() (the latter is CS0023 for value-type properties). The shipped code reflects this.

**Files:**
- Create: `CodeGenerationEngine/Core/Validation/Rules/LengthRangeRule.cs`
- Create: `CodeGenerationEngine/Core/Validation/Rules/NumericRangeRule.cs`
- Create: `CodeGenerationEngine/Core/Validation/Rules/GreaterThanRule.cs`
- Create: `CodeGenerationEngine/Core/Validation/Rules/LessThanRule.cs`
- Create: `CodeGenerationEngine/Core/Validation/Rules/RegexRule.cs`
- Create: `CodeGenerationEngine/Core/Validation/Rules/AllowedValuesRule.cs`
- Test: `CodeGenerator.Tests/Core/Validation/Rules/DataRulesTests.cs`

**Interfaces:**
- Consumes: Task 1 contracts.
- Produces: six `IValidationRule` + six `IValidationRuleParser` classes. Priorities: `LengthRangeRuleParser` 20, `NumericRangeRuleParser` 30, `GreaterThanRuleParser` 40, `LessThanRuleParser` 50, `EmailRuleParser` 60, `MatchRuleParser` 61, `AllowedValuesRuleParser` 100. All `ValidationCategory.Data`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Core/Validation/Rules/DataRulesTests.cs`:

```csharp
using System.Text;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class DataRulesTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "C" };

    private static string Emit(IValidationRule rule)
    {
        var sb = new StringBuilder();
        rule.Emit(new ValidationEmitContext(sb, new List<PropertyDefinition>()));
        return sb.ToString();
    }

    [TestMethod]
    public void Length_range()
    {
        Assert.IsTrue(new LengthRangeRuleParser().TryParse(Ctx("Name length must be between 3 and 50"), out var r));
        StringAssert.Contains(Emit(r!), "Length < 3");
        StringAssert.Contains(Emit(r!), "Length > 50");
    }

    [TestMethod]
    public void Numeric_range()
    {
        Assert.IsTrue(new NumericRangeRuleParser().TryParse(Ctx("Age must be between 18 and 60"), out var r));
        var code = Emit(r!);
        StringAssert.Contains(code, "< 18m");
        StringAssert.Contains(code, "> 60m");
    }

    [TestMethod]
    public void Greater_than()
    {
        Assert.IsTrue(new GreaterThanRuleParser().TryParse(Ctx("Salary must be greater than 0"), out var r));
        StringAssert.Contains(Emit(r!), "<= 0m");
    }

    [TestMethod]
    public void Less_than()
    {
        Assert.IsTrue(new LessThanRuleParser().TryParse(Ctx("Salary must be less than 100000"), out var r));
        StringAssert.Contains(Emit(r!), ">= 100000m");
    }

    [TestMethod]
    public void Valid_email()
    {
        Assert.IsTrue(new EmailRuleParser().TryParse(Ctx("Email must be a valid email"), out var r));
        StringAssert.Contains(Emit(r!), "Regex.IsMatch");
    }

    [TestMethod]
    public void Match_pattern()
    {
        Assert.IsTrue(new MatchRuleParser().TryParse(Ctx("Phone must match ^\\d{10}$"), out var r));
        StringAssert.Contains(Emit(r!), "Regex.IsMatch");
    }

    [TestMethod]
    public void Allowed_values_needs_a_list()
    {
        Assert.IsTrue(new AllowedValuesRuleParser().TryParse(Ctx("Country must be India, USA, UK"), out var r));
        StringAssert.Contains(Emit(r!), "__allowed");
        // single value is not an allowed-values list
        Assert.IsFalse(new AllowedValuesRuleParser().TryParse(Ctx("Country must be India"), out _));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~DataRulesTests"`
Expected: FAIL — none of the six rule types exist.

- [ ] **Step 3: Write minimal implementation**

`LengthRangeRule.cs`:
```csharp
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

public sealed class LengthRangeRule : IValidationRule
{
    private readonly string _p; private readonly int _min; private readonly int _max;
    public LengthRangeRule(string p, int min, int max) { _p = p; _min = min; _max = max; }
    public ValidationCategory Category => ValidationCategory.Data;
    public string Describe() => $"{_p} length must be between {_min} and {_max}";
    public void Emit(ValidationEmitContext ctx)
    {
        ctx.Line($"var __s = this.{_p}?.ToString();");
        ctx.Line($"if (__s != null && __s.Length < {_min}) errors.Add(\"{_p} minimum length is {_min}.\");");
        ctx.Line($"if (__s != null && __s.Length > {_max}) errors.Add(\"{_p} maximum length is {_max}.\");");
    }
}

public sealed class LengthRangeRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+length\s+must\s+be\s+between\s+(?<min>\d+)\s+and\s+(?<max>\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public int Priority => 20;
    public ValidationCategory Category => ValidationCategory.Data;
    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        if (m.Success)
        {
            rule = new LengthRangeRule(m.Groups["prop"].Value,
                int.Parse(m.Groups["min"].Value), int.Parse(m.Groups["max"].Value));
            return true;
        }
        rule = null; return false;
    }
}
```

`NumericRangeRule.cs`:
```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

public sealed class NumericRangeRule : IValidationRule
{
    private readonly string _p; private readonly decimal _min; private readonly decimal _max;
    public NumericRangeRule(string p, decimal min, decimal max) { _p = p; _min = min; _max = max; }
    public ValidationCategory Category => ValidationCategory.Data;
    public string Describe() =>
        $"{_p} must be between {_min.ToString(CultureInfo.InvariantCulture)} and {_max.ToString(CultureInfo.InvariantCulture)}";
    public void Emit(ValidationEmitContext ctx)
    {
        var min = _min.ToString(CultureInfo.InvariantCulture);
        var max = _max.ToString(CultureInfo.InvariantCulture);
        ctx.Line($"if (decimal.TryParse(this.{_p}?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var __n))");
        ctx.Line("{");
        ctx.Line($"    if (__n < {min}m) errors.Add(\"{_p} must be >= {min}.\");");
        ctx.Line($"    if (__n > {max}m) errors.Add(\"{_p} must be <= {max}.\");");
        ctx.Line("}");
    }
}

public sealed class NumericRangeRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+between\s+(?<min>[-+]?[0-9]*\.?[0-9]+)\s+and\s+(?<max>[-+]?[0-9]*\.?[0-9]+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public int Priority => 30;
    public ValidationCategory Category => ValidationCategory.Data;
    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        if (m.Success)
        {
            rule = new NumericRangeRule(m.Groups["prop"].Value,
                decimal.Parse(m.Groups["min"].Value, CultureInfo.InvariantCulture),
                decimal.Parse(m.Groups["max"].Value, CultureInfo.InvariantCulture));
            return true;
        }
        rule = null; return false;
    }
}
```

`GreaterThanRule.cs`:
```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

public sealed class GreaterThanRule : IValidationRule
{
    private readonly string _p; private readonly decimal _v;
    public GreaterThanRule(string p, decimal v) { _p = p; _v = v; }
    public ValidationCategory Category => ValidationCategory.Data;
    public string Describe() => $"{_p} must be greater than {_v.ToString(CultureInfo.InvariantCulture)}";
    public void Emit(ValidationEmitContext ctx)
    {
        var v = _v.ToString(CultureInfo.InvariantCulture);
        ctx.Line($"if (decimal.TryParse(this.{_p}?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var __n))");
        ctx.Line($"    if (__n <= {v}m) errors.Add(\"{_p} must be greater than {v}.\");");
    }
}

public sealed class GreaterThanRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+greater\s+than\s+(?<value>[-+]?[0-9]*\.?[0-9]+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public int Priority => 40;
    public ValidationCategory Category => ValidationCategory.Data;
    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        if (m.Success)
        {
            rule = new GreaterThanRule(m.Groups["prop"].Value,
                decimal.Parse(m.Groups["value"].Value, CultureInfo.InvariantCulture));
            return true;
        }
        rule = null; return false;
    }
}
```

`LessThanRule.cs`:
```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

public sealed class LessThanRule : IValidationRule
{
    private readonly string _p; private readonly decimal _v;
    public LessThanRule(string p, decimal v) { _p = p; _v = v; }
    public ValidationCategory Category => ValidationCategory.Data;
    public string Describe() => $"{_p} must be less than {_v.ToString(CultureInfo.InvariantCulture)}";
    public void Emit(ValidationEmitContext ctx)
    {
        var v = _v.ToString(CultureInfo.InvariantCulture);
        ctx.Line($"if (decimal.TryParse(this.{_p}?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var __n))");
        ctx.Line($"    if (__n >= {v}m) errors.Add(\"{_p} must be less than {v}.\");");
    }
}

public sealed class LessThanRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+less\s+than\s+(?<value>[-+]?[0-9]*\.?[0-9]+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public int Priority => 50;
    public ValidationCategory Category => ValidationCategory.Data;
    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        if (m.Success)
        {
            rule = new LessThanRule(m.Groups["prop"].Value,
                decimal.Parse(m.Groups["value"].Value, CultureInfo.InvariantCulture));
            return true;
        }
        rule = null; return false;
    }
}
```

`RegexRule.cs` (holds two parsers — email and match — sharing one rule type):
```csharp
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

public sealed class RegexRule : IValidationRule
{
    private readonly string _p; private readonly string _pattern;
    public RegexRule(string p, string pattern) { _p = p; _pattern = pattern; }
    public ValidationCategory Category => ValidationCategory.Data;
    public string Describe() => $"{_p} must match {_pattern}";
    public void Emit(ValidationEmitContext ctx)
    {
        var escaped = _pattern.Replace("\"", "\"\""); // for a verbatim @"..." literal
        ctx.Line($"var __s = this.{_p}?.ToString();");
        ctx.Line($"if (__s != null && !System.Text.RegularExpressions.Regex.IsMatch(__s, @\"{escaped}\"))");
        ctx.Line($"    errors.Add(\"{_p} format is invalid.\");");
    }
}

public sealed class EmailRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+(?:a\s+)?valid\s+email",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public int Priority => 60;
    public ValidationCategory Category => ValidationCategory.Data;
    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        if (m.Success)
        {
            rule = new RegexRule(m.Groups["prop"].Value, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
            return true;
        }
        rule = null; return false;
    }
}

public sealed class MatchRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+match\s+(?<pattern>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public int Priority => 61;
    public ValidationCategory Category => ValidationCategory.Data;
    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        if (m.Success)
        {
            rule = new RegexRule(m.Groups["prop"].Value,
                m.Groups["pattern"].Value.Trim().Trim('\'', '"'));
            return true;
        }
        rule = null; return false;
    }
}
```

`AllowedValuesRule.cs`:
```csharp
using System.Linq;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

public sealed class AllowedValuesRule : IValidationRule
{
    private readonly string _p; private readonly IReadOnlyList<string> _values;
    public AllowedValuesRule(string p, IReadOnlyList<string> values) { _p = p; _values = values; }
    public ValidationCategory Category => ValidationCategory.Data;
    public string Describe() => $"{_p} must be one of: {string.Join(", ", _values)}";
    public void Emit(ValidationEmitContext ctx)
    {
        var joined = string.Join(", ", _values.Select(v => "@\"" + v.Replace("\"", "\"\"") + "\""));
        ctx.Line($"var __s = this.{_p}?.ToString();");
        ctx.Line($"var __allowed = new[] {{ {joined} }};");
        ctx.Line("if (__s != null && !System.Array.Exists(__allowed, __a => string.Equals(__a, __s, System.StringComparison.OrdinalIgnoreCase)))");
        ctx.Line($"    errors.Add(\"{_p} contains an invalid value.\");");
    }
}

public sealed class AllowedValuesRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+(?<values>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public int Priority => 100; // catch-all: runs last
    public ValidationCategory Category => ValidationCategory.Data;
    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var raw = m.Groups["values"].Value;
        if (!raw.Contains(',') && !raw.Contains(" or ") && !raw.Contains(" and "))
            return false; // a single token is not an allowed-values list

        var parts = raw.Split(new[] { ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(p => p.Split(new[] { " or ", " and " }, System.StringSplitOptions.RemoveEmptyEntries))
            .Select(x => x.Trim().Trim('"', '\''))
            .Where(x => x.Length > 0)
            .ToList();

        if (parts.Count == 0) return false;
        rule = new AllowedValuesRule(m.Groups["prop"].Value, parts);
        return true;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~DataRulesTests"`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/Validation/Rules CodeGenerator.Tests/Core/Validation/Rules/DataRulesTests.cs
git commit -m "feat: length/range/comparison/regex/allowed-values data rule types"
```

---

## Task 6: ComparisonRule (business, cross-field)

> **Superseded during review:** ComparisonRule.Describe() echoes the verbatim parsed fragment (stored as _original); it does NOT reconstruct "{left} must be {phrase} {right}" (which rendered "must be not exceed"). The shipped code reflects this.

**Files:**
- Create: `CodeGenerationEngine/Core/Validation/Rules/ComparisonRule.cs`
- Test: `CodeGenerator.Tests/Core/Validation/Rules/ComparisonRuleTests.cs`

**Interfaces:**
- Consumes: Task 1 contracts; `PropertyDefinition`.
- Produces: `sealed class ComparisonRule : IValidationRule`, `sealed class ComparisonRuleParser : IValidationRuleParser` (Priority 20, Business).

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Core/Validation/Rules/ComparisonRuleTests.cs`:

```csharp
using System.Text;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class ComparisonRuleTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Booking" };

    private static string Emit(IValidationRule rule, params string[] props)
    {
        var sb = new StringBuilder();
        var defs = props.Select(p => new PropertyDefinition { Name = p, Type = "System.DateTime" }).ToList();
        rule.Emit(new ValidationEmitContext(sb, defs));
        return sb.ToString();
    }

    [TestMethod]
    public void Parses_after_as_a_business_rule()
    {
        Assert.IsTrue(new ComparisonRuleParser().TryParse(Ctx("EndDate must be after StartDate"), out var r));
        Assert.AreEqual(ValidationCategory.Business, r!.Category);
        Assert.AreEqual("EndDate must be after StartDate", r.Describe());
    }

    [TestMethod]
    public void Emits_a_compareto_check_when_both_properties_exist()
    {
        new ComparisonRuleParser().TryParse(Ctx("EndDate must be after StartDate"), out var r);
        var code = Emit(r!, "EndDate", "StartDate");
        StringAssert.Contains(code, "this.EndDate");
        StringAssert.Contains(code, "this.StartDate");
        StringAssert.Contains(code, "CompareTo");
        StringAssert.Contains(code, "<= 0"); // "after" is violated when left <= right
    }

    [TestMethod]
    public void Skips_with_a_comment_when_a_property_is_unknown()
    {
        new ComparisonRuleParser().TryParse(Ctx("EndDate must be after StartDate"), out var r);
        var code = Emit(r!, "EndDate"); // StartDate missing
        StringAssert.Contains(code, "// business rule skipped");
    }

    [TestMethod]
    public void Not_exceed_maps_to_greater_than_violation()
    {
        new ComparisonRuleParser().TryParse(Ctx("Discount must not exceed Price"), out var r);
        var code = Emit(r!, "Discount", "Price");
        StringAssert.Contains(code, "> 0"); // "not exceed" is violated when left > right
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~ComparisonRuleTests"`
Expected: FAIL — `ComparisonRule` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `CodeGenerationEngine/Core/Validation/Rules/ComparisonRule.cs`:

```csharp
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

public sealed class ComparisonRule : IValidationRule
{
    private readonly string _left;
    private readonly string _right;
    private readonly string _phrase;         // original operator words, for Describe()
    private readonly string _violationOp;    // the CompareTo(...) comparison that means "violated"

    public ComparisonRule(string left, string right, string phrase, string violationOp)
    {
        _left = left; _right = right; _phrase = phrase; _violationOp = violationOp;
    }

    public ValidationCategory Category => ValidationCategory.Business;
    public string Describe() => $"{_left} must be {_phrase} {_right}";

    public void Emit(ValidationEmitContext ctx)
    {
        if (!ctx.HasProperty(_left) || !ctx.HasProperty(_right))
        {
            ctx.Line($"// business rule skipped: '{Describe()}' references an unknown property");
            return;
        }

        ctx.Line($"var __l = (object)this.{_left};");
        ctx.Line($"var __r = (object)this.{_right};");
        ctx.Line($"if (__l is System.IComparable __lc && __r != null && __lc.CompareTo(__r) {_violationOp})");
        ctx.Line($"    errors.Add(\"{Describe()}.\");");
    }
}

public sealed class ComparisonRuleParser : IValidationRuleParser
{
    // "<Left> must [be] <operator> <Right>" where Right is another property.
    private static readonly Regex Pattern = new(
        @"^(?<left>\w+)\s+must\s+(?:be\s+)?(?<op>after|before|greater\s+than\s+or\s+equal\s+to|less\s+than\s+or\s+equal\s+to|greater\s+than|less\s+than|at\s+least|at\s+most|not\s+exceed)\s+(?<right>\w+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 20;
    public ValidationCategory Category => ValidationCategory.Business;

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var op = Regex.Replace(m.Groups["op"].Value.Trim(), @"\s+", " ").ToLowerInvariant();
        // Violation operator: the CompareTo(left,right) result that means the rule is broken.
        var violation = op switch
        {
            "after" or "greater than" => "<= 0",
            "before" or "less than" => ">= 0",
            "at least" or "greater than or equal to" => "< 0",
            "at most" or "less than or equal to" or "not exceed" => "> 0",
            _ => "<= 0"
        };

        rule = new ComparisonRule(
            m.Groups["left"].Value, m.Groups["right"].Value, op, violation);
        return true;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~ComparisonRuleTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/Validation/Rules/ComparisonRule.cs CodeGenerator.Tests/Core/Validation/Rules/ComparisonRuleTests.cs
git commit -m "feat: ComparisonRule business validation type (cross-field)"
```

---

## Task 7: RequiredWhenRule (business, conditional)

**Files:**
- Create: `CodeGenerationEngine/Core/Validation/Rules/RequiredWhenRule.cs`
- Test: `CodeGenerator.Tests/Core/Validation/Rules/RequiredWhenRuleTests.cs`

**Interfaces:**
- Consumes: Task 1 contracts; `PropertyDefinition`.
- Produces: `sealed class RequiredWhenRule : IValidationRule`, `sealed class RequiredWhenRuleParser : IValidationRuleParser` (Priority 10, Business).

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Core/Validation/Rules/RequiredWhenRuleTests.cs`:

```csharp
using System.Text;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class RequiredWhenRuleTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Order" };

    [TestMethod]
    public void Parses_conditional_required()
    {
        Assert.IsTrue(new RequiredWhenRuleParser().TryParse(
            Ctx("TrackingNumber is required when Status is Shipped"), out var r));
        Assert.AreEqual(ValidationCategory.Business, r!.Category);
        Assert.AreEqual("TrackingNumber is required when Status is Shipped", r.Describe());
    }

    [TestMethod]
    public void Emits_guarded_required_check()
    {
        new RequiredWhenRuleParser().TryParse(
            Ctx("TrackingNumber is required when Status is Shipped"), out var r);
        var sb = new StringBuilder();
        var defs = new List<PropertyDefinition>
        {
            new() { Name = "TrackingNumber", Type = "string" },
            new() { Name = "Status", Type = "string" }
        };
        r!.Emit(new ValidationEmitContext(sb, defs));
        var code = sb.ToString();
        StringAssert.Contains(code, "this.Status");
        StringAssert.Contains(code, "\"Shipped\"");
        StringAssert.Contains(code, "this.TrackingNumber");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~RequiredWhenRuleTests"`
Expected: FAIL — `RequiredWhenRule` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `CodeGenerationEngine/Core/Validation/Rules/RequiredWhenRule.cs`:

```csharp
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

public sealed class RequiredWhenRule : IValidationRule
{
    private readonly string _target; private readonly string _cond; private readonly string _value;
    public RequiredWhenRule(string target, string cond, string value)
    {
        _target = target; _cond = cond; _value = value;
    }

    public ValidationCategory Category => ValidationCategory.Business;
    public string Describe() => $"{_target} is required when {_cond} is {_value}";

    public void Emit(ValidationEmitContext ctx)
    {
        if (!ctx.HasProperty(_target) || !ctx.HasProperty(_cond))
        {
            ctx.Line($"// business rule skipped: '{Describe()}' references an unknown property");
            return;
        }

        var value = _value.Replace("\"", "\\\"");
        ctx.Line($"var __c = (object)this.{_cond};");
        ctx.Line($"if (__c != null && string.Equals(__c.ToString(), \"{value}\", System.StringComparison.OrdinalIgnoreCase))");
        ctx.Line("{");
        ctx.Line($"    var __t = (object)this.{_target};");
        ctx.Line("    if (__t == null || string.IsNullOrWhiteSpace(__t.ToString()))");
        ctx.Line($"        errors.Add(\"{Describe()}.\");");
        ctx.Line("}");
    }
}

public sealed class RequiredWhenRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<target>\w+)\s+is\s+required\s+when\s+(?<cond>\w+)\s+is\s+(?<val>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 10;
    public ValidationCategory Category => ValidationCategory.Business;

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        if (m.Success)
        {
            rule = new RequiredWhenRule(
                m.Groups["target"].Value, m.Groups["cond"].Value,
                m.Groups["val"].Value.Trim().Trim('\'', '"'));
            return true;
        }
        rule = null; return false;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~RequiredWhenRuleTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/Validation/Rules/RequiredWhenRule.cs CodeGenerator.Tests/Core/Validation/Rules/RequiredWhenRuleTests.cs
git commit -m "feat: RequiredWhenRule conditional business validation type"
```

---

## Task 8: Repoint the command path; remove the old if-chain

**Files:**
- Modify: `CodeGenerationEngine/Service/CommandHandlingService.cs` — `HandleAddValidation` (275-290), `HandleShowValidation` (364-389)
- Modify: `CodeGenerationEngine/Core/EntityExtractor.cs` — `GenerateValidator` (14-62)
- Delete: `CodeGenerationEngine/Core/ValidationRuleParser.cs` (now unused: its only caller was the old `HandleAddValidation`; its own `GenerateClassFromNaturalLanguage` has no callers)
- Modify: `CodeGenerationEngine/Core/ValidationEngine.cs` — delete the old `GenerateValidationPartial(…, IEnumerable<PropertyRule>)` method (166-269); keep `Validate`, `EmitValidationPartial`, `NormalizeType`, `InferTypeFromString`
- Test: `CodeGenerator.Tests/Core/Validation/DataValidationCommandTests.cs`

**Interfaces:**
- Consumes: `ValidationRuleCatalog` (Task 1), `ValidationRegistry` (Task 2), `ValidationEngine.EmitValidationPartial` (Task 4).
- Produces: no new public surface; `HandleAddValidation`/`HandleShowValidation`/`EntityExtractor.GenerateValidator` now route through the catalog + registry.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Core/Validation/DataValidationCommandTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;

namespace CodeGenerator.Tests.Core.Validation;

[TestClass]
[DoNotParallelize]
public class DataValidationCommandTests
{
    [TestInitialize]
    public void Reset()
    {
        ValidationRegistry.Clear();
        LearnedIntentStore.UseInMemory();
    }

    [TestMethod]
    public void Add_validation_registers_a_data_rule()
    {
        var command = new ParsedCommand
        {
            Intent = IntentType.AddValidation,
            ClassName = "Employee",
            PropertyName = "Name",
            ValidationText = "Name is required",
            OriginalSentence = "add validation should not be blank on Name in Employee"
        };
        ProjectMetaData? project = null;

        var ok = CommandHandlingService.Execute(command, ref project, [], Path.GetTempPath(), _ => { });

        Assert.IsTrue(ok);
        Assert.HasCount(1, ValidationRegistry.Get("Employee", ValidationCategory.Data));
    }

    [TestMethod]
    public void Add_validation_reports_failure_on_unparseable_text()
    {
        var command = new ParsedCommand
        {
            Intent = IntentType.AddValidation,
            ClassName = "Employee",
            ValidationText = "flibbertigibbet",
            OriginalSentence = "add validation flibbertigibbet"
        };
        ProjectMetaData? project = null;

        var ok = CommandHandlingService.Execute(command, ref project, [], Path.GetTempPath(), _ => { });

        Assert.IsFalse(ok);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~DataValidationCommandTests"`
Expected: FAIL to build — `HandleAddValidation` still references the deleted `ValidationRuleParser.Parse` / `ValidationRegistry.AddRule` from Task 2's rewrite.

- [ ] **Step 3: Write minimal implementation**

In `CodeGenerationEngine/Service/CommandHandlingService.cs`, add near the other usings:
```csharp
using CodeGenerationEngine.Core.Validation;
```

Replace `HandleAddValidation` (lines 275-290) with:
```csharp
private static bool HandleAddValidation(CommandExecutionContext ctx)
{
    return AddRules(ctx, ValidationCategory.Data,
        successPrefix: $"Validation added to {ctx.Command.ClassName}" +
            (string.IsNullOrWhiteSpace(ctx.Command.PropertyName)
                ? "." : $".{ctx.Command.PropertyName}"));
}

// Shared by data and business add-commands: parse the rule text through the
// catalog for the given category and register whatever was recognized.
private static bool AddRules(CommandExecutionContext ctx, ValidationCategory category, string successPrefix)
{
    var parseCtx = new ValidationParseContext
    {
        Fragment = ctx.Command.ValidationText,
        ClassName = ctx.Command.ClassName,
        TargetProperty = string.IsNullOrWhiteSpace(ctx.Command.PropertyName) ? null : ctx.Command.PropertyName
    };

    var rules = ValidationRuleCatalog.Default.Parse(ctx.Command.ValidationText, category, parseCtx);
    if (rules.Count == 0)
    {
        Console.WriteLine($"Could not parse a validation rule from: '{ctx.Command.ValidationText}'");
        return false;
    }

    foreach (var rule in rules)
        ValidationRegistry.Add(ctx.Command.ClassName, rule);

    Console.WriteLine(successPrefix);
    return true;
}
```

Replace `HandleShowValidation` (lines 364-389) with:
```csharp
private static bool HandleShowValidation(CommandExecutionContext ctx) =>
    ShowRules(ctx.Command.ClassName, ValidationCategory.Data, "Validation rules");
```
Add this shared helper next to it:
```csharp
private static bool ShowRules(string className, ValidationCategory category, string heading)
{
    var rules = ValidationRegistry.Get(className, category);
    if (rules.Count == 0)
    {
        Console.WriteLine($"No {heading.ToLowerInvariant()} for '{className}'.");
        return true;
    }

    Console.WriteLine($"{heading} for {className}:");
    foreach (var rule in rules)
        Console.WriteLine($"  {rule.Describe()}");
    return true;
}
```

In `CodeGenerationEngine/Core/EntityExtractor.cs`, add:
```csharp
using CodeGenerationEngine.Core.Validation;
```
Replace the body of `GenerateValidator` from the `var rules = …` line through the `GenerateValidationPartial` call (lines 22-53) so it reads:
```csharp
        var rules = ValidationRegistry.Get(command.ClassName);

        if (rules.Count == 0)
        {
            Console.WriteLine($"No validation rules found for {command.ClassName}.");
            return false;
        }

        var project = ProjectManager.CurrentProject;
        if (project == null)
        {
            Console.WriteLine("No project selected.");
            return false;
        }

        var classFile = Directory
            .EnumerateFiles(project.ProjectPath,
                $"{command.ClassName}.cs",
                SearchOption.AllDirectories).FirstOrDefault();

        if (classFile == null)
        {
            Console.WriteLine($"Class '{command.ClassName}' not found.");
            return false;
        }

        var properties = ExtractProperties(classFile);
        var namespaceName = ExtractNamespace(classFile);

        var validationCode = ValidationEngine.EmitValidationPartial(
                namespaceName, command.ClassName, properties, rules);
```
(Add `using System.Linq;` to `EntityExtractor.cs` if not present — `FirstOrDefault`/`Count` need it; `Count` on `IReadOnlyList` is a property, `FirstOrDefault` needs Linq. The file already uses `FirstOrDefault`, so Linq is present.)

Delete the file `CodeGenerationEngine/Core/ValidationRuleParser.cs`.

In `CodeGenerationEngine/Core/ValidationEngine.cs`, delete the entire old `GenerateValidationPartial(string ns, string className, IEnumerable<PropertyDefinition> properties, IEnumerable<PropertyRule> rules)` method (the ~104-line if-chain). Keep `Validate`, the new `EmitValidationPartial`, `NormalizeType`, and `InferTypeFromString`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~DataValidationCommandTests|FullyQualifiedName~ValidationRegistryTests"`
Expected: PASS. Then run the whole engine/generator suite to confirm nothing else referenced the deleted code:
Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx`
Expected: Build succeeds (no references to `ValidationRuleParser` or the removed overload remain).

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Service/CommandHandlingService.cs CodeGenerationEngine/Core/EntityExtractor.cs CodeGenerationEngine/Core/ValidationEngine.cs CodeGenerator.Tests/Core/Validation/DataValidationCommandTests.cs
git rm CodeGenerationEngine/Core/ValidationRuleParser.cs
git commit -m "refactor: route data validation through the catalog; drop the if-chain"
```

---

## Task 9: Business-validation intents + bridge routing

**Files:**
- Modify: `CodeGenerationEngine/Models/IntentType.cs` — append two members
- Modify: `NaturalLanguageBridge/LiteralCommandTable.cs` — add exact entries
- Modify: `NaturalLanguageBridge/CommandFront.cs` — add two guards ahead of the existing validation guards
- Modify: `NaturalLanguageBridge/CommandEntityExtractor.cs` — add extraction cases + `ExtractBusinessValidationInfo`
- Test: `CodeGenerator.Tests/Bridge/BusinessValidationRoutingTests.cs`

**Interfaces:**
- Consumes: `IntentType` (extended), `CommandEntityExtractor.Extract`.
- Produces: `IntentType.AddBusinessValidation`, `IntentType.ShowBusinessValidation`; `CommandFront.Parse` resolves these; `CommandEntityExtractor.ExtractBusinessValidationInfo(string, ParsedCommand)` sets `ClassName` + `ValidationText`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Bridge/BusinessValidationRoutingTests.cs`:

```csharp
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
[DoNotParallelize]
public class BusinessValidationRoutingTests
{
    [TestMethod]
    public void Add_business_validation_resolves_intent_class_and_text()
    {
        var cmd = CommandFront.Parse("add business validation EndDate must be after StartDate in Booking", "");
        Assert.AreEqual(IntentType.AddBusinessValidation, cmd.Intent);
        Assert.AreEqual("Booking", cmd.ClassName);
        Assert.AreEqual("EndDate must be after StartDate", cmd.ValidationText);
    }

    [TestMethod]
    public void Add_business_validation_does_not_trip_the_data_validation_guard()
    {
        var cmd = CommandFront.Parse("add business validation X must be after Y in Z", "");
        Assert.AreNotEqual(IntentType.AddValidation, cmd.Intent);
    }

    [TestMethod]
    public void Show_business_validation_resolves_intent_and_class()
    {
        var cmd = CommandFront.Parse("show business validation Booking", "");
        Assert.AreEqual(IntentType.ShowBusinessValidation, cmd.Intent);
        Assert.AreEqual("Booking", cmd.ClassName);
    }

    [TestMethod]
    public void Data_add_validation_still_routes_to_AddValidation()
    {
        var cmd = CommandFront.Parse("add validation Email should not be blank on Email in Customer", "");
        Assert.AreEqual(IntentType.AddValidation, cmd.Intent);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~BusinessValidationRoutingTests"`
Expected: FAIL — `IntentType.AddBusinessValidation` does not exist.

- [ ] **Step 3: Write minimal implementation**

In `CodeGenerationEngine/Models/IntentType.cs`, append two members at the very end (after `ShowValidation`), keeping the "append only" rule:
```csharp
    ShowValidation,

    // Business validation (entity-level). Appended last to keep persisted
    // integer values stable.
    AddBusinessValidation,
    ShowBusinessValidation
}
```

In `NaturalLanguageBridge/LiteralCommandTable.cs`, add to the `Exact` dictionary (after the existing validation entries):
```csharp
        ["add business validation"] = IntentType.AddBusinessValidation,
        ["show business validation"] = IntentType.ShowBusinessValidation,
        ["list business validation"] = IntentType.ShowBusinessValidation,
        ["view business validation"] = IntentType.ShowBusinessValidation,
```

In `NaturalLanguageBridge/CommandFront.cs`, add two compiled regexes next to the existing ones:
```csharp
    private static readonly Regex AddBusinessValidationRe =
        new(@"\badd\s+business\s+validation\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ShowBusinessValidationRe =
        new(@"\b(?:show|list|view)\s+business\s+validation\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
```
Then, in `Parse`, add these two blocks **immediately before** the existing `if (AddValidationRe.IsMatch(text))` block:
```csharp
        if (AddBusinessValidationRe.IsMatch(text))
        {
            var cmd = CommandEntityExtractor.Extract(input ?? string.Empty, IntentType.AddBusinessValidation, workspace);
            cmd.Confidence = 1.0;
            return cmd;
        }

        if (ShowBusinessValidationRe.IsMatch(text))
        {
            var cmd = CommandEntityExtractor.Extract(input ?? string.Empty, IntentType.ShowBusinessValidation, workspace);
            cmd.Confidence = 1.0;
            return cmd;
        }
```

In `NaturalLanguageBridge/CommandEntityExtractor.cs`, add two cases to the `switch` in `Extract` (next to the existing `AddValidation`/`ShowValidation` cases, around lines 84-90):
```csharp
            case IntentType.AddBusinessValidation:
                ExtractBusinessValidationInfo(sentence, command);
                break;

            case IntentType.ShowBusinessValidation:
                ExtractBusinessShowTarget(sentence, command);
                break;
```
Add these two methods (next to `ExtractValidationInfo`, ~line 255):
```csharp
    public static void ExtractBusinessValidationInfo(string sentence, ParsedCommand command)
    {
        // "add business validation <rule> in|for|on <class>"
        var withClass = Regex.Match(sentence,
            @"add\s+business\s+validation\s+(?<rule>.+?)\s+(?:in|for|on)\s+(?<class>[A-Za-z_]\w*)\s*$",
            RegexOptions.IgnoreCase);
        if (withClass.Success)
        {
            command.ValidationText = withClass.Groups["rule"].Value.Trim();
            command.ClassName = ToPascalCase(withClass.Groups["class"].Value);
            return;
        }

        // "add business validation <rule>" (class comes from session context)
        var noClass = Regex.Match(sentence,
            @"add\s+business\s+validation\s+(?<rule>.+)$", RegexOptions.IgnoreCase);
        if (noClass.Success)
            command.ValidationText = noClass.Groups["rule"].Value.Trim();
    }

    public static void ExtractBusinessShowTarget(string sentence, ParsedCommand command)
    {
        var m = Regex.Match(sentence,
            @"(?:show|list|view)\s+business\s+validation\s+(?:rules?\s+)?(?:for\s+)?(?<class>[A-Za-z_]\w*)",
            RegexOptions.IgnoreCase);
        if (m.Success)
            command.ClassName = ToPascalCase(m.Groups["class"].Value);
    }
```

> Note: `ToPascalCase("Booking")` must return `"Booking"`. It is the same helper the existing `ExtractValidationInfo` uses for class names, so class casing behaves identically to the data command.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~BusinessValidationRoutingTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Models/IntentType.cs NaturalLanguageBridge/LiteralCommandTable.cs NaturalLanguageBridge/CommandFront.cs NaturalLanguageBridge/CommandEntityExtractor.cs CodeGenerator.Tests/Bridge/BusinessValidationRoutingTests.cs
git commit -m "feat: route add/show business validation to new intents"
```

---

## Task 10: Business-validation handlers

**Files:**
- Modify: `CodeGenerationEngine/Service/CommandHandlingService.cs` — register two handlers in the `Handlers` dictionary (65-99) and add two handler methods
- Test: `CodeGenerator.Tests/Core/Validation/BusinessValidationCommandTests.cs`

**Interfaces:**
- Consumes: `AddRules` / `ShowRules` helpers (Task 8), `ValidationCategory.Business`, `IntentType.AddBusinessValidation`/`ShowBusinessValidation` (Task 9).
- Produces: `Handlers[IntentType.AddBusinessValidation]` and `Handlers[IntentType.ShowBusinessValidation]` are populated → `CommandHandlingService.IsSupported` returns true for both.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Core/Validation/BusinessValidationCommandTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;

namespace CodeGenerator.Tests.Core.Validation;

[TestClass]
[DoNotParallelize]
public class BusinessValidationCommandTests
{
    [TestInitialize]
    public void Reset()
    {
        ValidationRegistry.Clear();
        LearnedIntentStore.UseInMemory();
    }

    [TestMethod]
    public void Add_business_validation_registers_a_business_rule()
    {
        var command = new ParsedCommand
        {
            Intent = IntentType.AddBusinessValidation,
            ClassName = "Booking",
            ValidationText = "EndDate must be after StartDate",
            OriginalSentence = "add business validation EndDate must be after StartDate in Booking"
        };
        ProjectMetaData? project = null;

        var ok = CommandHandlingService.Execute(command, ref project, [], Path.GetTempPath(), _ => { });

        Assert.IsTrue(ok);
        Assert.HasCount(1, ValidationRegistry.Get("Booking", ValidationCategory.Business));
        Assert.IsEmpty(ValidationRegistry.Get("Booking", ValidationCategory.Data));
    }

    [TestMethod]
    public void Both_intents_are_supported()
    {
        Assert.IsTrue(CommandHandlingService.IsSupported(IntentType.AddBusinessValidation));
        Assert.IsTrue(CommandHandlingService.IsSupported(IntentType.ShowBusinessValidation));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~BusinessValidationCommandTests"`
Expected: FAIL — `Both_intents_are_supported` fails (no handlers registered) and the add test hits the "not supported yet" path.

- [ ] **Step 3: Write minimal implementation**

In `CodeGenerationEngine/Service/CommandHandlingService.cs`, add to the `Handlers` dictionary initializer (after the `ShowValidation` entry, ~line 93):
```csharp
            [IntentType.AddBusinessValidation] = HandleAddBusinessValidation,
            [IntentType.ShowBusinessValidation] = HandleShowBusinessValidation,
```
Add the two handler methods next to `HandleAddValidation`:
```csharp
private static bool HandleAddBusinessValidation(CommandExecutionContext ctx) =>
    AddRules(ctx, ValidationCategory.Business,
        successPrefix: $"Business rule added to {ctx.Command.ClassName}: {ctx.Command.ValidationText}");

private static bool HandleShowBusinessValidation(CommandExecutionContext ctx) =>
    ShowRules(ctx.Command.ClassName, ValidationCategory.Business, "Business rules");
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~BusinessValidationCommandTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Service/CommandHandlingService.cs CodeGenerator.Tests/Core/Validation/BusinessValidationCommandTests.cs
git commit -m "feat: add/show business validation command handlers"
```

---

## Task 11: Command catalog entries + regenerate docs

**Files:**
- Modify: `CodeGenerationEngine/Core/CommandDictionary.cs` — add entries in the Validation region (288-310)
- Modify (generated): `docs/commands.md`
- Test: existing `CodeGenerator.Tests/Core/CommandReferenceDocTests.cs` (no change)

**Interfaces:**
- Consumes: none.
- Produces: two new rows in the Validation section of `CommandDictionary.Commands`.

- [ ] **Step 1: Verify the doc test fails after adding catalog entries**

In `CodeGenerationEngine/Core/CommandDictionary.cs`, add inside the `#region "Validation"` block (after the `show validation` entry, before `#endregion`):
```csharp
        new()
        {
            Category = "Validation",
            Command = "add business validation <rule> in <class name>",
            Description = "Adds an entity-level business rule to a class.",
            Example = "add business validation EndDate must be after StartDate in Booking"
        },
        new()
        {
            Category = "Validation",
            Command = "show business validation <class name>",
            Description = "Show business rules for a class.",
            Example = "show business validation Booking"
        },
```

- [ ] **Step 2: Run the doc test to verify it fails**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~CommandReferenceDocTests"`
Expected: FAIL — `docs/commands.md is stale`.

- [ ] **Step 3: Regenerate the doc**

Run: `UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~CommandReferenceDocTests"`
Expected: PASS (the test rewrites `docs/commands.md`).

- [ ] **Step 4: Run the doc test again to verify it passes**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~CommandReferenceDocTests"`
Expected: PASS. Confirm `docs/commands.md` now lists both business-validation commands.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/CommandDictionary.cs docs/commands.md
git commit -m "docs: catalog and reference for business validation commands"
```

---

## Task 12: End-to-end generate validator (data + business)

**Files:**
- Test: `CodeGenerator.Tests/Core/Validation/GenerateValidatorEndToEndTests.cs`

**Interfaces:**
- Consumes: `ValidationEngine.EmitValidationPartial`, all rule types, `ValidationRegistry`.
- Produces: nothing new — an integration test proving a mixed rule set emits a compiling `Validate()` with both sections.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Core/Validation/GenerateValidatorEndToEndTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerator.Tests.Core.Validation;

[TestClass]
public class GenerateValidatorEndToEndTests
{
    [TestMethod]
    public void Mixed_rules_emit_a_syntactically_valid_partial_with_both_sections()
    {
        var parseCtx = new ValidationParseContext { Fragment = "", ClassName = "Booking" };
        var catalog = ValidationRuleCatalog.Default;

        var rules = new List<IValidationRule>();
        rules.AddRange(catalog.Parse("StartDate is required", ValidationCategory.Data, parseCtx));
        rules.AddRange(catalog.Parse("EndDate must be after StartDate", ValidationCategory.Business, parseCtx));

        var props = new List<PropertyDefinition>
        {
            new() { Name = "StartDate", Type = "System.DateTime" },
            new() { Name = "EndDate", Type = "System.DateTime" }
        };

        var code = ValidationEngine.EmitValidationPartial("Acme", "Booking", props, rules);

        StringAssert.Contains(code, "// Data validation");
        StringAssert.Contains(code, "// Business rules");
        StringAssert.Contains(code, "StartDate is required.");
        StringAssert.Contains(code, "EndDate must be after StartDate.");

        // The generated partial must at least parse as valid C#.
        var tree = CSharpSyntaxTree.ParseText(code);
        var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.IsEmpty(errors, string.Join("\n", errors.Select(e => e.ToString())));
    }
}
```

- [ ] **Step 2: Run test to verify it fails or passes**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~GenerateValidatorEndToEndTests"`
Expected: This may PASS immediately (all pieces exist by now). If the syntax-diagnostics assertion fails, fix the offending rule's `Emit` (a stray brace or quote) until the tree parses clean. This test is the guard that emitted code is well-formed.

- [ ] **Step 3: Run the full suite**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`
Expected: PASS — the whole solution, confirming no regression in the wider generator/bridge suites.

- [ ] **Step 4: Commit**

```bash
git add CodeGenerator.Tests/Core/Validation/GenerateValidatorEndToEndTests.cs
git commit -m "test: end-to-end generate validator emits both data and business sections"
```

---

## Self-Review

**1. Spec coverage:**
- §1 Core abstraction → Task 1. ✅
- §2 Catalog (reflection + Priority + extensibility test) → Task 1 (`Default_discovers_every_parser…`). ✅
- §3 Migrate data logic into rule classes; thin emitter → Tasks 3, 4, 5, 8 (delete if-chain). ✅
- §4 Storage (`Get` / `Get(category)`) → Task 2. ✅
- §5 Business rule types (ComparisonRule + RequiredWhenRule) → Tasks 6, 7. ✅
- §6 Command wiring (intents, literal table, CommandFront guards ordered first, extractor, handlers, generate-validator both sections, CommandDictionary + docs) → Tasks 9, 10, 11, 4/8/12. ✅
- §7 Testing (per-parser, per-emit, catalog discovery, extensibility, routing incl. no-trip, handler, round-trip both sections, docs, full suite) → distributed; the "no-trip" guard is Task 9, both-section round-trip is Task 12. ✅
- Out-of-scope items (no disk write from add-commands, runtime `Validate` untouched) respected. ✅

**2. Placeholder scan:** No TBD/TODO; every code step shows complete code. The only soft spot is Task 2/8's build-ordering note — it is explicit about combining commits, not a placeholder.

**3. Type consistency:** `ValidationRuleCatalog.Parse(text, category, ctx)`, `ValidationRegistry.Add/Get`, `ValidationEmitContext.Line/HasProperty/Properties`, `EmitValidationPartial(ns, className, IReadOnlyList<PropertyDefinition>, IReadOnlyList<IValidationRule>)`, and `AddRules/ShowRules` helper names are used identically across Tasks 1, 2, 4, 8, 10. `IntentType.AddBusinessValidation`/`ShowBusinessValidation` consistent across Tasks 9, 10, 11. Rule constructor signatures match their tests.

**Known execution caveat (flagged, not a defect):** Tasks 2 and 8 straddle a build break because `ValidationRegistry`'s old API had three in-repo callers. The executor should land them together (or as one commit) so the tree builds green between reviewable units. This is called out in Task 2 Step 4.
