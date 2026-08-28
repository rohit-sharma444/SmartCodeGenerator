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
        public IReadOnlyList<string> Properties => [Text];
        public string Describe() => Text;
        public void Emit(ValidationEmitContext ctx) => ctx.Line($"// {Text}");
    }

    private sealed class HighPriorityParser : IValidationRuleParser
    {
        public int Priority => 10;
        public ValidationCategory Category => ValidationCategory.Data;
        public IReadOnlyList<string> Examples => ["anything"];
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
        public IReadOnlyList<string> Examples => ["anything"];
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

        Assert.HasCount(expected, ValidationRuleCatalog.Default.Parsers);
    }

    /// <summary>
    /// The stronger half of the discovery guarantee: every parser type declared in the engine is
    /// in the catalog, whatever its constructor looks like.
    /// </summary>
    /// <remarks>
    /// <see cref="Default_discovers_every_parser_in_the_engine_assembly"/> counts only the types
    /// reflection can construct, so a parser that lost its public parameterless constructor
    /// disappears from both sides of that assertion and it stays green. This test does not filter,
    /// which is what makes it able to name the parser that went missing.
    /// </remarks>
    [TestMethod]
    public void Every_parser_declared_in_the_engine_is_discovered()
    {
        var declared = typeof(IValidationRuleParser).Assembly.GetTypes()
            .Where(t => typeof(IValidationRuleParser).IsAssignableFrom(t)
                        && !t.IsInterface && !t.IsAbstract)
            .ToList();

        Assert.IsGreaterThan(0, declared.Count);

        var discovered = ValidationRuleCatalog.Default.Parsers
            .Select(p => p.GetType())
            .ToHashSet();

        foreach (var type in declared)
        {
            Assert.Contains(type, discovered,
                $"{type.Name} was not discovered - check it has a public parameterless constructor.");
        }
    }
}
