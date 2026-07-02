namespace NaturalLanguageEngine.Models.Lexical;

public sealed class Document
{
    public string OriginalText { get; set; } = string.Empty;

    public IList<Sentence> Sentences { get; }
        = new List<Sentence>();
}