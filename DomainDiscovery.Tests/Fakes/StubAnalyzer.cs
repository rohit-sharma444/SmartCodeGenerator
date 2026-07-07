using NaturalLanguageEngine.Abstractions.Semantics;
using NaturalLanguageEngine.Models.Semantics;

namespace DomainDiscovery.Tests.Fakes;

/// <summary>Returns a canned <see cref="SemanticResult"/> regardless of input.</summary>
public sealed class StubAnalyzer : ISemanticAnalyzer
{
    private readonly SemanticResult _result;
    public string? LastText { get; private set; }

    public StubAnalyzer(SemanticResult result) => _result = result;

    public SemanticResult Analyze(string text)
    {
        LastText = text;
        return _result;
    }
}
