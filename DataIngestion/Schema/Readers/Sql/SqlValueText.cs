using System.Globalization;

namespace DataIngestion.Schema.Readers.Sql;

/// <summary>
/// A value read from SQL Server as the string <see cref="TabularTable.Rows"/> holds.
/// </summary>
/// <remarks>
/// Invariant throughout, and round-trip formats for the types where a locale would otherwise
/// change the meaning. This is the opposite direction of travel from the file readers: the type
/// is known and the string is derived from it, rather than the string being read and the type
/// guessed. Kept apart from <see cref="SqlClientSchemaSource"/> because what each value becomes
/// is a decision, and the adapter is the one piece no test can reach.
/// </remarks>
public static class SqlValueText
{
    public static string Render(object value) => value switch
    {
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),

        // The wall-clock time the source shows, WITHOUT its offset. The model holds a DateTime,
        // which cannot keep an arbitrary offset: written with one, the seed loaded as the
        // generating machine's local time, so 12:00 -12:00 showed as 5:30 the next morning on a
        // +05:30 machine. SqlTypeMap.LossNote says the offset is dropped.
        DateTimeOffset offset => offset.DateTime.ToString("O", CultureInfo.InvariantCulture),

        // Words rather than 0/1, matching what ExcelSheetReader emits for the same reason:
        // ColumnTypeInference recognises true/false, and a bool that reads back as an int is a
        // column that silently changes type.
        bool flag => flag ? "true" : "false",

        byte[] bytes => Convert.ToBase64String(bytes),

        // Uppercase, as SQL Server Management Studio shows it. The app compares text exactly,
        // so a lowercase seed let a GUID pasted from SSMS save as a second record.
        Guid guid => guid.ToString("D").ToUpperInvariant(),

        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
