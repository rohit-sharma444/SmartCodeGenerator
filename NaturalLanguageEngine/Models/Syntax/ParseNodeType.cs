namespace NaturalLanguageEngine.Models.Syntax;

public enum ParseNodeType
{
    Unknown = 0,

    Sentence,

    Clause,

    NounPhrase,

    VerbPhrase,

    AdjectivePhrase,

    AdverbPhrase,

    PrepositionalPhrase,

    Token
}