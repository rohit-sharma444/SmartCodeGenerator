using NaturalLanguageEngine.Syntax;

namespace NaturalLanguageEngine.Core.Stages;

/// <summary>
/// Tags each token in every sentence with a part of speech.
/// </summary>
public sealed class PartOfSpeechStage : PipelineStageBase
{
    private readonly PosTagger _tagger;

    public PartOfSpeechStage(PosTagger tagger)
    {
        _tagger = tagger;
    }

    public override string Name => "PartOfSpeech";

    public override int Order => 30;

    public override AnalysisContext Execute(AnalysisContext context)
    {
        foreach (var sentence in context.Document.Sentences)
            _tagger.Tag(sentence.Tokens);

        return context;
    }
}
