using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using NaturalLanguageBridge;

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

    /// <summary>
    /// The help entry's example has to be a phrasing the catalog still parses. An example that
    /// stopped working is worse than no example: it is the tool telling you to type something it
    /// will refuse.
    /// </summary>
    [TestMethod]
    public void The_help_examples_for_validation_still_parse()
    {
        var entries = CommandDictionary.Commands
            .Where(e => e.Category == "Validation"
                        && e.Command.StartsWith("add ", StringComparison.OrdinalIgnoreCase))
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

            Assert.IsGreaterThan(0, data.Count + business.Count,
                $"The catalog no longer parses the help example: {entry.Example}");
        }
    }
}
