namespace DataIngestion.Schema.Readers;

/// <summary>
/// SQL Server's type for a column, as one of the five types the generator can emit.
/// </summary>
/// <remarks>
/// A table, not inference. Every other reader hands strings to <see cref="ColumnTypeInference"/>
/// and asks what they were, which is where the leading-zero and thousands-separator corruptions
/// come from. A database column has a declared type, so this path never asks the question.
/// </remarks>
public static class SqlTypeMap
{
    private static readonly Dictionary<string, string> Types =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["int"] = "int",
            ["smallint"] = "int",
            ["tinyint"] = "int",
            ["bigint"] = "int",

            ["decimal"] = "decimal",
            ["numeric"] = "decimal",
            ["money"] = "decimal",
            ["smallmoney"] = "decimal",
            ["float"] = "decimal",
            ["real"] = "decimal",

            ["bit"] = "bool",

            ["date"] = "DateTime",
            ["datetime"] = "DateTime",
            ["datetime2"] = "DateTime",
            ["smalldatetime"] = "DateTime",
            ["datetimeoffset"] = "DateTime",

            // Not DateTime: a time of day ("09:30:00") is not a date, so every value failed to read
            // as one and was dropped, showing 01-Jan-01. None of the five model types holds a time
            // alone, so it is text -- every value kept, and LossNote says the type changed.
        };

    /// <summary>
    /// The model type for <paramref name="sqlType"/>. Anything unrecognised is <c>string</c> — the
    /// safe answer, and the one that loses nothing the source held.
    /// </summary>
    public static string ToModelType(string sqlType)
        => Types.TryGetValue(sqlType ?? string.Empty, out var type) ? type : "string";

    /// <summary>
    /// How many characters a value of <paramref name="sqlType"/> can take as TEXT in the model,
    /// given the length INFORMATION_SCHEMA declared for it; or <c>Unbounded</c> when the column
    /// sets no limit, and neither when nothing is known.
    /// </summary>
    /// <remarks>
    /// The declared length is not always a character count of the text the model holds. text,
    /// ntext and image report 2147483647 (or 1073741823): no limit at all, and a bound of
    /// int.MaxValue emits a validator check that cannot compile. A binary column reports BYTES,
    /// while its value is held as base64 text, four characters for every three bytes — a 16-byte
    /// hash is 24 characters and failed its own 16-character rule. time and uniqueidentifier
    /// report nothing, so they were given the generic 200; their text is never longer than
    /// hh:mm:ss.fffffff (16) and a GUID (36).
    /// </remarks>
    public static (int? MaxLength, bool Unbounded) TextLength(string sqlType, int? declared)
    {
        switch ((sqlType ?? string.Empty).ToLowerInvariant())
        {
            case "text" or "ntext" or "image" or "xml":
                return (null, true);
            case "time":
                return (16, false);
            case "uniqueidentifier":
                return (36, false);
        }

        if (declared == -1)
            return (null, true);

        if (declared is not > 0)
            return (null, false);

        return (sqlType ?? string.Empty).ToLowerInvariant() is "binary" or "varbinary"
            ? ((int)Math.Min(int.MaxValue, 4L * ((declared.Value + 2L) / 3)), false)
            : (declared, false);
    }

    /// <summary>
    /// A sentence naming what this column loses by being mapped, or null when it loses nothing.
    /// </summary>
    /// <remarks>
    /// Returned rather than logged so the caller decides where it appears. Only the two genuine
    /// losses are reported: a note against every column would be noise, and noise is how a warning
    /// stops being read.
    /// </remarks>
    public static string? LossNote(string sqlType, string columnName) =>
        (sqlType ?? string.Empty).ToLowerInvariant() switch
        {
            "bigint" =>
                $"{columnName} is bigint and becomes int — a value above {int.MaxValue} will not "
                + "survive.",
            "uniqueidentifier" =>
                $"{columnName} is uniqueidentifier and becomes string — correct to look at, but "
                + "no longer a Guid.",
            "time" =>
                $"{columnName} is time and becomes string (hh:mm:ss) — every value kept, but no "
                + "longer a TimeSpan.",
            "float" or "real" =>
                $"{columnName} is {sqlType!.ToLowerInvariant()} and becomes decimal — exact to 28 "
                + "digits, but a value beyond ±7.9E+28 cannot be kept, and one nearer zero than "
                + "1E-28 becomes 0.",
            "datetimeoffset" =>
                $"{columnName} is datetimeoffset and becomes DateTime — each value keeps the date "
                + "and time the database shows, but its UTC offset is dropped.",
            _ => null
        };
}
