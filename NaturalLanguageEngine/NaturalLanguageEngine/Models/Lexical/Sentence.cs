using NaturalLanguageEngine.Models.Analysis;
using NaturalLanguageEngine.Models.Syntax;

namespace NaturalLanguageEngine.Models.Lexical;

public sealed class Sentence
{
    public int Index { get; set; }

    public string OriginalText { get; set; } = string.Empty;

    public IList<TokenAnalysis> Tokens { get; }
        = new List<TokenAnalysis>();

    public IList<Phrase> Phrases { get; }
        = new List<Phrase>();
    
    public ParseTree? ParseTree { get; set; }
}