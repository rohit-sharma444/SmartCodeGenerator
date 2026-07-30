using System.Text;

namespace DataIngestion.Schema.Readers;

/// <summary>
/// Reads a delimited text file into one <see cref="TabularTable"/>. RFC4180-shaped: a quoted
/// field may contain the delimiter, a newline, or a doubled quote standing for one literal quote.
/// </summary>
/// <remarks>
/// Hand-written rather than taken from a package, for the same reason the rest of this repo's
/// parsing is: one file, no new dependency, and every rule visible where it is tested.
/// <see cref="ParseFields"/> and <see cref="ToTable"/> are public and static because
/// <see cref="DelimitedTextReader"/> is the same two steps with a sniffed delimiter, and
/// duplicating a quote-aware scanner is how the two would drift apart.
/// </remarks>
public sealed class CsvReader : IDataSourceReader
{
    public IReadOnlyList<TabularTable> Read(DataSourceSpec spec)
    {
        if (!File.Exists(spec.Location))
            throw new FileNotFoundException($"No file at {spec.Location}.", spec.Location);

        var records = ParseFields(File.ReadAllText(spec.Location), ',');

        return new[]
        {
            ToTable(
                Path.GetFileNameWithoutExtension(spec.Location),
                Path.GetFileName(spec.Location),
                records)
        };
    }

    /// <summary>
    /// Splits <paramref name="text"/> into records of fields. The first record is the header.
    /// A field is null when the source wrote nothing there; an explicitly quoted empty field
    /// ("") is an empty string, because the source said so.
    /// </summary>
    public static IReadOnlyList<string?[]> ParseFields(string text, char delimiter)
    {
        var records = new List<string?[]>();
        var fields = new List<string?>();
        var current = new StringBuilder();

        var quoted = false;       // inside a "..." field
        var wasQuoted = false;    // this field was quoted, so "" means empty, not absent
        var started = false;      // any character seen since the last delimiter/newline

        // A BOM is an encoding marker, not data. Left in place it becomes part of the first
        // column's name, and every later lookup of that column silently misses.
        var body = text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;

        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];

            if (quoted)
            {
                if (c != '"')
                {
                    current.Append(c);
                    continue;
                }

                // "" inside a quoted field is one literal quote; a lone " ends the field.
                if (i + 1 < body.Length && body[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                    continue;
                }

                quoted = false;
                continue;
            }

            if (c == '"' && !started)
            {
                quoted = true;
                wasQuoted = true;
                started = true;
                continue;
            }

            if (c == delimiter)
            {
                EndField();
                continue;
            }

            if (c is '\n' or '\r')
            {
                // Swallow the LF of a CRLF so it does not open an empty record.
                if (c == '\r' && i + 1 < body.Length && body[i + 1] == '\n')
                    i++;

                EndField();
                EndRecord();
                continue;
            }

            current.Append(c);
            started = true;
        }

        // A file not ending in a newline still has a last record.
        if (started || fields.Count > 0)
        {
            EndField();
            EndRecord();
        }

        return records;

        void EndField()
        {
            fields.Add(current.Length == 0 && !wasQuoted ? null : current.ToString());
            current.Clear();
            wasQuoted = false;
            started = false;
        }

        void EndRecord()
        {
            // A blank line between records is not a row of nulls.
            if (fields.Count > 1 || fields.Any(f => f is not null))
                records.Add(fields.ToArray());

            fields.Clear();
        }
    }

    /// <summary>
    /// Turns parsed records into a typed table: the first record names the columns, the rest are
    /// squared off to the header's width, and each column is typed from its own values.
    /// </summary>
    public static TabularTable ToTable(
        string name, string label, IReadOnlyList<string?[]> records)
    {
        if (records.Count == 0)
            throw new InvalidDataException($"{label} is empty — there is no header row to read.");

        if (records.Count == 1)
            throw new InvalidDataException(
                $"{label} has a header but no data rows, so there are no values to infer types "
                + "from. Add at least one row, or build the app from a description instead.");

        var header = records[0];
        var table = new TabularTable { Name = name, SourceLabel = label };

        for (var i = 0; i < header.Length; i++)
        {
            // An unnamed column is still addressable, and its position is the only name it has.
            var columnName = string.IsNullOrWhiteSpace(header[i])
                ? $"Column{i + 1}"
                : header[i]!.Trim();

            table.Columns.Add(new TabularColumn { Name = columnName });
        }

        foreach (var record in records.Skip(1))
        {
            var row = new string?[header.Length];
            for (var i = 0; i < header.Length; i++)
                row[i] = i < record.Length ? record[i] : null;

            table.Rows.Add(row);
        }

        foreach (var column in table.Columns)
            column.Type = ColumnTypeInference.Infer(table.ColumnValues(column.Name), column.Name);

        return table;
    }
}
