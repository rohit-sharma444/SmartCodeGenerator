using NaturalLanguageEngine.Abstractions.Morphology;
using NaturalLanguageEngine.Knowledge.Repositories;

namespace NaturalLanguageEngine.Core.Stages;

/// <summary>
/// Assigns a lemma to each token and flags stop-words in token features.
/// </summary>
public sealed class MorphologyStage : PipelineStageBase
{
    private readonly ILemmatizer _lemmatizer;
    private readonly ILanguageRepository _language;

    public MorphologyStage(ILemmatizer lemmatizer, ILanguageRepository language)
    {
        _lemmatizer = lemmatizer;
        _language = language;
    }

    public override string Name => "Morphology";

    public override int Order => 20;

    public override AnalysisContext Execute(AnalysisContext context)
    {
        foreach (var sentence in context.Document.Sentences)
        {
            foreach (var analysis in sentence.Tokens)
            {
                analysis.Lemma = _lemmatizer.GetLemma(analysis.Token);

                if (_language.IsStopWord(analysis.Token.Text))
                    analysis.Features["stopword"] = true;
            }
        }

        return context;
    }
}
