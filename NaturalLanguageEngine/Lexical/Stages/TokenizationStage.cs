
using NaturalLanguageEngine.Abstractions.Lexical;
using NaturalLanguageEngine.Core;
using NaturalLanguageEngine.Models.Lexical;

namespace NaturalLanguageEngine.Lexical.Stages;

/// <summary>
/// Splits the input into sentences and tokenizes each one, populating
/// <see cref="AnalysisContext.Document"/> with one <see cref="Sentence"/>
/// per sentence boundary (rather than treating the whole input as a
/// single sentence).
/// </summary>
public sealed class TokenizationStage : PipelineStageBase
{
    private readonly ISentenceSplitter _sentenceSplitter;

    public TokenizationStage(ISentenceSplitter sentenceSplitter)
    {
        _sentenceSplitter = sentenceSplitter;
    }

    public override string Name => "Tokenization";

    public override int Order => 10;

    public override AnalysisContext Execute(AnalysisContext context)
    {
        var sentences = _sentenceSplitter.Split(context.OriginalText);

        var document = new Document
        {
            OriginalText = context.OriginalText
        };

        var allTokens = new List<Token>();
        int index = 0;

        foreach (var sentence in sentences)
        {
            sentence.Index = index++;

            foreach (var analysis in sentence.Tokens)
            {
                analysis.Confidence = 1.0;
                allTokens.Add(analysis.Token);
            }

            document.Sentences.Add(sentence);
        }

        context.Tokens = allTokens;
        context.Document = document;

        return context;
    }
}
