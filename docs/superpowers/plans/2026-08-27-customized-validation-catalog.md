# Customized Validation — Catalog Extension Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Widen `ValidationRuleCatalog` from nine rule types to roughly thirty, so the plain English a person actually types parses into a check, and fix the two existing parsers the new ones would collide with.

**Architecture:** Every rule type is one file in `CodeGenerationEngine/Core/Validation/Rules/` holding a rule class (knows how to `Describe()` itself and `Emit()` its C#) and one or more parser classes (recognize a phrasing, produce the rule). Parsers are reflection-discovered by `ValidationRuleCatalog.DiscoverFromAssembly` — there is no registration list to edit. Ordering between parsers is the `Priority` property, lowest first, first match per fragment wins. Two interface members are added so the page in the second plan can list accepted phrasings and refuse rules naming absent properties without either list drifting from the parsers.

**Tech Stack:** C#, .NET, Roslyn (`Microsoft.CodeAnalysis.CSharp` — already referenced by `CodeGenerationEngine`), MSTest.

**Spec:** `docs/superpowers/specs/2026-08-27-customized-validation-design.md`

## Global Constraints

- **Emitted C# must be compile-tested, never substring-tested alone.** Use `Compiler.Compile` from `CodeGenerationEngine.Core`. This project has shipped emitted code that passed substring assertions and did not compile.
- **`ValidationEmitContext.Line` writes each line prefixed with exactly twelve spaces.** Do not change it — the second plan depends on that fixed base indent.
- **Every emitted check is wrapped in its own `try`/`catch` by `ValidationEngine.EmitSection`.** A rule's `Emit` therefore owns its own scope and may reuse local names like `__n`, `__s`, `__d` freely.
- **User text inside an emitted string literal goes through `Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(text, quote: true)`.** A rule value containing a quote must not be able to end the literal. `RequiredWhenRule` is the existing example.
- **Culture:** every numeric parse uses `System.Globalization.NumberStyles.Any` with `System.Globalization.CultureInfo.InvariantCulture`; every date parse uses `System.Globalization.CultureInfo.InvariantCulture` with `System.Globalization.DateTimeStyles.None`. Never the single-argument `DateTime.TryParse` overload — it reads `03/04/2026` as whichever of March and April the machine's culture prefers.
- **New parser priorities must be below 100** (`AllowedValuesRuleParser` is the catch-all there) and must not tie with a parser whose pattern could also match the same sentence.
- **MSTest:** `Assert.HasCount(n, collection)`, never `Assert.AreEqual(n, collection.Count)` — analyzer rule MSTEST0037 fails the Release build on the latter.
- **Test command:** `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~<TestClass>"` run from the repository root, `D:\SmartCodeGenerator-Repository`.
- **CI gives no verdict on this repository** — every GitHub Actions run since 2026-08-25 dies in seconds against an account spending limit. A local full `dotnet test` is the verdict.

---

### Task 1: Stop the two existing parsers swallowing the new phrasings

`AllowedValuesRuleParser` is the priority-100 catch-all. It already refuses a single bare token, but it splits on `" and "`, so a conjoined sentence no specific parser matched becomes a list: `Age must be greater than 0 and less than 100` parses to `Age must be one of: greater than 0, less than 100` — a check that can never pass, reported as a success.

`ComparisonRuleParser` (business, priority 20) matches `<left> must be at least <right>` with `right` as `\w+`, which matches digits. Once Task 3 adds the data-side `at least` parser, a sentence parsed under the business category turns `Age must be at least 18` into a comparison against a property named `18`.

**Files:**
- Modify: `CodeGenerationEngine/Core/Validation/Rules/AllowedValuesRule.cs:24-49`
- Modify: `CodeGenerationEngine/Core/Validation/Rules/ComparisonRule.cs:41-44`
- Test: `CodeGenerator.Tests/Core/Validation/Rules/ParserCollisionTests.cs` (create)

**Interfaces:**
- Consumes: nothing.
- Produces: nothing new. Behaviour change only — `AllowedValuesRuleParser.TryParse` now returns false for `" and "`-joined text with no comma and no `" or "`; `ComparisonRuleParser.TryParse` now returns false when the right operand is all digits.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/Core/Validation/Rules/ParserCollisionTests.cs`:

```csharp
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;

namespace CodeGenerator.Tests.Core.Validation.Rules;

/// <summary>
/// The two parsers that match more than they should. Both are load-bearing for the wider
/// catalog: the catch-all would turn every unmatched conjoined sentence into a bogus
/// allowed-values list, and the comparison rule would claim "at least 18" as a comparison
/// against a property called 18.
/// </summary>
[TestClass]
public class ParserCollisionTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Employee" };

    [TestMethod]
    public void Conjoined_bounds_are_not_an_allowed_values_list()
    {
        var parsed = new AllowedValuesRuleParser()
            .TryParse(Ctx("Age must be greater than 0 and less than 100"), out _);

        Assert.IsFalse(parsed, "'and' joins two rules, not two allowed values.");
    }

    [TestMethod]
    public void Comma_list_is_still_an_allowed_values_list()
    {
        Assert.IsTrue(new AllowedValuesRuleParser()
            .TryParse(Ctx("Status must be Draft, Sent, Paid"), out var rule));
        Assert.AreEqual("Status must be one of: Draft, Sent, Paid", rule!.Describe());
    }

    [TestMethod]
    public void Or_list_is_still_an_allowed_values_list()
    {
        Assert.IsTrue(new AllowedValuesRuleParser()
            .TryParse(Ctx("Status must be Draft or Paid"), out var rule));
        Assert.AreEqual("Status must be one of: Draft, Paid", rule!.Describe());
    }

    /// <summary>
    /// The ordinary English list. The guard is what refuses a bare " and "; the split must still
    /// break on it, or the last two items fuse into one value the check can never match.
    /// </summary>
    [TestMethod]
    public void A_comma_list_ending_in_and_keeps_all_three_values()
    {
        Assert.IsTrue(new AllowedValuesRuleParser()
            .TryParse(Ctx("Status must be Draft, Sent and Paid"), out var rule));
        Assert.AreEqual("Status must be one of: Draft, Sent, Paid", rule!.Describe());
    }

    [TestMethod]
    public void A_numeric_operand_is_not_a_property_comparison()
    {
        var parsed = new ComparisonRuleParser().TryParse(Ctx("Age must be at least 18"), out _);

        Assert.IsFalse(parsed, "18 is a number, not a property to compare against.");
    }

    [TestMethod]
    public void A_named_operand_is_still_a_property_comparison()
    {
        Assert.IsTrue(new ComparisonRuleParser()
            .TryParse(Ctx("EndDate must be after StartDate"), out var rule));
        Assert.AreEqual("EndDate must be after StartDate", rule!.Describe());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ParserCollisionTests"`

Expected: FAIL — `Conjoined_bounds_are_not_an_allowed_values_list` and `A_numeric_operand_is_not_a_property_comparison` fail; the other four pass. They pass because they pin behaviour that already works and must survive the change — which is exactly what makes them worth writing first.

- [ ] **Step 3: Tighten the allowed-values catch-all**

In `AllowedValuesRule.cs`, inside `AllowedValuesRuleParser.TryParse`, tighten **the guard only**. The split keeps every separator it had:

```csharp
        var raw = m.Groups["values"].Value;
        // The guard decides whether this is a list at all, and " and " is not evidence that it is:
        // in a rule sentence it nearly always joins two rules, and accepting it turned
        // "Age must be greater than 0 and less than 100" into an allowed-values check that can
        // never pass. A list is a comma list or an "or" list.
        if (!raw.Contains(',') && !raw.Contains(" or "))
            return false; // a single token is not an allowed-values list

        // The split, by contrast, keeps " and ". By the time it runs the guard has already
        // established this IS a list, and "Draft, Sent and Paid" is how people write one — dropping
        // the separator here fused the last two values into the single literal "Sent and Paid",
        // which the emitted check could never match.
        var parts = raw.Split(new[] { ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(p => p.Split(new[] { " or ", " and " }, System.StringSplitOptions.RemoveEmptyEntries))
            .Select(x => x.Trim().Trim('"', '\''))
            .Where(x => x.Length > 0)
            .ToList();
```

- [ ] **Step 4: Exclude a numeric operand from the comparison rule**

In `ComparisonRule.cs`, change the `right` group of `ComparisonRuleParser.Pattern` so an all-digit operand cannot match:

```csharp
    // <right> refuses an all-digit operand: it names a property, and "at least 18" is a bound on a
    // number, which MinValueRuleParser owns on the data side.
    private static readonly Regex Pattern = new(
        @"^(?<left>\w+)\s+must\s+(?:be\s+)?(?<op>after|before|greater\s+than\s+or\s+equal\s+to|less\s+than\s+or\s+equal\s+to|greater\s+than|less\s+than|at\s+least|at\s+most|not\s+exceed)\s+(?<right>(?!\d+$)\w+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ParserCollisionTests"`

Expected: PASS, 5 tests.

- [ ] **Step 6: Run the whole existing validation suite for regressions**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~Core.Validation"`

Expected: PASS. If an existing test asserted the old `" and "` splitting, read it before changing it — it may be documenting the defect rather than the intent.

- [ ] **Step 7: Commit**

```bash
git add CodeGenerationEngine/Core/Validation/Rules/AllowedValuesRule.cs \
        CodeGenerationEngine/Core/Validation/Rules/ComparisonRule.cs \
        CodeGenerator.Tests/Core/Validation/Rules/ParserCollisionTests.cs
git commit -m "fix: the catch-all claimed conjoined rules and the comparison claimed numbers"
```

---

### Task 2: Every parser states its phrasings, every rule states its properties

Two interface members. `Examples` is what the validation page lists when it cannot parse a sentence — built by asking the catalog, so there is no second list to forget. `Properties` is what lets the writer in the second plan refuse a rule naming a property the class does not have, instead of emitting a skipped-rule comment and reporting success.

**Files:**
- Modify: `CodeGenerationEngine/Core/Validation/IValidationRuleParser.cs`
- Modify: `CodeGenerationEngine/Core/Validation/IValidationRule.cs`
- Modify: all nine rule files in `CodeGenerationEngine/Core/Validation/Rules/`
- Test: `CodeGenerator.Tests/Core/Validation/CatalogExamplesTests.cs` (create)

**Interfaces:**
- Consumes: `ValidationRuleCatalog.Default.Parsers` (existing).
- Produces:
  - `IValidationRuleParser.Examples` → `IReadOnlyList<string>`, one entry per accepted phrasing, each a complete rule fragment that this parser parses.
  - `IValidationRule.Properties` → `IReadOnlyList<string>`, every property name the rule refers to.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Core/Validation/CatalogExamplesTests.cs`:

```csharp
using CodeGenerationEngine.Core.Validation;

namespace CodeGenerator.Tests.Core.Validation;

/// <summary>
/// Every parser's own examples must survive the catalog it lives in. This is the collision test
/// for the whole catalog at once: a new parser whose phrasing is stolen by a lower-priority
/// neighbour fails here without anyone having to think of the pair.
/// </summary>
[TestClass]
public class CatalogExamplesTests
{
    [TestMethod]
    public void Every_parser_offers_at_least_one_example()
    {
        foreach (var parser in ValidationRuleCatalog.Default.Parsers)
            Assert.IsGreaterThan(0, parser.Examples.Count, parser.GetType().Name);
    }

    [TestMethod]
    public void Every_example_is_parsed_by_the_parser_that_offered_it()
    {
        foreach (var parser in ValidationRuleCatalog.Default.Parsers)
        foreach (var example in parser.Examples)
        {
            var ctx = new ValidationParseContext { Fragment = example, ClassName = "Sample" };

            Assert.IsTrue(parser.TryParse(ctx, out var mine),
                $"{parser.GetType().Name} does not parse its own example: {example}");

            var fromCatalog = ValidationRuleCatalog.Default.Parse(example, parser.Category, ctx);

            Assert.HasCount(1, fromCatalog);
            Assert.AreEqual(mine!.Describe(), fromCatalog[0].Describe(),
                $"Another parser claimed {parser.GetType().Name}'s example: {example}");
        }
    }

    [TestMethod]
    public void Every_rule_names_the_properties_it_refers_to()
    {
        foreach (var parser in ValidationRuleCatalog.Default.Parsers)
        foreach (var example in parser.Examples)
        {
            var ctx = new ValidationParseContext { Fragment = example, ClassName = "Sample" };
            parser.TryParse(ctx, out var rule);

            Assert.IsGreaterThan(0, rule!.Properties.Count,
                $"{rule.GetType().Name} names no property for: {example}");
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~CatalogExamplesTests"`

Expected: FAIL to **compile** — `IValidationRuleParser` has no `Examples` and `IValidationRule` has no `Properties`.

- [ ] **Step 3: Add the interface members**

`IValidationRuleParser.cs`:

```csharp
public interface IValidationRuleParser
{
    /// <summary>Lower runs first. Keeps a broad catch-all (e.g. allowed-values)
    /// from swallowing a more specific pattern.</summary>
    int Priority { get; }
    ValidationCategory Category { get; }

    /// <summary>
    /// The phrasings this parser accepts, each a complete rule fragment.
    /// </summary>
    /// <remarks>
    /// Stated by the parser rather than listed anywhere else, because the validation page shows
    /// this list to someone whose sentence was refused. A hand-kept list beside a growing catalog
    /// is a list that tells people to type things the tool no longer understands.
    /// </remarks>
    IReadOnlyList<string> Examples { get; }

    bool TryParse(ValidationParseContext ctx, out IValidationRule? rule);
}
```

`IValidationRule.cs`:

```csharp
public interface IValidationRule
{
    ValidationCategory Category { get; }

    /// <summary>
    /// Every property this rule refers to.
    /// </summary>
    /// <remarks>
    /// So a caller can refuse a rule naming a property the class does not have, rather than
    /// emitting it and finding out at emit time — which is a skipped-rule comment and a reported
    /// success over a check that is not there.
    /// </remarks>
    IReadOnlyList<string> Properties { get; }

    string Describe();
    void Emit(ValidationEmitContext ctx);
}
```

- [ ] **Step 4: Implement both members on all nine existing rule files**

For each file in `CodeGenerationEngine/Core/Validation/Rules/`, add `Properties` to the rule and `Examples` to each parser. The property list is the operands the rule already holds in fields:

```csharp
// RequiredRule
public IReadOnlyList<string> Properties => [_property];

// RequiredRuleParser
public IReadOnlyList<string> Examples =>
    ["Name is required", "Name must not be blank"];

// LengthRangeRule
public IReadOnlyList<string> Properties => [_p];
// LengthRangeRuleParser
public IReadOnlyList<string> Examples => ["Name length must be between 2 and 50"];

// NumericRangeRule
public IReadOnlyList<string> Properties => [_p];
// NumericRangeRuleParser
public IReadOnlyList<string> Examples => ["Age must be between 18 and 65"];

// GreaterThanRule
public IReadOnlyList<string> Properties => [_p];
// GreaterThanRuleParser
public IReadOnlyList<string> Examples => ["Quantity must be greater than 0"];

// LessThanRule
public IReadOnlyList<string> Properties => [_p];
// LessThanRuleParser
public IReadOnlyList<string> Examples => ["Discount must be less than 100"];

// RegexRule
public IReadOnlyList<string> Properties => [_p];
// EmailRuleParser
public IReadOnlyList<string> Examples => ["Email must be a valid email"];
// RegexRuleParser
public IReadOnlyList<string> Examples => [@"Code must match ^[A-Z]{3}$"];

// AllowedValuesRule
public IReadOnlyList<string> Properties => [_p];
// AllowedValuesRuleParser
public IReadOnlyList<string> Examples =>
    ["Status must be Draft, Sent, Paid", "Status must be Draft or Paid"];

// ComparisonRule
public IReadOnlyList<string> Properties => [_left, _right];
// ComparisonRuleParser
public IReadOnlyList<string> Examples =>
    ["EndDate must be after StartDate", "Discount must not exceed Total"];

// RequiredWhenRule
public IReadOnlyList<string> Properties => [_target, _cond];
// RequiredWhenRuleParser
public IReadOnlyList<string> Examples => ["ShippedDate is required when Status is Shipped"];
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~CatalogExamplesTests"`

Expected: PASS, 3 tests.

If `Every_example_is_parsed_by_the_parser_that_offered_it` fails on the regex example, the catch-all or `EmailRuleParser` claimed it — adjust the example to one that is unambiguous rather than reordering priorities, unless the collision is real.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerationEngine/Core/Validation/ CodeGenerator.Tests/Core/Validation/CatalogExamplesTests.cs
git commit -m "feat: parsers state their phrasings and rules state their properties"
```

---

### Task 3: Inclusive numeric bounds

`at least` and `at most` are the two most common bounds in written English and neither parses today: `GreaterThanRule` and `LessThanRule` are strict-only.

**Files:**
- Create: `CodeGenerationEngine/Core/Validation/Rules/InclusiveBoundRule.cs`
- Test: `CodeGenerator.Tests/Core/Validation/Rules/InclusiveBoundRuleTests.cs`

**Interfaces:**
- Consumes: `IValidationRule.Properties`, `IValidationRuleParser.Examples` (Task 2).
- Produces: `InclusiveBoundRule(string property, decimal value, bool atLeast)`; parsers `MinValueRuleParser` and `MaxValueRuleParser`, both priority 35, category `Data`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Core/Validation/Rules/InclusiveBoundRuleTests.cs`:

```csharp
using System.Text;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class InclusiveBoundRuleTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Employee" };

    private static string Emit(IValidationRule rule, string property, string type)
    {
        var sb = new StringBuilder();
        rule.Emit(new ValidationEmitContext(
            sb, [new PropertyDefinition { Name = property, Type = type }]));
        return sb.ToString();
    }

    /// <summary>Compiles the emitted body inside a real class, which is the only thing that
    /// proves the C# is C#. A substring assertion cannot.</summary>
    private static void AssertCompiles(string body, string propertyDeclaration)
    {
        var source =
            "namespace Acme { public class Employee { " + propertyDeclaration +
            " public System.Collections.Generic.List<string> Validate() { " +
            "var errors = new System.Collections.Generic.List<string>();" +
            body + " return errors; } } }";

        var result = Compiler.Compile(source);
        Assert.IsTrue(result.Success,
            string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    [DataRow("Age must be at least 18")]
    [DataRow("Age must be no less than 18")]
    [DataRow("Age must be greater than or equal to 18")]
    public void Parses_the_at_least_phrasings(string sentence)
    {
        Assert.IsTrue(new MinValueRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual("Age must be at least 18", rule!.Describe());
        Assert.AreEqual(ValidationCategory.Data, rule.Category);
        CollectionAssert.AreEqual(new[] { "Age" }, rule.Properties.ToArray());
    }

    [TestMethod]
    [DataRow("Salary must be at most 100000")]
    [DataRow("Salary must be no more than 100000")]
    [DataRow("Salary must not exceed 100000")]
    [DataRow("Salary must be less than or equal to 100000")]
    public void Parses_the_at_most_phrasings(string sentence)
    {
        Assert.IsTrue(new MaxValueRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual("Salary must be at most 100000", rule!.Describe());
    }

    [TestMethod]
    public void A_character_bound_is_not_a_numeric_bound()
    {
        Assert.IsFalse(new MaxValueRuleParser()
            .TryParse(Ctx("Name must be at most 50 characters"), out _));
    }

    [TestMethod]
    public void Emitted_min_bound_compiles_and_checks_the_lower_edge()
    {
        new MinValueRuleParser().TryParse(Ctx("Age must be at least 18"), out var rule);
        var code = Emit(rule!, "Age", "int");

        StringAssert.Contains(code, "this.Age");
        StringAssert.Contains(code, "__n < 18m");
        AssertCompiles(code, "public int Age { get; set; }");
    }

    [TestMethod]
    public void Emitted_max_bound_compiles_and_checks_the_upper_edge()
    {
        new MaxValueRuleParser().TryParse(Ctx("Salary must be at most 100000"), out var rule);
        var code = Emit(rule!, "Salary", "decimal");

        StringAssert.Contains(code, "__n > 100000m");
        AssertCompiles(code, "public decimal Salary { get; set; }");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~InclusiveBoundRuleTests"`

Expected: FAIL to compile — `MinValueRuleParser` does not exist.

- [ ] **Step 3: Write the rule and its two parsers**

Create `CodeGenerationEngine/Core/Validation/Rules/InclusiveBoundRule.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

/// <summary>Data rule: a number at or beyond a bound. The inclusive counterpart of
/// <see cref="GreaterThanRule"/> and <see cref="LessThanRule"/>, which are strict-only —
/// so "at least 18" had nowhere to go before this.</summary>
public sealed class InclusiveBoundRule : IValidationRule
{
    private readonly string _p;
    private readonly decimal _value;
    private readonly bool _atLeast;

    public InclusiveBoundRule(string property, decimal value, bool atLeast)
    {
        _p = property;
        _value = value;
        _atLeast = atLeast;
    }

    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Properties => [_p];

    public string Describe() =>
        $"{_p} must be {(_atLeast ? "at least" : "at most")} " +
        _value.ToString(CultureInfo.InvariantCulture);

    public void Emit(ValidationEmitContext ctx)
    {
        var value = _value.ToString(CultureInfo.InvariantCulture);
        var violation = _atLeast ? "<" : ">";
        var message = Microsoft.CodeAnalysis.CSharp.SymbolDisplay
            .FormatLiteral(Describe() + ".", quote: true);

        ctx.Line($"if (decimal.TryParse(((object)this.{_p})?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var __n))");
        ctx.Line($"    if (__n {violation} {value}m) errors.Add({message});");
    }
}

public sealed class MinValueRuleParser : IValidationRuleParser
{
    // Anchored on the number, so "at most 50 characters" falls through to the length parsers.
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+(?:at\s+least|no\s+less\s+than|greater\s+than\s+or\s+equal\s+to)\s+(?<value>[-+]?[0-9]*\.?[0-9]+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 35;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
        ["Age must be at least 18", "Age must be greater than or equal to 18"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        rule = new InclusiveBoundRule(
            m.Groups["prop"].Value,
            decimal.Parse(m.Groups["value"].Value, CultureInfo.InvariantCulture),
            atLeast: true);
        return true;
    }
}

public sealed class MaxValueRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+(?:be\s+(?:at\s+most|no\s+more\s+than|less\s+than\s+or\s+equal\s+to)|not\s+exceed)\s+(?<value>[-+]?[0-9]*\.?[0-9]+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 35;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
        ["Salary must be at most 100000", "Salary must not exceed 100000"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        rule = new InclusiveBoundRule(
            m.Groups["prop"].Value,
            decimal.Parse(m.Groups["value"].Value, CultureInfo.InvariantCulture),
            atLeast: false);
        return true;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~InclusiveBoundRuleTests|FullyQualifiedName~CatalogExamplesTests"`

Expected: PASS. `CatalogExamplesTests` is included on purpose — it is the collision check for the new parsers against every existing one.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/Validation/Rules/InclusiveBoundRule.cs \
        CodeGenerator.Tests/Core/Validation/Rules/InclusiveBoundRuleTests.cs
git commit -m "feat: at least / at most bounds"
```

---

### Task 4: Sign, zero and number-shape rules

"Price must be positive" parses to nothing today.

**Files:**
- Create: `CodeGenerationEngine/Core/Validation/Rules/NumberShapeRule.cs`
- Test: `CodeGenerator.Tests/Core/Validation/Rules/NumberShapeRuleTests.cs`

**Interfaces:**
- Consumes: Task 2's interface members.
- Produces: `NumberShapeRule(string property, NumberShape shape)` with `public enum NumberShape { Positive, Negative, NonNegative, NonZero, Integral, Numeric }`; parsers `SignRuleParser` (priority 36) and `NumberFormRuleParser` (priority 37), category `Data`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Core/Validation/Rules/NumberShapeRuleTests.cs`:

```csharp
using System.Text;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class NumberShapeRuleTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Order" };

    private static string Emit(IValidationRule rule, string property, string type)
    {
        var sb = new StringBuilder();
        rule.Emit(new ValidationEmitContext(
            sb, [new PropertyDefinition { Name = property, Type = type }]));
        return sb.ToString();
    }

    private static void AssertCompiles(string body, string propertyDeclaration)
    {
        var source =
            "namespace Acme { public class Order { " + propertyDeclaration +
            " public System.Collections.Generic.List<string> Validate() { " +
            "var errors = new System.Collections.Generic.List<string>();" +
            body + " return errors; } } }";

        var result = Compiler.Compile(source);
        Assert.IsTrue(result.Success,
            string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    [DataRow("Price must be positive", "Price must be positive")]
    [DataRow("Balance must be negative", "Balance must be negative")]
    [DataRow("Quantity must not be negative", "Quantity must not be negative")]
    [DataRow("Quantity must be zero or greater", "Quantity must not be negative")]
    [DataRow("Total must not be zero", "Total must not be zero")]
    public void Parses_the_sign_phrasings(string sentence, string described)
    {
        Assert.IsTrue(new SignRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual(described, rule!.Describe());
    }

    [TestMethod]
    [DataRow("Quantity must be a whole number", "Quantity must be a whole number")]
    [DataRow("Quantity must be an integer", "Quantity must be a whole number")]
    [DataRow("Amount must be numeric", "Amount must be numeric")]
    [DataRow("Amount must be a number", "Amount must be numeric")]
    public void Parses_the_number_form_phrasings(string sentence, string described)
    {
        Assert.IsTrue(new NumberFormRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual(described, rule!.Describe());
    }

    [TestMethod]
    public void Emitted_positive_check_compiles()
    {
        new SignRuleParser().TryParse(Ctx("Price must be positive"), out var rule);
        var code = Emit(rule!, "Price", "decimal");

        StringAssert.Contains(code, "__n <= 0m");
        AssertCompiles(code, "public decimal Price { get; set; }");
    }

    [TestMethod]
    public void Emitted_whole_number_check_compiles()
    {
        new NumberFormRuleParser().TryParse(Ctx("Quantity must be a whole number"), out var rule);
        var code = Emit(rule!, "Quantity", "decimal");

        StringAssert.Contains(code, "decimal.Truncate");
        AssertCompiles(code, "public decimal Quantity { get; set; }");
    }

    [TestMethod]
    public void Emitted_numeric_check_compiles_against_a_string_property()
    {
        new NumberFormRuleParser().TryParse(Ctx("Amount must be numeric"), out var rule);
        var code = Emit(rule!, "Amount", "string");

        AssertCompiles(code, "public string Amount { get; set; } = string.Empty;");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~NumberShapeRuleTests"`

Expected: FAIL to compile — `SignRuleParser` does not exist.

- [ ] **Step 3: Write the rule and its parsers**

Create `CodeGenerationEngine/Core/Validation/Rules/NumberShapeRule.cs`:

```csharp
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

/// <summary>What shape a number has to be, as opposed to what value it has to be near.</summary>
public enum NumberShape
{
    Positive,
    Negative,
    NonNegative,
    NonZero,
    Integral,
    Numeric
}

/// <summary>Data rule: sign, zero, and whether the value is a number at all.</summary>
public sealed class NumberShapeRule : IValidationRule
{
    private readonly string _p;
    private readonly NumberShape _shape;

    public NumberShapeRule(string property, NumberShape shape)
    {
        _p = property;
        _shape = shape;
    }

    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Properties => [_p];

    public string Describe() => _shape switch
    {
        NumberShape.Positive => $"{_p} must be positive",
        NumberShape.Negative => $"{_p} must be negative",
        NumberShape.NonNegative => $"{_p} must not be negative",
        NumberShape.NonZero => $"{_p} must not be zero",
        NumberShape.Integral => $"{_p} must be a whole number",
        _ => $"{_p} must be numeric"
    };

    public void Emit(ValidationEmitContext ctx)
    {
        var message = Microsoft.CodeAnalysis.CSharp.SymbolDisplay
            .FormatLiteral(Describe() + ".", quote: true);

        ctx.Line($"var __t = ((object)this.{_p})?.ToString();");
        ctx.Line("var __parsed = decimal.TryParse(__t, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var __n);");

        // "Numeric" is the one shape where failing to parse IS the violation. Every other shape
        // is a statement about a number, so a value that is not one is left to the rule whose
        // job that is — two rules on one property is how you say both things.
        if (_shape == NumberShape.Numeric)
        {
            ctx.Line($"if (!string.IsNullOrWhiteSpace(__t) && !__parsed) errors.Add({message});");
            return;
        }

        var violation = _shape switch
        {
            NumberShape.Positive => "__n <= 0m",
            NumberShape.Negative => "__n >= 0m",
            NumberShape.NonNegative => "__n < 0m",
            NumberShape.NonZero => "__n == 0m",
            _ => "decimal.Truncate(__n) != __n"
        };

        ctx.Line($"if (__parsed && {violation}) errors.Add({message});");
    }
}

public sealed class SignRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+(?<neg>not\s+)?be\s+(?<shape>positive|negative|zero\s+or\s+greater|zero)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 36;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
    [
        "Price must be positive",
        "Quantity must not be negative",
        "Total must not be zero"
    ];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var negated = m.Groups["neg"].Success;
        var shape = Regex.Replace(m.Groups["shape"].Value.Trim(), @"\s+", " ").ToLowerInvariant();

        var resolved = (shape, negated) switch
        {
            ("positive", false) => NumberShape.Positive,
            ("negative", false) => NumberShape.Negative,
            ("negative", true) => NumberShape.NonNegative,
            ("zero or greater", false) => NumberShape.NonNegative,
            ("zero", true) => NumberShape.NonZero,
            _ => (NumberShape?)null
        };

        if (resolved is null) return false;

        rule = new NumberShapeRule(m.Groups["prop"].Value, resolved.Value);
        return true;
    }
}

public sealed class NumberFormRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+(?:a\s+whole\s+number|an\s+integer|numeric|a\s+number)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 37;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
        ["Quantity must be a whole number", "Amount must be numeric"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var whole = ctx.Fragment.Contains("whole", System.StringComparison.OrdinalIgnoreCase)
                 || ctx.Fragment.Contains("integer", System.StringComparison.OrdinalIgnoreCase);

        rule = new NumberShapeRule(
            m.Groups["prop"].Value, whole ? NumberShape.Integral : NumberShape.Numeric);
        return true;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~NumberShapeRuleTests|FullyQualifiedName~CatalogExamplesTests"`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/Validation/Rules/NumberShapeRule.cs \
        CodeGenerator.Tests/Core/Validation/Rules/NumberShapeRuleTests.cs
git commit -m "feat: sign, zero and number-shape rules"
```

---

### Task 5: Standalone length bounds

There is a length *range* rule and no standalone bound, so "Name must be at most 50 characters" does not parse.

**Files:**
- Create: `CodeGenerationEngine/Core/Validation/Rules/LengthBoundRule.cs`
- Test: `CodeGenerator.Tests/Core/Validation/Rules/LengthBoundRuleTests.cs`

**Interfaces:**
- Consumes: Task 2's interface members.
- Produces: `LengthBoundRule(string property, int length, LengthBound bound)` with `public enum LengthBound { Min, Max, Exact }`; parsers `MinLengthRuleParser`, `MaxLengthRuleParser`, `ExactLengthRuleParser`, all priority 18, category `Data`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Core/Validation/Rules/LengthBoundRuleTests.cs`:

```csharp
using System.Text;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class LengthBoundRuleTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Employee" };

    private static string Emit(IValidationRule rule)
    {
        var sb = new StringBuilder();
        rule.Emit(new ValidationEmitContext(
            sb, [new PropertyDefinition { Name = "Name", Type = "string" }]));
        return sb.ToString();
    }

    private static void AssertCompiles(string body)
    {
        var source =
            "namespace Acme { public class Employee { public string Name { get; set; } = string.Empty;" +
            " public System.Collections.Generic.List<string> Validate() { " +
            "var errors = new System.Collections.Generic.List<string>();" +
            body + " return errors; } } }";

        var result = Compiler.Compile(source);
        Assert.IsTrue(result.Success,
            string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    [DataRow("Name must be at most 50 characters")]
    [DataRow("Name must be no longer than 50 characters")]
    [DataRow("Name must not exceed 50 characters")]
    public void Parses_the_maximum_length_phrasings(string sentence)
    {
        Assert.IsTrue(new MaxLengthRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual("Name must be at most 50 characters", rule!.Describe());
    }

    [TestMethod]
    public void Parses_the_minimum_length_phrasing()
    {
        Assert.IsTrue(new MinLengthRuleParser()
            .TryParse(Ctx("Name must be at least 2 characters"), out var rule));
        Assert.AreEqual("Name must be at least 2 characters", rule!.Describe());
    }

    [TestMethod]
    public void Parses_the_exact_length_phrasing()
    {
        Assert.IsTrue(new ExactLengthRuleParser()
            .TryParse(Ctx("Name must be exactly 8 characters"), out var rule));
        Assert.AreEqual("Name must be exactly 8 characters", rule!.Describe());
    }

    /// <summary>The catalog, not the parser: this is the ordering that keeps a character
    /// bound away from the numeric bound parsers added in Task 3.</summary>
    [TestMethod]
    public void The_catalog_routes_a_character_bound_to_the_length_rule()
    {
        var ctx = Ctx("Name must be at most 50 characters");
        var rules = ValidationRuleCatalog.Default.Parse(
            ctx.Fragment, ValidationCategory.Data, ctx);

        Assert.HasCount(1, rules);
        Assert.AreEqual("Name must be at most 50 characters", rules[0].Describe());
    }

    [TestMethod]
    public void Emitted_maximum_length_check_compiles()
    {
        new MaxLengthRuleParser().TryParse(Ctx("Name must be at most 50 characters"), out var rule);
        var code = Emit(rule!);

        StringAssert.Contains(code, "__s.Length > 50");
        AssertCompiles(code);
    }

    [TestMethod]
    public void Emitted_exact_length_check_compiles()
    {
        new ExactLengthRuleParser().TryParse(Ctx("Name must be exactly 8 characters"), out var rule);
        var code = Emit(rule!);

        StringAssert.Contains(code, "__s.Length != 8");
        AssertCompiles(code);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~LengthBoundRuleTests"`

Expected: FAIL to compile — `MaxLengthRuleParser` does not exist.

- [ ] **Step 3: Write the rule and its three parsers**

Create `CodeGenerationEngine/Core/Validation/Rules/LengthBoundRule.cs`:

```csharp
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

public enum LengthBound
{
    Min,
    Max,
    Exact
}

/// <summary>Data rule: one edge of a string's length. The standalone counterpart of
/// <see cref="LengthRangeRule"/>, which needs both edges named.</summary>
public sealed class LengthBoundRule : IValidationRule
{
    private readonly string _p;
    private readonly int _length;
    private readonly LengthBound _bound;

    public LengthBoundRule(string property, int length, LengthBound bound)
    {
        _p = property;
        _length = length;
        _bound = bound;
    }

    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Properties => [_p];

    public string Describe() => _bound switch
    {
        LengthBound.Min => $"{_p} must be at least {_length} characters",
        LengthBound.Max => $"{_p} must be at most {_length} characters",
        _ => $"{_p} must be exactly {_length} characters"
    };

    public void Emit(ValidationEmitContext ctx)
    {
        var violation = _bound switch
        {
            LengthBound.Min => $"__s.Length < {_length}",
            LengthBound.Max => $"__s.Length > {_length}",
            _ => $"__s.Length != {_length}"
        };

        var message = Microsoft.CodeAnalysis.CSharp.SymbolDisplay
            .FormatLiteral(Describe() + ".", quote: true);

        ctx.Line($"var __s = ((object)this.{_p})?.ToString();");
        ctx.Line($"if (__s != null && {violation}) errors.Add({message});");
    }
}

public sealed class MinLengthRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+(?:at\s+least|no\s+shorter\s+than)\s+(?<n>\d+)\s+characters?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 18;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples => ["Name must be at least 2 characters"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        rule = new LengthBoundRule(
            m.Groups["prop"].Value, int.Parse(m.Groups["n"].Value), LengthBound.Min);
        return true;
    }
}

public sealed class MaxLengthRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+(?:be\s+(?:at\s+most|no\s+longer\s+than)|not\s+exceed)\s+(?<n>\d+)\s+characters?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 18;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
        ["Name must be at most 50 characters", "Name must not exceed 50 characters"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        rule = new LengthBoundRule(
            m.Groups["prop"].Value, int.Parse(m.Groups["n"].Value), LengthBound.Max);
        return true;
    }
}

public sealed class ExactLengthRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+exactly\s+(?<n>\d+)\s+characters?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 18;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples => ["Code must be exactly 8 characters"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        rule = new LengthBoundRule(
            m.Groups["prop"].Value, int.Parse(m.Groups["n"].Value), LengthBound.Exact);
        return true;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~LengthBoundRuleTests|FullyQualifiedName~CatalogExamplesTests"`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/Validation/Rules/LengthBoundRule.cs \
        CodeGenerator.Tests/Core/Validation/Rules/LengthBoundRuleTests.cs
git commit -m "feat: standalone length bounds"
```

---

### Task 6: Text shape — prefix, suffix, substring, case, character class

**Files:**
- Create: `CodeGenerationEngine/Core/Validation/Rules/TextShapeRule.cs`
- Test: `CodeGenerator.Tests/Core/Validation/Rules/TextShapeRuleTests.cs`

**Interfaces:**
- Consumes: Task 2's interface members.
- Produces: `TextShapeRule(string property, TextShape shape, string operand)` with `public enum TextShape { StartsWith, EndsWith, Contains, DoesNotContain, Uppercase, Lowercase, Digits, Letters, Alphanumeric }`; parsers `AffixRuleParser` and `ContainsRuleParser` (priority 62), `CaseRuleParser` (63), `CharacterClassRuleParser` (64), category `Data`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Core/Validation/Rules/TextShapeRuleTests.cs`:

```csharp
using System.Text;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class TextShapeRuleTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Product" };

    private static string Emit(IValidationRule rule)
    {
        var sb = new StringBuilder();
        rule.Emit(new ValidationEmitContext(
            sb, [new PropertyDefinition { Name = "Sku", Type = "string" }]));
        return sb.ToString();
    }

    private static void AssertCompiles(string body)
    {
        var source =
            "namespace Acme { public class Product { public string Sku { get; set; } = string.Empty;" +
            " public System.Collections.Generic.List<string> Validate() { " +
            "var errors = new System.Collections.Generic.List<string>();" +
            body + " return errors; } } }";

        var result = Compiler.Compile(source);
        Assert.IsTrue(result.Success,
            string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    [DataRow("Sku must start with \"AB\"", "Sku must start with AB")]
    [DataRow("Sku must start with AB", "Sku must start with AB")]
    [DataRow("Sku must end with \"Z\"", "Sku must end with Z")]
    public void Parses_the_affix_phrasings(string sentence, string described)
    {
        Assert.IsTrue(new AffixRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual(described, rule!.Describe());
    }

    [TestMethod]
    [DataRow("Sku must contain \"-\"", "Sku must contain -")]
    [DataRow("Sku must not contain \" \"", "Sku must not contain  ")]
    public void Parses_the_substring_phrasings(string sentence, string described)
    {
        Assert.IsTrue(new ContainsRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual(described, rule!.Describe());
    }

    [TestMethod]
    [DataRow("Sku must be uppercase")]
    [DataRow("Sku must be lowercase")]
    public void Parses_the_case_phrasings(string sentence)
    {
        Assert.IsTrue(new CaseRuleParser().TryParse(Ctx(sentence), out _));
    }

    [TestMethod]
    [DataRow("Sku must be digits only", "Sku must be digits only")]
    [DataRow("Sku must be letters only", "Sku must be letters only")]
    [DataRow("Sku must be alphanumeric", "Sku must be alphanumeric")]
    public void Parses_the_character_class_phrasings(string sentence, string described)
    {
        Assert.IsTrue(new CharacterClassRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual(described, rule!.Describe());
    }

    /// <summary>A quote in the operand must not be able to end the emitted string literal.</summary>
    [TestMethod]
    public void An_operand_containing_a_quote_still_compiles()
    {
        new AffixRuleParser().TryParse(Ctx("Sku must start with \"A\"\"B\""), out var rule);
        AssertCompiles(Emit(rule!));
    }

    [TestMethod]
    public void Emitted_starts_with_check_compiles()
    {
        new AffixRuleParser().TryParse(Ctx("Sku must start with \"AB\""), out var rule);
        var code = Emit(rule!);

        StringAssert.Contains(code, "StartsWith");
        AssertCompiles(code);
    }

    [TestMethod]
    public void Emitted_digits_only_check_compiles()
    {
        new CharacterClassRuleParser().TryParse(Ctx("Sku must be digits only"), out var rule);
        var code = Emit(rule!);

        StringAssert.Contains(code, "IsMatch");
        AssertCompiles(code);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~TextShapeRuleTests"`

Expected: FAIL to compile — `AffixRuleParser` does not exist.

- [ ] **Step 3: Write the rule and its four parsers**

Create `CodeGenerationEngine/Core/Validation/Rules/TextShapeRule.cs`:

```csharp
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerationEngine.Core.Validation.Rules;

public enum TextShape
{
    StartsWith,
    EndsWith,
    Contains,
    DoesNotContain,
    Uppercase,
    Lowercase,
    Digits,
    Letters,
    Alphanumeric
}

/// <summary>Data rule: what a string looks like, short of a full regex.</summary>
/// <remarks>
/// These describe themselves in the words that were typed rather than as the pattern they
/// compile to — unlike the named formats, which are openly sugar over <see cref="RegexRule"/>.
/// "Sku must be digits only" is what someone wants to read back, not <c>^[0-9]+$</c>.
/// </remarks>
public sealed class TextShapeRule : IValidationRule
{
    private readonly string _p;
    private readonly TextShape _shape;
    private readonly string _operand;

    public TextShapeRule(string property, TextShape shape, string operand = "")
    {
        _p = property;
        _shape = shape;
        _operand = operand;
    }

    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Properties => [_p];

    public string Describe() => _shape switch
    {
        TextShape.StartsWith => $"{_p} must start with {_operand}",
        TextShape.EndsWith => $"{_p} must end with {_operand}",
        TextShape.Contains => $"{_p} must contain {_operand}",
        TextShape.DoesNotContain => $"{_p} must not contain {_operand}",
        TextShape.Uppercase => $"{_p} must be uppercase",
        TextShape.Lowercase => $"{_p} must be lowercase",
        TextShape.Digits => $"{_p} must be digits only",
        TextShape.Letters => $"{_p} must be letters only",
        _ => $"{_p} must be alphanumeric"
    };

    public void Emit(ValidationEmitContext ctx)
    {
        var message = SymbolDisplay.FormatLiteral(Describe() + ".", quote: true);
        var operand = SymbolDisplay.FormatLiteral(_operand, quote: true);

        ctx.Line($"var __s = ((object)this.{_p})?.ToString();");

        var violation = _shape switch
        {
            TextShape.StartsWith => $"!__s.StartsWith({operand}, System.StringComparison.Ordinal)",
            TextShape.EndsWith => $"!__s.EndsWith({operand}, System.StringComparison.Ordinal)",
            TextShape.Contains => $"!__s.Contains({operand}, System.StringComparison.Ordinal)",
            TextShape.DoesNotContain => $"__s.Contains({operand}, System.StringComparison.Ordinal)",
            TextShape.Uppercase => "__s != __s.ToUpperInvariant()",
            TextShape.Lowercase => "__s != __s.ToLowerInvariant()",
            TextShape.Digits => "!System.Text.RegularExpressions.Regex.IsMatch(__s, @\"^[0-9]+$\")",
            TextShape.Letters => "!System.Text.RegularExpressions.Regex.IsMatch(__s, @\"^[A-Za-z]+$\")",
            _ => "!System.Text.RegularExpressions.Regex.IsMatch(__s, @\"^[A-Za-z0-9]+$\")"
        };

        // A blank value is the required rule's business, not this one's. Two rules on one property
        // is how you say "present, and shaped like this".
        ctx.Line($"if (!string.IsNullOrEmpty(__s) && {violation}) errors.Add({message});");
    }
}

/// <summary>Shared by the parsers that take a quoted or bare operand.</summary>
internal static class TextOperand
{
    public static string Unquote(string raw) => raw.Trim().Trim('"', '\'');
}

public sealed class AffixRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+(?<edge>start|end)\s+with\s+(?<text>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 62;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
        ["Sku must start with \"AB\"", "Sku must end with \"Z\""];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var shape = m.Groups["edge"].Value.Equals("start", System.StringComparison.OrdinalIgnoreCase)
            ? TextShape.StartsWith
            : TextShape.EndsWith;

        rule = new TextShapeRule(
            m.Groups["prop"].Value, shape, TextOperand.Unquote(m.Groups["text"].Value));
        return true;
    }
}

public sealed class ContainsRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+(?<neg>not\s+)?contain\s+(?<text>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 62;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
        ["Sku must contain \"-\"", "Notes must not contain \"TODO\""];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var shape = m.Groups["neg"].Success ? TextShape.DoesNotContain : TextShape.Contains;

        rule = new TextShapeRule(
            m.Groups["prop"].Value, shape, TextOperand.Unquote(m.Groups["text"].Value));
        return true;
    }
}

public sealed class CaseRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+(?<case>uppercase|lowercase|upper\s+case|lower\s+case)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 63;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples => ["Code must be uppercase", "Slug must be lowercase"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var upper = m.Groups["case"].Value.StartsWith("upper", System.StringComparison.OrdinalIgnoreCase);

        rule = new TextShapeRule(
            m.Groups["prop"].Value, upper ? TextShape.Uppercase : TextShape.Lowercase);
        return true;
    }
}

public sealed class CharacterClassRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+(?<class>digits\s+only|numbers\s+only|letters\s+only|alphanumeric)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 64;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
    [
        "Pin must be digits only",
        "Surname must be letters only",
        "Code must be alphanumeric"
    ];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var raw = Regex.Replace(m.Groups["class"].Value.Trim(), @"\s+", " ").ToLowerInvariant();

        var shape = raw switch
        {
            "digits only" or "numbers only" => TextShape.Digits,
            "letters only" => TextShape.Letters,
            _ => TextShape.Alphanumeric
        };

        rule = new TextShapeRule(m.Groups["prop"].Value, shape);
        return true;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~TextShapeRuleTests|FullyQualifiedName~CatalogExamplesTests"`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/Validation/Rules/TextShapeRule.cs \
        CodeGenerator.Tests/Core/Validation/Rules/TextShapeRuleTests.cs
git commit -m "feat: prefix, suffix, substring, case and character-class rules"
```

---

### Task 7: Named formats

Sugar over `RegexRule`, exactly as `EmailRuleParser` already is — one parser holding a table of name to pattern, so a new format is a row rather than a class.

**Files:**
- Create: `CodeGenerationEngine/Core/Validation/Rules/NamedFormatRule.cs`
- Test: `CodeGenerator.Tests/Core/Validation/Rules/NamedFormatRuleTests.cs`

**Interfaces:**
- Consumes: `RegexRule` (existing, `Rules/RegexRule.cs`), Task 2's interface members.
- Produces: `NamedFormatRuleParser` (priority 65, category `Data`), producing a `RegexRule`; `ValidDateRule(string property)` with `ValidDateRuleParser` (priority 66, category `Data`).

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Core/Validation/Rules/NamedFormatRuleTests.cs`:

```csharp
using System.Text;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class NamedFormatRuleTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Contact" };

    private static string Emit(IValidationRule rule, string property, string type)
    {
        var sb = new StringBuilder();
        rule.Emit(new ValidationEmitContext(
            sb, [new PropertyDefinition { Name = property, Type = type }]));
        return sb.ToString();
    }

    private static void AssertCompiles(string body, string propertyDeclaration)
    {
        var source =
            "namespace Acme { public class Contact { " + propertyDeclaration +
            " public System.Collections.Generic.List<string> Validate() { " +
            "var errors = new System.Collections.Generic.List<string>();" +
            body + " return errors; } } }";

        var result = Compiler.Compile(source);
        Assert.IsTrue(result.Success,
            string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    [DataRow("Phone must be a valid phone number")]
    [DataRow("Website must be a valid URL")]
    [DataRow("Postcode must be a valid postcode")]
    [DataRow("Reference must be a valid GUID")]
    [DataRow("Host must be a valid IP address")]
    public void Parses_the_named_formats(string sentence)
    {
        Assert.IsTrue(new NamedFormatRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual(ValidationCategory.Data, rule!.Category);
        StringAssert.Contains(rule.Describe(), "must match");
    }

    [TestMethod]
    public void Every_named_format_emits_a_pattern_that_is_a_valid_regex()
    {
        foreach (var example in new NamedFormatRuleParser().Examples)
        {
            new NamedFormatRuleParser().TryParse(Ctx(example), out var rule);

            var pattern = rule!.Describe()["Phone must match ".Length..];
            _ = new System.Text.RegularExpressions.Regex(pattern); // throws if malformed
        }
    }

    [TestMethod]
    public void Emitted_phone_check_compiles()
    {
        new NamedFormatRuleParser().TryParse(Ctx("Phone must be a valid phone number"), out var rule);
        var code = Emit(rule!, "Phone", "string");

        AssertCompiles(code, "public string Phone { get; set; } = string.Empty;");
    }

    [TestMethod]
    public void Parses_and_emits_a_valid_date_check()
    {
        Assert.IsTrue(new ValidDateRuleParser()
            .TryParse(Ctx("Born must be a valid date"), out var rule));
        Assert.AreEqual("Born must be a valid date", rule!.Describe());

        var code = Emit(rule, "Born", "string");
        StringAssert.Contains(code, "DateTime.TryParse");
        StringAssert.Contains(code, "InvariantCulture");
        AssertCompiles(code, "public string Born { get; set; } = string.Empty;");
    }
}
```

Note: `Every_named_format_emits_a_pattern_that_is_a_valid_regex` slices off the `"<Prop> must match "` prefix using the length of the `Phone` example's prefix; keep every example's property name `Phone`-length or compute the prefix with `IndexOf("must match ")`. Use the latter — replace that line with:

```csharp
            var at = rule!.Describe().IndexOf("must match ", System.StringComparison.Ordinal);
            var pattern = rule.Describe()[(at + "must match ".Length)..];
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~NamedFormatRuleTests"`

Expected: FAIL to compile — `NamedFormatRuleParser` does not exist.

- [ ] **Step 3: Write the parsers**

Create `CodeGenerationEngine/Core/Validation/Rules/NamedFormatRule.cs`:

```csharp
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerationEngine.Core.Validation.Rules;

/// <summary>Data rule: the value has to parse as a date.</summary>
/// <remarks>
/// Not a regex, unlike the rest of this file: dates are not a shape, and a pattern that accepted
/// 31/02 would be worse than no rule. Invariant culture and <c>DateTimeStyles.None</c>, so
/// 03/04/2026 does not mean March on one machine and April on the next.
/// </remarks>
public sealed class ValidDateRule : IValidationRule
{
    private readonly string _p;

    public ValidDateRule(string property) => _p = property;

    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Properties => [_p];

    public string Describe() => $"{_p} must be a valid date";

    public void Emit(ValidationEmitContext ctx)
    {
        var message = SymbolDisplay.FormatLiteral(Describe() + ".", quote: true);

        ctx.Line($"var __t = ((object)this.{_p})?.ToString();");
        ctx.Line("var __ok = string.IsNullOrWhiteSpace(__t) || System.DateTime.TryParse(__t, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);");
        ctx.Line($"if (!__ok) errors.Add({message});");
    }
}

/// <summary>The formats people ask for by name. Each is sugar over <see cref="RegexRule"/>,
/// the way <c>EmailRuleParser</c> already is — which is why they describe themselves as the
/// pattern they became.</summary>
public sealed class NamedFormatRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+(?:a\s+|an\s+)?valid\s+(?<format>phone\s+number|phone|url|web\s+address|postcode|post\s+code|guid|uuid|ip\s+address|ip)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Deliberately loose where the real-world format is: a phone number pattern that
    /// rejects a legitimate number is a pattern that gets deleted.</summary>
    private static readonly Dictionary<string, string> Patterns = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["phone number"] = @"^\+?[0-9 ()\-]{7,20}$",
        ["phone"] = @"^\+?[0-9 ()\-]{7,20}$",
        ["url"] = @"^https?://[^\s]+$",
        ["web address"] = @"^https?://[^\s]+$",
        ["postcode"] = @"^[A-Za-z]{1,2}[0-9][0-9A-Za-z]?\s*[0-9][A-Za-z]{2}$",
        ["post code"] = @"^[A-Za-z]{1,2}[0-9][0-9A-Za-z]?\s*[0-9][A-Za-z]{2}$",
        ["guid"] = @"^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$",
        ["uuid"] = @"^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$",
        ["ip address"] = @"^((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$",
        ["ip"] = @"^((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$"
    };

    public int Priority => 65;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
    [
        "Phone must be a valid phone number",
        "Website must be a valid URL",
        "Postcode must be a valid postcode",
        "Reference must be a valid GUID",
        "Host must be a valid IP address"
    ];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var format = Regex.Replace(m.Groups["format"].Value.Trim(), @"\s+", " ");
        if (!Patterns.TryGetValue(format, out var pattern)) return false;

        rule = new RegexRule(m.Groups["prop"].Value, pattern);
        return true;
    }
}

public sealed class ValidDateRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+a\s+valid\s+date$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 66;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples => ["Born must be a valid date"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        rule = new ValidDateRule(m.Groups["prop"].Value);
        return true;
    }
}
```

Check `RegexRule`'s constructor accessibility while implementing — if it is not public, make it public in the same commit and say so in the message.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~NamedFormatRuleTests|FullyQualifiedName~CatalogExamplesTests"`

Expected: PASS. `EmailRuleParser` is priority 60 and its pattern requires the literal word `email`, so it does not claim these.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/Validation/Rules/NamedFormatRule.cs \
        CodeGenerator.Tests/Core/Validation/Rules/NamedFormatRuleTests.cs
git commit -m "feat: named formats - phone, URL, postcode, GUID, IP, date"
```

---

### Task 8: Relative dates

**Files:**
- Create: `CodeGenerationEngine/Core/Validation/Rules/RelativeDateRule.cs`
- Test: `CodeGenerator.Tests/Core/Validation/Rules/RelativeDateRuleTests.cs`

**Interfaces:**
- Consumes: Task 2's interface members.
- Produces: `RelativeDateRule(string property, DateBound bound, int amount = 0)` with `public enum DateBound { Past, Future, NotPast, NotFuture, YearsAgo, WithinDays }`; parsers `RelativeDateRuleParser` (priority 70) and `DateDistanceRuleParser` (priority 71), category `Data`.

**Deviation from the spec, deliberate:** the spec says the emitted check reads the property's declared type out of `ValidationEmitContext.Properties` and only falls back to `TryParse`. The emitted code below decides at **runtime** with `is System.DateTime` instead. It covers the same cases plus `DateTime?`, `object` and `string` properties with one shape, and it does not depend on how the declared type happens to be spelled (`DateTime`, `System.DateTime`, `DateTime?`). The culture guarantee the spec asked for is unchanged.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Core/Validation/Rules/RelativeDateRuleTests.cs`:

```csharp
using System.Text;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class RelativeDateRuleTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Person" };

    private static string Emit(IValidationRule rule, string property, string type)
    {
        var sb = new StringBuilder();
        rule.Emit(new ValidationEmitContext(
            sb, [new PropertyDefinition { Name = property, Type = type }]));
        return sb.ToString();
    }

    private static void AssertCompiles(string body, string propertyDeclaration)
    {
        var source =
            "namespace Acme { public class Person { " + propertyDeclaration +
            " public System.Collections.Generic.List<string> Validate() { " +
            "var errors = new System.Collections.Generic.List<string>();" +
            body + " return errors; } } }";

        var result = Compiler.Compile(source);
        Assert.IsTrue(result.Success,
            string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    [DataRow("DateOfBirth must be in the past", "DateOfBirth must be in the past")]
    [DataRow("ExpiryDate must be in the future", "ExpiryDate must be in the future")]
    [DataRow("StartDate must not be in the past", "StartDate must not be in the past")]
    [DataRow("StartDate must not be in the future", "StartDate must not be in the future")]
    public void Parses_the_relative_phrasings(string sentence, string described)
    {
        Assert.IsTrue(new RelativeDateRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual(described, rule!.Describe());
    }

    [TestMethod]
    [DataRow("DateOfBirth must be at least 18 years ago", "DateOfBirth must be at least 18 years ago")]
    [DataRow("Modified must be within the last 30 days", "Modified must be within the last 30 days")]
    public void Parses_the_distance_phrasings(string sentence, string described)
    {
        Assert.IsTrue(new DateDistanceRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual(described, rule!.Describe());
    }

    [TestMethod]
    public void Emitted_check_compiles_against_a_DateTime_property()
    {
        new RelativeDateRuleParser().TryParse(Ctx("DateOfBirth must be in the past"), out var rule);
        var code = Emit(rule!, "DateOfBirth", "System.DateTime");

        AssertCompiles(code, "public System.DateTime DateOfBirth { get; set; }");
    }

    [TestMethod]
    public void Emitted_check_compiles_against_a_nullable_DateTime_property()
    {
        new RelativeDateRuleParser().TryParse(Ctx("ExpiryDate must be in the future"), out var rule);
        var code = Emit(rule!, "ExpiryDate", "System.DateTime?");

        AssertCompiles(code, "public System.DateTime? ExpiryDate { get; set; }");
    }

    [TestMethod]
    public void Emitted_check_compiles_against_a_string_property_and_parses_invariantly()
    {
        new RelativeDateRuleParser().TryParse(Ctx("DateOfBirth must be in the past"), out var rule);
        var code = Emit(rule!, "DateOfBirth", "string");

        StringAssert.Contains(code, "InvariantCulture");
        StringAssert.Contains(code, "DateTimeStyles.None");
        AssertCompiles(code, "public string DateOfBirth { get; set; } = string.Empty;");
    }

    [TestMethod]
    public void Emitted_years_ago_check_compiles()
    {
        new DateDistanceRuleParser()
            .TryParse(Ctx("DateOfBirth must be at least 18 years ago"), out var rule);
        var code = Emit(rule!, "DateOfBirth", "System.DateTime");

        StringAssert.Contains(code, "AddYears(-18)");
        AssertCompiles(code, "public System.DateTime DateOfBirth { get; set; }");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~RelativeDateRuleTests"`

Expected: FAIL to compile — `RelativeDateRuleParser` does not exist.

- [ ] **Step 3: Write the rule and its parsers**

Create `CodeGenerationEngine/Core/Validation/Rules/RelativeDateRule.cs`:

```csharp
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerationEngine.Core.Validation.Rules;

public enum DateBound
{
    Past,
    Future,
    NotPast,
    NotFuture,
    YearsAgo,
    WithinDays
}

/// <summary>Data rule: where a date sits relative to now.</summary>
public sealed class RelativeDateRule : IValidationRule
{
    private readonly string _p;
    private readonly DateBound _bound;
    private readonly int _amount;

    public RelativeDateRule(string property, DateBound bound, int amount = 0)
    {
        _p = property;
        _bound = bound;
        _amount = amount;
    }

    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Properties => [_p];

    public string Describe() => _bound switch
    {
        DateBound.Past => $"{_p} must be in the past",
        DateBound.Future => $"{_p} must be in the future",
        DateBound.NotPast => $"{_p} must not be in the past",
        DateBound.NotFuture => $"{_p} must not be in the future",
        DateBound.YearsAgo => $"{_p} must be at least {_amount} years ago",
        _ => $"{_p} must be within the last {_amount} days"
    };

    public void Emit(ValidationEmitContext ctx)
    {
        var message = SymbolDisplay.FormatLiteral(Describe() + ".", quote: true);

        // Decided at runtime rather than from the declared type: this one shape covers DateTime,
        // DateTime?, object and a date held as a string, and does not care how the type was
        // spelled in the source.
        ctx.Line($"var __o = (object)this.{_p};");
        ctx.Line("System.DateTime __d;");
        ctx.Line("var __ok = __o is System.DateTime;");
        ctx.Line("if (__ok) __d = (System.DateTime)__o;");
        ctx.Line("else __ok = System.DateTime.TryParse(__o?.ToString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out __d);");

        var violation = _bound switch
        {
            DateBound.Past => "__d >= System.DateTime.Now",
            DateBound.Future => "__d <= System.DateTime.Now",
            DateBound.NotPast => "__d < System.DateTime.Today",
            DateBound.NotFuture => "__d > System.DateTime.Now",
            DateBound.YearsAgo => $"__d > System.DateTime.Today.AddYears(-{_amount})",
            _ => $"__d < System.DateTime.Today.AddDays(-{_amount})"
        };

        ctx.Line($"if (__ok && {violation}) errors.Add({message});");
    }
}

public sealed class RelativeDateRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+(?<neg>not\s+)?be\s+in\s+the\s+(?<when>past|future)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 70;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
    [
        "DateOfBirth must be in the past",
        "ExpiryDate must be in the future",
        "StartDate must not be in the past"
    ];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var past = m.Groups["when"].Value.Equals("past", System.StringComparison.OrdinalIgnoreCase);
        var negated = m.Groups["neg"].Success;

        var bound = (past, negated) switch
        {
            (true, false) => DateBound.Past,
            (true, true) => DateBound.NotPast,
            (false, false) => DateBound.Future,
            _ => DateBound.NotFuture
        };

        rule = new RelativeDateRule(m.Groups["prop"].Value, bound);
        return true;
    }
}

public sealed class DateDistanceRuleParser : IValidationRuleParser
{
    private static readonly Regex YearsAgo = new(
        @"^(?<prop>\w+)\s+must\s+be\s+at\s+least\s+(?<n>\d+)\s+years?\s+ago$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WithinDays = new(
        @"^(?<prop>\w+)\s+must\s+be\s+within\s+the\s+last\s+(?<n>\d+)\s+days?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 71;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
        ["DateOfBirth must be at least 18 years ago", "Modified must be within the last 30 days"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        rule = null;

        var years = YearsAgo.Match(ctx.Fragment);
        if (years.Success)
        {
            rule = new RelativeDateRule(
                years.Groups["prop"].Value, DateBound.YearsAgo, int.Parse(years.Groups["n"].Value));
            return true;
        }

        var days = WithinDays.Match(ctx.Fragment);
        if (days.Success)
        {
            rule = new RelativeDateRule(
                days.Groups["prop"].Value, DateBound.WithinDays, int.Parse(days.Groups["n"].Value));
            return true;
        }

        return false;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~RelativeDateRuleTests|FullyQualifiedName~CatalogExamplesTests"`

Expected: PASS. Watch `DateOfBirth must be at least 18 years ago` — `MinValueRuleParser` (35) anchors on `$` after the number, so `years ago` keeps it out.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/Validation/Rules/RelativeDateRule.cs \
        CodeGenerator.Tests/Core/Validation/Rules/RelativeDateRuleTests.cs
git commit -m "feat: relative date rules"
```

---

### Task 9: Cross-property business rules

**Files:**
- Create: `CodeGenerationEngine/Core/Validation/Rules/EitherOrRule.cs`
- Create: `CodeGenerationEngine/Core/Validation/Rules/ArithmeticRule.cs`
- Create: `CodeGenerationEngine/Core/Validation/Rules/SameAsRule.cs`
- Test: `CodeGenerator.Tests/Core/Validation/Rules/CrossPropertyRuleTests.cs`

**Interfaces:**
- Consumes: `ValidationEmitContext.HasProperty` (existing), Task 2's interface members.
- Produces: `EitherOrRule(string a, string b)` + `EitherOrRuleParser` (priority 12); `ArithmeticRule(string target, string left, string op, string right)` + `ArithmeticRuleParser` (priority 15); `SameAsRule(string a, string b, bool same)` + `SameAsRuleParser` (priority 16). All category `Business`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Core/Validation/Rules/CrossPropertyRuleTests.cs`:

```csharp
using System.Text;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class CrossPropertyRuleTests
{
    private static ValidationParseContext Ctx(string fragment) =>
        new() { Fragment = fragment, ClassName = "Order" };

    private static string Emit(IValidationRule rule, params (string Name, string Type)[] props)
    {
        var sb = new StringBuilder();
        var defs = props.Select(p => new PropertyDefinition { Name = p.Name, Type = p.Type }).ToList();
        rule.Emit(new ValidationEmitContext(sb, defs));
        return sb.ToString();
    }

    private static void AssertCompiles(string body, string declarations)
    {
        var source =
            "namespace Acme { public class Order { " + declarations +
            " public System.Collections.Generic.List<string> Validate() { " +
            "var errors = new System.Collections.Generic.List<string>();" +
            body + " return errors; } } }";

        var result = Compiler.Compile(source);
        Assert.IsTrue(result.Success,
            string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    public void Parses_either_or()
    {
        Assert.IsTrue(new EitherOrRuleParser()
            .TryParse(Ctx("Either Email or Phone must be provided"), out var rule));
        Assert.AreEqual("Either Email or Phone must be provided", rule!.Describe());
        Assert.AreEqual(ValidationCategory.Business, rule.Category);
        CollectionAssert.AreEquivalent(new[] { "Email", "Phone" }, rule.Properties.ToArray());
    }

    [TestMethod]
    [DataRow("Total must equal Quantity times Price")]
    [DataRow("Total must equal Quantity plus Price")]
    [DataRow("Total must equal Quantity minus Price")]
    [DataRow("Total must equal Quantity divided by Price")]
    public void Parses_the_arithmetic_phrasings(string sentence)
    {
        Assert.IsTrue(new ArithmeticRuleParser().TryParse(Ctx(sentence), out var rule));
        Assert.AreEqual(sentence, rule!.Describe());
        CollectionAssert.AreEquivalent(
            new[] { "Total", "Quantity", "Price" }, rule.Properties.ToArray());
    }

    [TestMethod]
    [DataRow("BillingPostcode must be the same as ShippingPostcode", true)]
    [DataRow("BillingPostcode must be different from ShippingPostcode", false)]
    [DataRow("BillingPostcode must not be the same as ShippingPostcode", false)]
    public void Parses_the_sameness_phrasings(string sentence, bool same)
    {
        Assert.IsTrue(new SameAsRuleParser().TryParse(Ctx(sentence), out var rule));
        StringAssert.Contains(rule!.Describe(), same ? "the same as" : "different from");
    }

    [TestMethod]
    public void Emitted_either_or_check_compiles()
    {
        new EitherOrRuleParser().TryParse(Ctx("Either Email or Phone must be provided"), out var rule);
        var code = Emit(rule!, ("Email", "string"), ("Phone", "string"));

        AssertCompiles(code,
            "public string Email { get; set; } = string.Empty;" +
            "public string Phone { get; set; } = string.Empty;");
    }

    [TestMethod]
    public void Emitted_arithmetic_check_compiles()
    {
        new ArithmeticRuleParser().TryParse(Ctx("Total must equal Quantity times Price"), out var rule);
        var code = Emit(rule!, ("Total", "decimal"), ("Quantity", "int"), ("Price", "decimal"));

        AssertCompiles(code,
            "public decimal Total { get; set; } public int Quantity { get; set; }" +
            "public decimal Price { get; set; }");
    }

    [TestMethod]
    public void An_unknown_operand_is_skipped_with_a_comment_rather_than_emitted()
    {
        new ArithmeticRuleParser().TryParse(Ctx("Total must equal Quantity times Price"), out var rule);
        var code = Emit(rule!, ("Total", "decimal"), ("Quantity", "int")); // Price missing

        StringAssert.Contains(code, "// business rule skipped");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~CrossPropertyRuleTests"`

Expected: FAIL to compile — `EitherOrRuleParser` does not exist.

- [ ] **Step 3: Write `EitherOrRule.cs`**

```csharp
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerationEngine.Core.Validation.Rules;

/// <summary>Business rule: at least one of two properties has to carry a value.</summary>
public sealed class EitherOrRule : IValidationRule
{
    private readonly string _a;
    private readonly string _b;

    public EitherOrRule(string a, string b)
    {
        _a = a;
        _b = b;
    }

    public ValidationCategory Category => ValidationCategory.Business;

    public IReadOnlyList<string> Properties => [_a, _b];

    public string Describe() => $"Either {_a} or {_b} must be provided";

    public void Emit(ValidationEmitContext ctx)
    {
        if (!ctx.HasProperty(_a) || !ctx.HasProperty(_b))
        {
            ctx.Line($"// business rule skipped: '{Describe()}' references an unknown property");
            return;
        }

        var message = SymbolDisplay.FormatLiteral(Describe() + ".", quote: true);

        ctx.Line($"var __a = ((object)this.{_a})?.ToString();");
        ctx.Line($"var __b = ((object)this.{_b})?.ToString();");
        ctx.Line($"if (string.IsNullOrWhiteSpace(__a) && string.IsNullOrWhiteSpace(__b)) errors.Add({message});");
    }
}

public sealed class EitherOrRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^either\s+(?<a>\w+)\s+or\s+(?<b>\w+)\s+must\s+be\s+(?:provided|present|supplied|set)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 12;
    public ValidationCategory Category => ValidationCategory.Business;

    public IReadOnlyList<string> Examples => ["Either Email or Phone must be provided"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        rule = new EitherOrRule(m.Groups["a"].Value, m.Groups["b"].Value);
        return true;
    }
}
```

- [ ] **Step 4: Write `ArithmeticRule.cs`**

```csharp
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerationEngine.Core.Validation.Rules;

/// <summary>Business rule: one property has to equal an arithmetic combination of two others.</summary>
/// <remarks>
/// Compared as decimals with a small tolerance rather than for exact equality: the operands may be
/// doubles, and a rule that fires because 0.1 + 0.2 is not 0.3 is a rule people switch off.
/// </remarks>
public sealed class ArithmeticRule : IValidationRule
{
    private readonly string _target;
    private readonly string _left;
    private readonly string _op;      // the word, as typed
    private readonly string _right;

    public ArithmeticRule(string target, string left, string op, string right)
    {
        _target = target;
        _left = left;
        _op = op;
        _right = right;
    }

    public ValidationCategory Category => ValidationCategory.Business;

    public IReadOnlyList<string> Properties => [_target, _left, _right];

    public string Describe() => $"{_target} must equal {_left} {_op} {_right}";

    public void Emit(ValidationEmitContext ctx)
    {
        if (!ctx.HasProperty(_target) || !ctx.HasProperty(_left) || !ctx.HasProperty(_right))
        {
            ctx.Line($"// business rule skipped: '{Describe()}' references an unknown property");
            return;
        }

        var symbol = _op.ToLowerInvariant() switch
        {
            "times" or "multiplied by" => "*",
            "plus" => "+",
            "minus" => "-",
            _ => "/"
        };

        var message = SymbolDisplay.FormatLiteral(Describe() + ".", quote: true);
        const string Parse = "System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture";

        ctx.Line($"if (decimal.TryParse(((object)this.{_target})?.ToString(), {Parse}, out var __t)");
        ctx.Line($"    && decimal.TryParse(((object)this.{_left})?.ToString(), {Parse}, out var __l)");
        ctx.Line($"    && decimal.TryParse(((object)this.{_right})?.ToString(), {Parse}, out var __r))");
        ctx.Line("{");

        if (symbol == "/")
            ctx.Line($"    if (__r != 0m && System.Math.Abs(__t - (__l / __r)) > 0.0001m) errors.Add({message});");
        else
            ctx.Line($"    if (System.Math.Abs(__t - (__l {symbol} __r)) > 0.0001m) errors.Add({message});");

        ctx.Line("}");
    }
}

public sealed class ArithmeticRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<target>\w+)\s+must\s+equal\s+(?<left>\w+)\s+(?<op>times|multiplied\s+by|plus|minus|divided\s+by)\s+(?<right>\w+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 15;
    public ValidationCategory Category => ValidationCategory.Business;

    public IReadOnlyList<string> Examples =>
        ["Total must equal Quantity times Price", "Net must equal Gross minus Tax"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        rule = new ArithmeticRule(
            m.Groups["target"].Value,
            m.Groups["left"].Value,
            Regex.Replace(m.Groups["op"].Value.Trim(), @"\s+", " ").ToLowerInvariant(),
            m.Groups["right"].Value);
        return true;
    }
}
```

- [ ] **Step 5: Write `SameAsRule.cs`**

```csharp
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerationEngine.Core.Validation.Rules;

/// <summary>Business rule: two properties have to agree, or have to differ.</summary>
public sealed class SameAsRule : IValidationRule
{
    private readonly string _a;
    private readonly string _b;
    private readonly bool _same;

    public SameAsRule(string a, string b, bool same)
    {
        _a = a;
        _b = b;
        _same = same;
    }

    public ValidationCategory Category => ValidationCategory.Business;

    public IReadOnlyList<string> Properties => [_a, _b];

    public string Describe() =>
        _same ? $"{_a} must be the same as {_b}" : $"{_a} must be different from {_b}";

    public void Emit(ValidationEmitContext ctx)
    {
        if (!ctx.HasProperty(_a) || !ctx.HasProperty(_b))
        {
            ctx.Line($"// business rule skipped: '{Describe()}' references an unknown property");
            return;
        }

        var message = SymbolDisplay.FormatLiteral(Describe() + ".", quote: true);

        ctx.Line($"var __a = ((object)this.{_a})?.ToString();");
        ctx.Line($"var __b = ((object)this.{_b})?.ToString();");
        ctx.Line($"var __equal = string.Equals(__a, __b, System.StringComparison.Ordinal);");
        ctx.Line($"if ({(_same ? "!__equal" : "__equal")}) errors.Add({message});");
    }
}

public sealed class SameAsRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<a>\w+)\s+must\s+(?<neg>not\s+)?(?:be\s+the\s+same\s+as|equal|be\s+(?<diff>different\s+from))\s+(?<b>\w+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 16;
    public ValidationCategory Category => ValidationCategory.Business;

    public IReadOnlyList<string> Examples =>
    [
        "BillingPostcode must be the same as ShippingPostcode",
        "BillingPostcode must be different from ShippingPostcode"
    ];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var same = !m.Groups["neg"].Success && !m.Groups["diff"].Success;

        rule = new SameAsRule(m.Groups["a"].Value, m.Groups["b"].Value, same);
        return true;
    }
}
```

`ArithmeticRuleParser` is priority 15 and `SameAsRuleParser` is 16, so `Total must equal Quantity times Price` reaches the arithmetic parser before the sameness one, whose `<b>` is a single `\w+` and could not match `Quantity times Price` anyway.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~CrossPropertyRuleTests|FullyQualifiedName~CatalogExamplesTests"`

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add CodeGenerationEngine/Core/Validation/Rules/EitherOrRule.cs \
        CodeGenerationEngine/Core/Validation/Rules/ArithmeticRule.cs \
        CodeGenerationEngine/Core/Validation/Rules/SameAsRule.cs \
        CodeGenerator.Tests/Core/Validation/Rules/CrossPropertyRuleTests.cs
git commit -m "feat: either-or, arithmetic and sameness business rules"
```

---

### Task 10: The new phrasings work from `add validation` too

The page in the second plan is not the only caller. `CommandFront.Parse` composes the rule fragment from `add validation <rule> on <property> in <class>`, and that composition is where the rule text is actually built — the older unit tests hand-built an already composed `ValidationText`, which is how every data rule except `required` was once unreachable end to end while its own tests passed.

**Files:**
- Modify: `CodeGenerator.Tests/Core/Validation/AddValidationPhrasingTests.cs`

**Interfaces:**
- Consumes: every parser from Tasks 3–9; `CommandFront.Parse(string, string)`; `CommandHandlingService.Execute`.
- Produces: nothing.

- [ ] **Step 1: Write the failing tests**

Append to `AddValidationPhrasingTests.cs`, inside the existing class (it already has `Run`, `AssertRegistered` and a `[TestInitialize]` that clears the registry):

```csharp
    [TestMethod]
    public void Inclusive_bound_phrasing_registers_a_rule() =>
        AssertRegistered(
            "add validation must be at least 18 on Age in Employee",
            "Employee", "Age must be at least 18");

    [TestMethod]
    public void Character_bound_phrasing_registers_a_length_rule() =>
        AssertRegistered(
            "add validation must be at most 50 characters on Name in Employee",
            "Employee", "Name must be at most 50 characters");

    [TestMethod]
    public void Sign_phrasing_registers_a_rule() =>
        AssertRegistered(
            "add validation must be positive on Salary in Employee",
            "Employee", "Salary must be positive");

    [TestMethod]
    public void Named_format_phrasing_registers_a_rule() =>
        AssertRegistered(
            "add validation must be a valid URL on Website in Employee",
            "Employee", @"Website must match ^https?://[^\s]+$");

    [TestMethod]
    public void Relative_date_phrasing_registers_a_rule() =>
        AssertRegistered(
            "add validation must be in the past on DateOfBirth in Employee",
            "Employee", "DateOfBirth must be in the past");

    [TestMethod]
    public void Text_shape_phrasing_registers_a_rule() =>
        AssertRegistered(
            "add validation must be uppercase on Code in Employee",
            "Employee", "Code must be uppercase");
```

And a business-side check in `BusinessValidationCommandTests.cs`, matching whatever helper that file already uses to assert a registered business rule:

```csharp
    [TestMethod]
    public void Either_or_phrasing_registers_a_business_rule() =>
        AssertRegistered(
            "add business validation Either Email or Phone must be provided in Customer",
            "Customer", "Either Email or Phone must be provided");
```

- [ ] **Step 2: Run the tests to verify they fail or pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~AddValidationPhrasingTests|FullyQualifiedName~BusinessValidationCommandTests"`

Expected: these should PASS already — the parsers exist and the catalog discovers them by reflection. **If any fails, the defect is in `CommandFront.Parse`'s composition, not in the parser**: read the composed `ValidationText` first (add a temporary assertion on `command.ValidationText`) before touching a regex. The most likely cause is the property name being prepended to text that already names it, or not being prepended at all.

- [ ] **Step 3: Fix `CommandFront.Parse` only if a test failed**

If and only if a test failed, find where `CommandFront` composes `ValidationText` (search `NaturalLanguageBridge` for `ValidationText`) and extend the composition to cover the failing shape. Do not change the parser's regex to accommodate a badly composed fragment — that would make the parser accept text no one types.

- [ ] **Step 4: Run the whole test suite**

Run: `dotnet test SmartCodeGenerator.slnx --configuration Release`

Expected: PASS, everything. Release with the analyzers on is what CI would have run, and is where MSTEST0037 and the xUnit/MSTest analyzer rules surface as errors rather than warnings.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator.Tests/Core/Validation/
git commit -m "test: the new phrasings reach the catalog from add validation"
```

---

### Task 11: Document the phrasings in the command catalog

`help` reads `CommandDictionary`. The validation entries there give one example each and predate every rule added by this plan.

**Files:**
- Modify: `CodeGenerationEngine/Core/CommandDictionary.cs:295-333`
- Test: `CodeGenerator.Tests/Core/Validation/CatalogExamplesTests.cs` (add one test)

**Interfaces:**
- Consumes: `ValidationRuleCatalog.Default.Parsers[].Examples`.
- Produces: nothing.

- [ ] **Step 1: Write the failing test**

Add to `CatalogExamplesTests.cs`:

```csharp
    /// <summary>
    /// The help entry's example has to be a phrasing the catalog still parses. An example that
    /// stopped working is worse than no example: it is the tool telling you to type something it
    /// will refuse.
    /// </summary>
    [TestMethod]
    public void The_help_examples_for_validation_still_parse()
    {
        var entries = CommandDictionary.All
            .Where(e => e.Category == "Validation" && e.Command.StartsWith("add ",
                System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.IsGreaterThan(0, entries.Count);

        foreach (var entry in entries)
        {
            var command = CommandFront.Parse(entry.Example, "");

            Assert.IsFalse(string.IsNullOrWhiteSpace(command.ValidationText),
                $"No rule text came out of the help example: {entry.Example}");

            var ctx = new ValidationParseContext
            {
                Fragment = command.ValidationText,
                ClassName = command.ClassName
            };

            var data = ValidationRuleCatalog.Default.Parse(
                command.ValidationText, ValidationCategory.Data, ctx);
            var business = ValidationRuleCatalog.Default.Parse(
                command.ValidationText, ValidationCategory.Business, ctx);

            Assert.IsTrue(data.Count + business.Count > 0,
                $"The catalog no longer parses the help example: {entry.Example}");
        }
    }
```

Add `using CodeGenerationEngine.Core;` and `using NaturalLanguageBridge;` to the file. Confirm the real names of `CommandDictionary`'s collection and entry members while writing this — read `CommandDictionary.cs` around line 295 for the property names (`Category`, `Command`, `Description`, `Example`) and the accessor that returns them all.

- [ ] **Step 2: Run the test**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~CatalogExamplesTests"`

Expected: PASS if the existing examples still parse — this test is a guard, and it is allowed to pass on first run. If it fails, an existing help example is already broken; fix the example.

- [ ] **Step 3: Widen the description of the two add-commands**

In `CommandDictionary.cs`, extend the `Description` of `add validation <rule> on <property> in <class name>` and `add business validation <rule> in <class name>` to name the families rather than a single example. Keep to the console's width — the help renderer folds past roughly 150 characters:

```csharp
            Description = "Adds a data validation rule to a class in plain English. Understands "
                        + "required, numeric bounds (between, at least, at most, greater/less "
                        + "than, positive, whole number), text bounds (length, starts/ends with, "
                        + "contains, uppercase, digits only), named formats (email, URL, phone, "
                        + "postcode, GUID, IP) and relative dates (in the past, in the future, "
                        + "at least N years ago).",
```

```csharp
            Description = "Adds a business rule spanning more than one property: comparisons "
                        + "(after, before, not exceed), required-when, either/or, arithmetic "
                        + "(equals A times B) and sameness (same as, different from).",
```

- [ ] **Step 4: Run the help tests**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~Help"`

Expected: PASS. There is a no-wrap-at-80 test on the help overview; if a new description trips a width assertion, shorten it rather than changing the assertion.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/CommandDictionary.cs \
        CodeGenerator.Tests/Core/Validation/CatalogExamplesTests.cs
git commit -m "docs: the validation help entries name what the catalog now understands"
```

---

### Task 12: Full suite, and the catalog's own count

**Files:**
- Test: `CodeGenerator.Tests/Core/Validation/ValidationRuleCatalogTests.cs` (modify if it asserts a parser count)

- [ ] **Step 1: Run the full suite in Release**

Run: `dotnet test SmartCodeGenerator.slnx --configuration Release`

Expected: PASS across all test projects. Note the total green count in the commit message — this repository tracks it.

- [ ] **Step 2: Fix any parser-count assertion**

If `ValidationRuleCatalogTests` asserts a fixed number of discovered parsers, update it to the new number. Do not delete the assertion — it is what notices a parser that failed to be discovered because it lacks a parameterless constructor.

- [ ] **Step 3: Verify the catalog discovered every new parser**

Run a one-off check that every new parser type is present, as a test rather than by hand — add to `ValidationRuleCatalogTests`:

```csharp
    [TestMethod]
    public void Every_parser_in_the_assembly_is_discovered()
    {
        var declared = typeof(IValidationRuleParser).Assembly.GetTypes()
            .Where(t => typeof(IValidationRuleParser).IsAssignableFrom(t)
                        && !t.IsInterface && !t.IsAbstract)
            .ToList();

        var discovered = ValidationRuleCatalog.Default.Parsers.Select(p => p.GetType()).ToHashSet();

        foreach (var type in declared)
            Assert.IsTrue(discovered.Contains(type),
                $"{type.Name} was not discovered — check it has a public parameterless constructor.");
    }
```

- [ ] **Step 4: Commit**

```bash
git add CodeGenerator.Tests/Core/Validation/ValidationRuleCatalogTests.cs
git commit -m "test: every declared parser is discovered by the catalog"
```

---

## Self-Review

**Spec coverage.** Tier 1 numbers → Tasks 3 and 4. Tier 1 text → Tasks 5 and 6. Tier 2 formats → Task 7. Tier 2 dates → Task 8. Tier 3 cross-property → Task 9. The two collision fixes → Task 1. `IValidationRuleParser.Examples` → Task 2. The spec's `IValidationRule.Properties` is not in the spec's own text — it was added here because the second plan's writer cannot refuse a rule naming an absent property without it; flagged in Task 2's rationale. The spec's "reads the declared type" for dates is implemented as a runtime check instead, flagged in Task 8.

**Placeholder scan.** No TBDs. Two places instruct reading existing code before writing: Task 7's `RegexRule` constructor accessibility and Task 11's `CommandDictionary` member names. Both are "confirm this name in the file" rather than "figure out what to do", and both name the file and line range.

**Type consistency.** `InclusiveBoundRule(string, decimal, bool)`, `NumberShapeRule(string, NumberShape)`, `LengthBoundRule(string, int, LengthBound)`, `TextShapeRule(string, TextShape, string)`, `ValidDateRule(string)`, `RelativeDateRule(string, DateBound, int)`, `EitherOrRule(string, string)`, `ArithmeticRule(string, string, string, string)`, `SameAsRule(string, string, bool)` — each is constructed only by its own parser, in the same file. `Examples` and `Properties` are both `IReadOnlyList<string>` everywhere.

**Priorities, in one place, for the reviewer:** Data — LengthBound 18, LengthRange 20, NumericRange 30, MinValue/MaxValue 35, Sign 36, NumberForm 37, GreaterThan 40, LessThan 50, Email 60, Regex 61, Affix/Contains 62, Case 63, CharacterClass 64, NamedFormat 65, ValidDate 66, RelativeDate 70, DateDistance 71, AllowedValues 100. Business — RequiredWhen 10, EitherOr 12, Arithmetic 15, SameAs 16, Comparison 20.
