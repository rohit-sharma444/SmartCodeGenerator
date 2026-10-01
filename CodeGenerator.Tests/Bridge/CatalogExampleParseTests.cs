using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

/// <summary>
/// Every example help, docs/commands.md and the manual show must be a line the tool
/// understands. "create employee class" was the catalog's own example for creating a class
/// for months, and typing it said "Command not recognized".
/// </summary>
[TestClass]
public class CatalogExampleParseTests
{
    // Handled by the CLI (or the build-app review loop) before the parser sees the line, so
    // CommandFront rightly reads these as Unknown.
    private static readonly HashSet<string> HandledBeforeParsing = ["Utility", "AI", "Build App"];

    [TestMethod]
    public void Every_parsed_example_is_recognised()
    {
        var unrecognised = CommandDictionary.Commands
            .Where(c => !HandledBeforeParsing.Contains(c.Category) && c.Command != "show relationships")
            .Where(c => CommandFront.Parse(c.Example, "").Intent == IntentType.Unknown)
            .Select(c => $"{c.Command}: e.g. {c.Example}")
            .ToList();

        Assert.IsEmpty(unrecognised, string.Join("\n", unrecognised));
    }
}
