using NaturalLanguageEngine.Abstractions.Morphology;

namespace NaturalLanguageEngine.Core.Stages;

/// <summary>
/// Populates <see cref="AnalysisContext.NormalizedText"/> from the
/// original input. Runs before tokenization.
/// </summary>
public sealed class NormalizationStage : PipelineStageBase
{
    private readonly INormalizer _normalizer;

    public NormalizationStage(INormalizer normalizer)
    {
        _normalizer = normalizer;
    }

    public override string Name => "Normalization";

    public override int Order => 5;

    public override AnalysisContext Execute(AnalysisContext context)
    {
        context.NormalizedText = _normalizer.Normalize(context.OriginalText);
        return context;
    }
}
