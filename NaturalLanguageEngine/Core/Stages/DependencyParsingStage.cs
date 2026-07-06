using NaturalLanguageEngine.Abstractions.Syntax;

namespace NaturalLanguageEngine.Core.Stages;

/// <summary>
/// Runs the dependency parser over each sentence's phrases and appends the
/// resulting edges to <see cref="AnalysisContext.Dependencies"/>. Runs
/// after phrase detection (which the parser consumes) and before the
/// semantic stage (which consumes the resulting dependency edges to derive
/// relationships).
/// </summary>
public sealed class DependencyParsingStage : PipelineStageBase
{
    private readonly IDependencyParser _parser;

    public DependencyParsingStage(IDependencyParser parser)
    {
        _parser = parser;
    }

    public override string Name => "DependencyParsing";

    public override int Order => 45;

    public override AnalysisContext Execute(AnalysisContext context)
    {
        foreach (var sentence in context.Document.Sentences)
        {
            var dependencies = _parser.Parse(sentence.Phrases.ToList());
            foreach (var dependency in dependencies)
                context.Dependencies.Add(dependency);
        }

        return context;
    }
}
