using NaturalLanguageEngine.Knowledge.Models;

namespace NaturalLanguageEngine.Knowledge.Providers;

public interface ILanguageProvider
{
    IEnumerable<WordEntry> LoadWords();

    IEnumerable<SynonymSet> LoadSynonyms();

    IDictionary<string, string> LoadContractions();

    ISet<string> LoadStopWords();

    ISet<string> LoadAbbreviations();

    IDictionary<string, string[]> LoadIrregularWords();

    /// <summary>Known place names (cities, countries, regions, landmarks).</summary>
    ISet<string> LoadKnownLocations();

    /// <summary>Known organization names and name-final tokens that mark an organization (e.g. "Inc", "Corp").</summary>
    ISet<string> LoadKnownOrganizations();
}