namespace DataIngestion.Schema.Readers;

/// <summary>
/// Maps a <see cref="DataSourceKind"/> to its reader, and answers for the one kind that has no
/// reader yet. The interview offers every kind, so exactly one place has to know which are
/// implemented — this one.
/// </summary>
public static class DataSourceReaders
{
    /// <summary>The prompt's menu, in the order the requirements listed the formats.</summary>
    public static IReadOnlyList<string> Keys { get; } = ["sql", "excel", "txt", "json", "csv"];

    /// <summary>Spellings a user may reasonably type for each kind.</summary>
    private static readonly Dictionary<string, DataSourceKind> Aliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["sql"] = DataSourceKind.Sql,
            ["sqlserver"] = DataSourceKind.Sql,
            ["mssql"] = DataSourceKind.Sql,
            ["database"] = DataSourceKind.Sql,
            ["excel"] = DataSourceKind.Excel,
            ["xlsx"] = DataSourceKind.Excel,
            ["xls"] = DataSourceKind.Excel,
            ["spreadsheet"] = DataSourceKind.Excel,
            ["txt"] = DataSourceKind.Txt,
            ["text"] = DataSourceKind.Txt,
            ["tsv"] = DataSourceKind.Txt,
            ["json"] = DataSourceKind.Json,
            ["csv"] = DataSourceKind.Csv,
        };

    public static bool TryParseKind(string? text, out DataSourceKind kind)
    {
        kind = default;
        return !string.IsNullOrWhiteSpace(text) && Aliases.TryGetValue(text.Trim(), out kind);
    }

    public static bool TryCreate(DataSourceKind kind, out IDataSourceReader reader)
    {
        switch (kind)
        {
            case DataSourceKind.Csv:
                reader = new CsvReader();
                return true;
            case DataSourceKind.Txt:
                reader = new DelimitedTextReader();
                return true;
            case DataSourceKind.Json:
                reader = new JsonTableReader();
                return true;
            case DataSourceKind.Excel:
                reader = new ExcelReader();
                return true;
            case DataSourceKind.Sql:
                reader = new Sql.SqlServerReader(new Sql.SqlClientSchemaSource());
                return true;
            default:
                reader = null!;
                return false;
        }
    }

    /// <summary>What to tell a user who picked a kind this build cannot read.</summary>
    /// <remarks>
    /// Every declared kind has a reader as of the SQL Server branch, so nothing reaches this
    /// today. It stays because <see cref="TryCreate"/> is the one place that knows, and a
    /// kind added to <see cref="DataSourceKind"/> before its reader needs something to say.
    /// <para>
    /// The working formats are ASKED FOR rather than listed here. The hand-written list this
    /// replaces went stale the moment Excel shipped, and again the moment SQL did.
    /// </para>
    /// </remarks>
    public static string NotSupportedMessage(DataSourceKind kind)
        => $"{kind} is not in this build yet. Working formats: "
         + string.Join(
               ", ",
               Keys.Where(key => TryParseKind(key, out var parsed) && TryCreate(parsed, out _)))
         + ".";
}
