namespace DataIngestion.Schema.Readers;

/// <summary>
/// Reads a <c>.txt</c> export by working out its delimiter first. A TXT file has no declared
/// format, so the alternative to sniffing is asking the user which character it is — a question
/// they should not have to answer about their own file.
/// </summary>
public sealed class DelimitedTextReader : IDataSourceReader
{
    /// <summary>
    /// How many lines the sniff examines. Enough to see a consistent field count, few enough
    /// that a large export is not read twice for the sake of a guess.
    /// </summary>
    public const int SniffLines = 20;

    /// <summary>
    /// Candidates in priority order. Tab first because it is almost never punctuation inside a
    /// field, so where both a tab and a comma are consistent the tab is the real one. Comma is
    /// last for the same reason inverted: it is the most likely to appear inside prose.
    /// </summary>
    private static readonly char[] Candidates = ['\t', '|', ';', ','];

    public IReadOnlyList<TabularTable> Read(DataSourceSpec spec)
    {
        if (!File.Exists(spec.Location))
            throw new FileNotFoundException($"No file at {spec.Location}.", spec.Location);

        var text = File.ReadAllText(spec.Location);
        var label = Path.GetFileName(spec.Location);

        var delimiter = SniffDelimiter(text)
            ?? throw new InvalidDataException(
                $"{label} could not find a delimiter — no tab, pipe, semicolon or comma splits "
                + "its lines into a consistent number of fields, so it does not look like a "
                + "table. If it is prose describing what you want, use `build app: <description>` "
                + "instead.");

        return new[]
        {
            ToTableFrom(text, delimiter,
                Path.GetFileNameWithoutExtension(spec.Location), label)
        };
    }

    private static TabularTable ToTableFrom(
        string text, char delimiter, string name, string label)
        => CsvReader.ToTable(name, label, CsvReader.ParseFields(text, delimiter));

    /// <summary>
    /// The delimiter that splits the first <see cref="SniffLines"/> lines into the same number
    /// of fields — more than one field, and the same count every line. Null when none does.
    /// </summary>
    /// <remarks>
    /// Consistency is the whole test. Prose is full of commas, but the number of them per line
    /// varies, so a comma never passes; a real CSV's comma count is fixed by its column count.
    /// The scan is quote-aware because it goes through <see cref="CsvReader.ParseFields"/>, so a
    /// quoted "Wills, Ada" does not count as two fields.
    /// </remarks>
    public static char? SniffDelimiter(string text)
    {
        foreach (var candidate in Candidates)
        {
            var records = CsvReader.ParseFields(text, candidate)
                .Take(SniffLines)
                .ToList();

            if (records.Count < 2)
                continue;

            var width = records[0].Length;
            if (width > 1 && records.All(r => r.Length == width))
                return candidate;
        }

        return null;
    }
}
