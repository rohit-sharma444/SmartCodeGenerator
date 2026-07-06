using NaturalLanguageEngine.Models.Analysis;

namespace NaturalLanguageEngine.Models.Syntax;

public sealed class Dependency
{
    public TokenAnalysis Governor { get; set; } = default!;

    public TokenAnalysis Dependent { get; set; } = default!;

    public DependencyType Type { get; set; }
}