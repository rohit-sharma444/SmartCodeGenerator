using NaturalLanguageEngine.Models.Syntax;

namespace NaturalLanguageEngine.Knowledge.Models;

//public sealed record WordEntry
//{
//    public required string Word { get; init; }
//    public required string Lemma { get; init; }
//    public required PartOfSpeech PartOfSpeech { get; init; }
//    public LexicalFeatures Features { get; init; } = LexicalFeatures.None;
//    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
//    public double Frequency { get; init; }
//    public bool IsStopWord { get; init; }
//}

[Flags]
public enum LexicalFeatures
{
    None = 0,
    Singular = 1 << 0,
    Plural = 1 << 1,
    ProperNoun = 1 << 2,
    AuxiliaryVerb = 1 << 3,
    ModalVerb = 1 << 4,
    Comparative = 1 << 5,
    Superlative = 1 << 6,
    PastTense = 1 << 7,
    PresentParticiple = 1 << 8,
    PastParticiple = 1 << 9,
    Contraction = 1 << 10,
    Abbreviation = 1 << 11
}

public enum PartOfSpeech
{
    Unknown = 0,
    Noun,
    ProperNoun,
    Pronoun,
    Verb,
    Auxiliary,
    Modal,
    Adjective,
    Adverb,
    Determiner,
    Preposition,
    Conjunction,
    Interjection,
    Numeral,
    Particle,
    Symbol,
    Punctuation
}


public sealed class WordEntry
{
    public string Word { get; init; } = string.Empty;
    public string Lemma { get; init; } = string.Empty;
    public string PartOfSpeech { get; init; } = string.Empty;
    public IReadOnlyCollection<string> Tags { get; init; }
        = Array.Empty<string>();
}