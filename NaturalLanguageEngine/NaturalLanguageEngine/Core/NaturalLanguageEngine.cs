using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageEngine.Core;

public sealed class NaturalLanguageEngine
{
    private readonly Pipeline _pipeline;

    public NaturalLanguageEngine(Pipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public SemanticResult Analyze(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Input text cannot be null or empty.", nameof(text));

        var context = _pipeline.Execute(text);

        var result = new SemanticResult
        {
            Intent = context.Intent,
            Confidence = context.Confidence
        };

        foreach (var entity in context.Entities)
            result.Entities.Add(entity);

        foreach (var relationship in context.Relationships)
            result.Relationships.Add(relationship);

        foreach (var diagnostic in context.Diagnostics)
            result.Diagnostics.Add(diagnostic);

        return result;
    }
}