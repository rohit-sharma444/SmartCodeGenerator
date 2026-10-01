using System.Text.Json;
using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Phrasings the CLI did not understand, each paired with the intent of the command the user
/// typed next that it did - the raw material a person reviews into learned-phrases.tsv.
/// </summary>
/// <remarks>
/// Kept beside learned.json in the workspace's .codegen folder, one JSON object per line, so a
/// crash mid-write costs at most the line being written. Nothing here changes what the CLI
/// understands: a candidate is only evidence until it has been reviewed, committed, and passed
/// the golden-set gate.
/// </remarks>
public static class PhraseCandidates
{
    public const string FileName = "phrase-candidates.jsonl";

    /// <summary>One retry: <see cref="Phrase"/> is <see cref="Miss"/> without the words the
    /// working command took as its arguments, so it reads as a phrasing of the command rather
    /// than one use of it.</summary>
    public sealed record Candidate(string Phrase, IntentType Intent, string Miss, string FixedBy, DateTime Utc);

    private static readonly object Gate = new();
    private static List<Candidate> _all = [];
    private static string? _filePath;

    public static IReadOnlyList<Candidate> All
    {
        get { lock (Gate) return [.. _all]; }
    }

    /// <summary>Called with the learned store's own folder, so both follow the workspace.</summary>
    internal static void Initialize(string? folder)
    {
        lock (Gate)
        {
            _filePath = folder is null ? null : Path.Combine(folder, FileName);
            _all = _filePath is null ? [] : Load(_filePath);
        }
    }

    internal static void Record(Candidate candidate)
    {
        lock (Gate)
        {
            _all.Add(candidate);

            if (_filePath is null)
                return;

            try
            {
                File.AppendAllText(_filePath, JsonSerializer.Serialize(candidate) + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Evidence, not state: losing one line must never fail the command that produced it.
            }
        }
    }

    /// <summary>Forgets every candidate and deletes the log and the review file: they hold lines the
    /// user typed, so "reset learning" must not leave them behind.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            _all = [];

            if (_filePath is null)
                return;

            try
            {
                File.Delete(_filePath);
                File.Delete(Path.Combine(Path.GetDirectoryName(_filePath)!, ReviewFileName));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The in-memory list is already empty; a file left behind is retried next reset.
            }
        }
    }

    /// <summary>One line to review: a phrasing, what it was taken to mean, how many retries said
    /// so, and one of them in full so the reviewer can see what was actually typed.</summary>
    public sealed record ReviewLine(string Phrase, IntentType Intent, int Count, string Example);

    public const string ReviewFileName = "phrase-candidates.tsv";

    /// <summary>The candidates grouped by phrasing and meaning, most-seen first.</summary>
    public static IReadOnlyList<ReviewLine> Review()
        => All
            .GroupBy(c => (Phrase: c.Phrase.ToLowerInvariant(), c.Intent))
            .Select(g => new ReviewLine(g.First().Phrase, g.Key.Intent, g.Count(),
                $"{OneLine(g.Last().Miss)}  ->  {OneLine(g.Last().FixedBy)}"))
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Phrase, StringComparer.Ordinal)
            .ToList();

    /// <summary>The review in learned-phrases.tsv's own format - <c>phrase&lt;TAB&gt;Intent</c> -
    /// with the count and an example as a comment above each line, so an approved line is copied
    /// across as it stands.</summary>
    public static string ReviewTsv()
    {
        var lines = new List<string>
        {
            "# Candidate phrasings, most-seen first. Copy the ones that are right into",
            "# CodeIntelligence\\Corpus\\learned-phrases.tsv; GoldenSetTests then check they do no harm.",
        };

        foreach (var line in Review())
        {
            lines.Add($"# seen {line.Count}x, e.g. {line.Example}");
            lines.Add($"{line.Phrase}\t{line.Intent}");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    /// <summary>Writes <see cref="ReviewTsv"/> beside the candidate log; the path, or null with no
    /// workspace or nothing to review.</summary>
    public static string? WriteReviewFile()
    {
        string? folder;
        lock (Gate)
            folder = _filePath is null ? null : Path.GetDirectoryName(_filePath);

        if (folder is null || All.Count == 0)
            return null;

        try
        {
            var path = Path.Combine(folder, ReviewFileName);
            File.WriteAllText(path, ReviewTsv());
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// <paramref name="miss"/> without the words <paramref name="fix"/> took as arguments, or
    /// null when too little is left to be a phrasing of anything.
    /// </summary>
    public static string? Generalize(string miss, ParsedCommand fix)
    {
        var arguments = ArgumentWords(fix);

        var kept = Words(miss)
            .Where(w => !arguments.Contains(w))
            .ToList();

        // One word left ("class", "new") is a keyword, not a way of saying a command.
        return kept.Count >= 2 ? string.Join(' ', kept) : null;
    }

    /// <summary>Whether <paramref name="fix"/> reads as another go at <paramref name="miss"/>: the
    /// two share at least one word that is neither a little word nor one of
    /// <paramref name="command"/>'s arguments. Looser than LearningCoordinator's 60%-overlap
    /// rephrase test on purpose - "make me a new class Invoice" and "create class Invoice" share
    /// only "class", and are exactly the pair this exists to catch. A person reviews every one.</summary>
    public static bool IsRetry(string miss, string fix, ParsedCommand? command = null)
    {
        var arguments = command is null ? [] : ArgumentWords(command);
        var missWords = Words(miss).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return !string.Equals(miss.Trim(), fix.Trim(), StringComparison.OrdinalIgnoreCase)
               && Words(fix).Any(w => missWords.Contains(w) && !LittleWords.Contains(w) && !arguments.Contains(w));
    }

    // Shared by almost any two sentences, so sharing one says nothing.
    private static readonly HashSet<string> LittleWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "to", "in", "on", "of", "for", "with", "from", "at", "by", "and", "or",
        "is", "it", "me", "my", "i", "please", "this", "that", "all"
    };

    // A line typed over several lines, or with tabs, kept to one line of the review file.
    private static string OneLine(string text)
        => string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static IEnumerable<string> Words(string text)
        => (text ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('.', ',', ';', ':', '!', '?', '"', '\''))
            .Where(w => w.Length > 0);

    private static HashSet<string> ArgumentWords(ParsedCommand fix)
    {
        string?[] values =
        [
            fix.ClassName, fix.NewClassName, fix.ProjectName, fix.PropertyName, fix.NewPropertyName,
            fix.MethodName, fix.BaseClass, fix.InterfaceName, fix.ServiceName, fix.RepositoryName,
            fix.ControllerName, fix.ApiName, fix.OutputFolder, fix.ValidationText,
            .. fix.Properties.Select(p => p.Name),
            .. fix.Methods.Select(m => m.Name),
        ];

        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .SelectMany(v => Words(v!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static List<Candidate> Load(string path)
    {
        var loaded = new List<Candidate>();

        try
        {
            if (!File.Exists(path))
                return loaded;

            foreach (var line in File.ReadAllLines(path))
            {
                try
                {
                    if (line.Trim().Length > 0 && JsonSerializer.Deserialize<Candidate>(line) is { } candidate)
                        loaded.Add(candidate);
                }
                catch (JsonException)
                {
                    // A line cut short by a crash; the rest are still good.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return loaded;
    }
}
