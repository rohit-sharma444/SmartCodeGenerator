namespace NaturalLanguageEngine.Models.Morphology;

public sealed class Lemma
{
    public string Root { get; set; } = string.Empty;

    public string Original { get; set; } = string.Empty;

    public override string ToString()
        => Root;
}