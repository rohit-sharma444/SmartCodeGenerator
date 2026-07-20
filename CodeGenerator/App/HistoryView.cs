namespace CodeGenerator.App;

/// <summary>Formats the command history as a numbered, oldest-first list.</summary>
internal static class HistoryView
{
    internal static string Format(IReadOnlyList<string> entries, int max)
    {
        if (entries is null || entries.Count == 0)
            return "No commands in history yet.";

        var start = Math.Max(0, entries.Count - max);
        var lines = new List<string>();

        for (var i = start; i < entries.Count; i++)
            lines.Add($"{i - start + 1,4}  {entries[i]}");

        return string.Join(Environment.NewLine, lines);
    }
}
