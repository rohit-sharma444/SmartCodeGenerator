using CodeGenerationEngine.Models;
using System.Text.Json;

namespace CodeGenerationEngine.Core;

public sealed class VocabularyStore
{
    private readonly Dictionary<string, LearnedToken> _tokens =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _lock = new();

    public IReadOnlyDictionary<string, LearnedToken> Tokens => _tokens;

    public bool TryGet(
        string word,
        out LearnedToken token)
    {
        lock (_lock)
        {
            return _tokens.TryGetValue(
                word,
                out token!);
        }
    }

    public void Learn(
        string word,
        string normalizedWord,
        TokenCategory category,
        double confidence = 0.5)
    {
        if (string.IsNullOrWhiteSpace(word))
            return;

        lock (_lock)
        {
            if (_tokens.TryGetValue(word, out var existing))
            {
                existing.Frequency++;

                if (confidence > existing.Confidence)
                    existing.Confidence = confidence;

                if (!string.IsNullOrWhiteSpace(normalizedWord))
                    existing.NormalizedWord = normalizedWord;

                if (category != TokenCategory.Unknown)
                    existing.Category = category;

                return;
            }

            _tokens[word] = new LearnedToken
            {
                Word = word,
                NormalizedWord = normalizedWord,
                Category = category,
                Frequency = 1,
                Confidence = confidence
            };
        }
    }

    public void Save(string file)
    {
        lock (_lock)
        {
            var json =
                JsonSerializer.Serialize(
                    _tokens.Values.OrderByDescending(x => x.Frequency),
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

            File.WriteAllText(file, json);
        }
    }

    public void Load(string file)
    {
        if (!File.Exists(file))
            return;

        var json = File.ReadAllText(file);

        var items =
            JsonSerializer.Deserialize<List<LearnedToken>>(json);

        if (items == null)
            return;

        lock (_lock)
        {
            _tokens.Clear();

            foreach (var item in items)
            {
                _tokens[item.Word] = item;
            }
        }
    }
}