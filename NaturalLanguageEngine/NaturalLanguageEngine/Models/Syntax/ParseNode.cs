using NaturalLanguageEngine.Models.Analysis;

namespace NaturalLanguageEngine.Models.Syntax;

public sealed class ParseNode
{
    public ParseNodeType Type { get; set; }

    /// <summary>
    /// Head token of this syntactic unit.
    /// </summary>
    public TokenAnalysis? Head { get; set; }

    /// <summary>
    /// Leaf node only.
    /// </summary>
    public TokenAnalysis? Token { get; set; }

    public ParseNode? Parent { get; private set; }

    public IList<ParseNode> Children { get; }
        = new List<ParseNode>();

    public void AddChild(ParseNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    public IEnumerable<TokenAnalysis> DescendantTokens()
    {
        if (Token != null)
            yield return Token;

        foreach (var child in Children)
        {
            foreach (var t in child.DescendantTokens())
                yield return t;
        }
    }
}