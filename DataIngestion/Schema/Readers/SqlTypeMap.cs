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
            ["time"] = "DateTime",
        };

    /// <summary>
    /// The model type for <paramref name="sqlType"/>. Anything unrecognised is <c>string</c> — the
    /// safe answer, and the one that loses nothing the source held.
    /// </summary>
    public static string ToModelType(string sqlType)
        => Types.TryGetValue(sqlType ?? string.Empty, out var type) ? type : "string";

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
            _ => null
        };
}
