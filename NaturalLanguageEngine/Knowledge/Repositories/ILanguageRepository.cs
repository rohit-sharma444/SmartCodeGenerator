using NaturalLanguageEngine.Knowledge.Models;

namespace NaturalLanguageEngine.Knowledge.Repositories;

public interface ILanguageRepository
{
    bool IsStopWord(string word);

    bool TryGetWord(
        string word,
        out WordEntry entry);

    bool TryGetSynonym(
        string word,
        out string canonicalWord);

    bool TryExpandContraction(
        string word,
        out string expanded);

    bool IsAbbreviation(string word);

    IReadOnlyCollection<string> GetIrregularForms(
        string lemma);

    /// <summary>True if the word is a recognized place name (city, country, region, landmark).</summary>
    bool IsKnownLocation(string word);

    /// <summary>True if the word is a recognized organization name, or a token that marks a name as an organization (e.g. "Inc", "Corp").</summary>
    bool IsKnownOrganization(string word);
}