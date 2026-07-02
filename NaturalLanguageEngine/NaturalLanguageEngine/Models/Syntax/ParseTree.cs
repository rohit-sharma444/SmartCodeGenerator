namespace NaturalLanguageEngine.Models.Syntax;

public sealed class ParseTree
{
    public ParseNode Root { get; }

    public ParseTree(ParseNode root)
    {
        Root = root;
    }
}