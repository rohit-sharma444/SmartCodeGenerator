using NaturalLanguageEngine.Models.Common;
using NaturalLanguageEngine.Models.Lexical;
using NaturalLanguageEngine.Models.Morphology;
using NaturalLanguageEngine.Models.Semantics;
using NaturalLanguageEngine.Models.Syntax;

namespace NaturalLanguageEngine.Models.Analysis;

public sealed class TokenAnalysis
{
    public Token Token { get; init; } = default!;

    public Lemma? Lemma { get; set; }

    public PartOfSpeech PartOfSpeech { get; set; }

    public Entity? Entity { get; set; }

    public ConfidenceScore Confidence { get; set; }

    public IDictionary<string, object> Features { get; }
        = new Dictionary<string, object>();
}