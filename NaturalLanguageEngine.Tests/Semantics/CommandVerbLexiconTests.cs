using NaturalLanguageEngine.Abstractions.Semantics;
using NaturalLanguageEngine.Core;
using NaturalLanguageEngine.Models.Semantics;
using Xunit;

namespace NaturalLanguageEngine.Tests.Semantics;

/// <summary>
/// Locks in that CodeGenerator's imperative command verbs are present in the
/// English lexicon and are therefore tagged as Action verbs by the engine.
/// </summary>
public class CommandVerbLexiconTests
{
    private readonly ISemanticAnalyzer _engine = EngineBuilder.BuildDefault().Analyzer;

    [Theory]
    [InlineData("add method Save to Order", "AddIntent", "add")]
    [InlineData("list projects", "ListIntent", "list")]
    [InlineData("generate a service for Payment", "GenerateIntent", "generate")]
    [InlineData("delete class Foo", "DeleteIntent", "delete")]
    [InlineData("rename class A to B", "RenameIntent", "rename")]
    [InlineData("analyze class Foo", "AnalyzeIntent", "analyze")]
    public void Analyze_CommandVerb_TaggedAsActionWithIntent(
        string input, string expectedIntent, string expectedVerb)
    {
        var result = _engine.Analyze(input);

        Assert.Equal(expectedIntent, result.Intent?.Name);
        Assert.Contains(result.Entities,
            e => e.Type == EntityType.Action && e.Text == expectedVerb);
    }
}
