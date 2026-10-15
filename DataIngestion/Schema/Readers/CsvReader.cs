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

        var table = FromText(
            Path.GetFileNameWithoutExtension(spec.Location),
            Path.GetFileName(spec.Location),
            SourceText.Read(spec.Location, out var encodingNote),
            ',');

        if (encodingNote is not null)
            table.Notes.Insert(0, encodingNote);

        return new[] { table };
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

        var renamed = new List<string>();

        for (var i = 0; i < header.Length; i++)
        {
            // An unnamed column is still addressable, and its position is the only name it has.
            var columnName = string.IsNullOrWhiteSpace(header[i])
                ? $"Column{i + 1}"
                : header[i]!.Trim();

            // Every column is looked up by name, ignoring case, so a second Notes read the first
            // one's values. Numbered, it keeps its own; DuplicateOf keeps the name it came with.
            var unique = UniqueColumnName(columnName, table.Columns.Select(c => c.Name));

            if (unique != columnName)
                renamed.Add($"{columnName} as {unique}");

            table.Columns.Add(new TabularColumn
            {
                Name = unique,
                DuplicateOf = unique == columnName ? null : columnName
            });
        }

        if (renamed.Count > 0)
            table.Notes.Add(
                $"In {label}, an earlier column already had the name, so the next one was "
                + $"numbered to keep its own values: {string.Join(", ", renamed)}.");

        foreach (var record in records.Skip(1))
        {
            var row = new string?[header.Length];
            for (var i = 0; i < header.Length; i++)
                row[i] = i < record.Length ? record[i] : null;

            table.Rows.Add(row);
        }

        ReadNullTextAsMissing(table);

        foreach (var column in table.Columns)
        {
            var values = table.ColumnValues(column.Name).ToList();
            column.Type = ColumnTypeInference.Infer(values, column.Name);
        }

        return table;
    }

    /// <summary>
    /// A delimited text file as a table: <see cref="ParseFields"/>, then <see cref="ToTable"/>,
    /// with every row whose field count differs from the header's named in a note.
    /// </summary>
    /// <remarks>
    /// A row with MORE fields than the header is left out. Its extra field almost always comes
    /// from an unquoted delimiter inside a value ("Smith, John"), and which value it split cannot
    /// be known: cut to the header's width, the row put "John" in Qty and lost the 2, and that one
    /// row made the whole Qty column text. Extra fields that are all empty (a trailing delimiter)
    /// hold nothing, so that row is kept as it is. A row with FEWER fields is kept, its missing
    /// fields empty — the usual way an export ends a row early — but named all the same, since a
    /// field dropped from the middle would shift the rest. Only text files: an Excel row is short
    /// whenever its last cells are empty, which says nothing about its shape.
    /// </remarks>
    public static TabularTable FromText(string name, string label, string text, char delimiter)
    {
        var records = ParseFields(text, delimiter);

        if (records.Count < 2)
            return ToTable(name, label, records);

        var width = records[0].Length;
        var kept = new List<string?[]> { records[0] };
        var longer = new List<string>();
        var shorter = new List<string>();

        for (var r = 1; r < records.Count; r++)
        {
            var record = records[r];
            var where = $"row {r} ({record[0]?.Trim()})";

            if (record.Length > width && record.Skip(width).Any(f => !string.IsNullOrEmpty(f)))
            {
                longer.Add(where);
                continue;
            }

            if (record.Length < width)
                shorter.Add(where);

            kept.Add(record);
        }

        var table = ToTable(name, label, kept);

        if (longer.Count > 0)
            table.Notes.Add(
                $"In {label}, {Rows(longer)} more fields than the header's {width}, so "
                + $"{(longer.Count == 1 ? "it was" : "they were")} left out rather than have values "
                + $"land in the wrong columns (a value holding the delimiter needs quotes): "
                + $"{string.Join(", ", longer)}.");

        if (shorter.Count > 0)
            table.Notes.Add(
                $"In {label}, {Rows(shorter)} fewer fields than the header's {width}; "
                + $"the missing fields were left empty: {string.Join(", ", shorter)}.");

        return table;

        static string Rows(List<string> rows)
            => rows.Count == 1 ? "1 row has" : $"{rows.Count} rows have";
    }

    /// <summary><paramref name="name"/>, or the first of name2, name3... that no name in
    /// <paramref name="taken"/> has, ignoring case.</summary>
    public static string UniqueColumnName(string name, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);

        if (!used.Contains(name))
            return name;

        for (var n = 2; ; n++)
        {
            var candidate = name + n;
            if (!used.Contains(candidate))
                return candidate;
        }
    }

    /// <summary>The spelling a file source uses for "no value here".</summary>
    public const string NullText = "NULL";

    /// <summary>
    /// Clears every cell that is exactly <see cref="NullText"/> (trimmed, any case), and says how
    /// many each column had.
    /// </summary>
    /// <remarks>
    /// A file has no null of its own, so SQL Server Management Studio, a pasted query result and
    /// most database exports write the word instead. Kept as data, one NULL made a column of
    /// numbers text, was seeded as the word, and was offered as an allowed value beside yes and
    /// no. Read as missing, a column that is genuinely text loses nothing a user would want: the
    /// cell becomes empty, which is what NULL meant. Only an exact cell is cleared, never one
    /// that merely contains the word ("Nullable", "Annul").
    /// </remarks>
    private static void ReadNullTextAsMissing(TabularTable table)
    {
        var counts = new int[table.Columns.Count];

        foreach (var row in table.Rows)
        {
            for (var i = 0; i < row.Length; i++)
            {
                if (row[i] is { } cell
                    && string.Equals(cell.Trim(), NullText, StringComparison.OrdinalIgnoreCase))
                {
                    row[i] = null;
                    counts[i]++;
                }
            }
        }

        var columns = table.Columns
            .Select((column, i) => (column.Name, Count: counts[i]))
            .Where(c => c.Count > 0)
            .Select(c => $"{c.Name} ({c.Count})")
            .ToList();

        if (columns.Count > 0)
            table.Notes.Add(
                $"In {table.Label}, cells holding the text {NullText} were read as missing values: "
                + $"{string.Join(", ", columns)}.");
    }
}
