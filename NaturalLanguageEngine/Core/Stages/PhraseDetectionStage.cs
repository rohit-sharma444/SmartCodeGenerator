using NaturalLanguageEngine.Abstractions.Syntax;

namespace NaturalLanguageEngine.Core.Stages;

/// <summary>
/// Groups tagged tokens into noun/verb phrases within each sentence.
/// </summary>
public sealed class PhraseDetectionStage : PipelineStageBase
{
    private readonly IPhraseDetector _detector;

    public PhraseDetectionStage(IPhraseDetector detector)
    {
        _detector = detector;
    }

    public override string Name => "PhraseDetection";

    public override int Order => 40;

    public override AnalysisContext Execute(AnalysisContext context)
    {
        foreach (var sentence in context.Document.Sentences)
            _detector.Detect(sentence);

        return context;
    }
}
