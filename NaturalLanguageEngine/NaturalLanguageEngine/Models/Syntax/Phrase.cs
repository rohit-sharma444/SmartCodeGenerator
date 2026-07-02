using NaturalLanguageEngine.Models.Analysis;

namespace NaturalLanguageEngine.Models.Syntax;

public sealed class Phrase
{
    public PhraseType Type { get; set; }

    /// <summary>
    /// The head word of the phrase.
    /// Example:
    /// "red sports car"
    /// head = car
    /// </summary>
    public TokenAnalysis? Head { get; set; }

    public IList<TokenAnalysis> Tokens { get; }
        = new List<TokenAnalysis>();

    public IList<Phrase> Children { get; }
        = new List<Phrase>();

    public Phrase? Parent { get; set; }

    public override string ToString()
        => string.Join(" ", Tokens.Select(t => t.Token.Text));
}