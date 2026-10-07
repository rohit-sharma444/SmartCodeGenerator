using System.Text.Json;
using DomainDiscovery.SeedExtraction;

namespace DataIngestion.Schema.Readers;

/// <summary>
/// Reads a JSON document into tables. Unlike CSV and TXT, JSON already carries structure, and
/// this reader keeps it: a nested array is a child entity with a foreign key, not a column.
/// </summary>
public sealed class JsonTableReader : IDataSourceReader
{
    /// <summary>Appended to a parent's entity name to make the child's foreign-key column, so it
    /// matches the <c>&lt;Target&gt;Id</c> convention <c>ForeignKeys</c> and
    /// <c>DomainModelMapper</c> already share and the generated nav properties line up.</summary>
    public const string ParentIdSuffix = "Id";

    /// <summary>The value column of a child table extracted from an array of scalars.</summary>
    public const string ScalarValueColumn = "Value";

    public IReadOnlyList<TabularTable> Read(DataSourceSpec spec)
    {
        if (!File.Exists(spec.Location))
            throw new FileNotFoundException($"No file at {spec.Location}.", spec.Location);

        return ReadJson(
            File.ReadAllText(spec.Location),
            Path.GetFileNameWithoutExtension(spec.Location),
            Path.GetFileName(spec.Location));
    }

    /// <summary>Parses <paramref name="json"/> into a root table named after
    /// <paramref name="rootName"/>, plus a child table per nested array at any depth.</summary>
    public static IReadOnlyList<TabularTable> ReadJson(string json, string rootName, string label)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{label} is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;

            var objects = root.ValueKind switch
            {
                // A single object is a table of one row, not an error.
                JsonValueKind.Object => new List<JsonElement> { root.Clone() },
                JsonValueKind.Array => root.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Object)
                    .Select(e => e.Clone())
                    .ToList(),
                _ => throw new InvalidDataException(
                    $"{label} must hold an object or an array of objects at the top level."),
            };

            if (objects.Count == 0)
                throw new InvalidDataException(
                    $"{label} contains no objects, so there are no columns to read. An array of "
                    + "objects is what this reader needs.");

            var children = new List<TabularTable>();
            var tables = new List<TabularTable> { BuildTable(rootName, label, objects, children) };
            tables.AddRange(children);

            foreach (var table in tables)
                foreach (var column in table.Columns)
                {
                    var values = table.ColumnValues(column.Name).ToList();
                    column.Type = ColumnTypeInference.Infer(values, column.Name);
                }

            return tables;
        }
    }

    /// <summary>
    /// Builds one table from <paramref name="objects"/>, appending a child table to
    /// <paramref name="children"/> for each nested array. Columns are the UNION of every
    /// object's keys, in order of first appearance, so objects that disagree still line up.
    /// </summary>
    private static TabularTable BuildTable(
        string name, string label, List<JsonElement> objects, List<TabularTable> children)
    {
        var entityName = NameNormalizer.ToUserTypedEntityName(name);
        var table = new TabularTable { Name = entityName, SourceLabel = label };

        var columnNames = new List<string>();
        var cells = new List<Dictionary<string, string?>>();

        // Nested arrays, keyed by property name, each element tagged with the 1-BASED index of
        // the parent row it came from. That index is the child's foreign key, and it is 1-based
        // because seeded Ids are assigned 1..N in row order.
        var arrays = new Dictionary<string, List<(int ParentRow, JsonElement Element)>>(
            StringComparer.Ordinal);

        for (var rowIndex = 0; rowIndex < objects.Count; rowIndex++)
        {
            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            Flatten(objects[rowIndex], string.Empty, rowIndex + 1, row, columnNames, arrays);
            cells.Add(row);
        }

        foreach (var columnName in columnNames)
            table.Columns.Add(new TabularColumn { Name = columnName });

        foreach (var row in cells)
            table.Rows.Add(columnNames
                .Select(c => row.TryGetValue(c, out var v) ? v : null)
                .ToArray());

        // Recursive: an array inside an array is a grandchild, and the same rule applies.
        foreach (var (property, elements) in arrays)
        {
            var foreignKey = entityName + ParentIdSuffix;
            var childName = NameNormalizer.ToUserTypedEntityName(property);

            var allObjects = elements.All(e => e.Element.ValueKind == JsonValueKind.Object);

            children.Add(allObjects
                ? ObjectChild(childName, label, foreignKey, elements, children)
                : ScalarChild(childName, label, foreignKey, elements));
        }

        return table;
    }

    /// <summary>A child whose elements are objects: the parent FK first, then its own columns.</summary>
    private static TabularTable ObjectChild(
        string childName,
        string label,
        string foreignKey,
        List<(int ParentRow, JsonElement Element)> elements,
        List<TabularTable> children)
    {
        var inner = BuildTable(
            childName, label, elements.Select(e => e.Element).ToList(), children);

        var child = new TabularTable { Name = inner.Name, SourceLabel = label };
        child.Columns.Add(new TabularColumn { Name = foreignKey, Type = "int", HoldsRowNumbers = true });
        child.Columns.AddRange(inner.Columns);

        for (var i = 0; i < inner.Rows.Count; i++)
        {
            var row = new string?[child.Columns.Count];
            row[0] = elements[i].ParentRow.ToString();
            Array.Copy(inner.Rows[i], 0, row, 1, inner.Rows[i].Length);
            child.Rows.Add(row);
        }

        return child;
    }

    /// <summary>A child whose elements are scalars: the parent FK and one value column.</summary>
    private static TabularTable ScalarChild(
        string childName,
        string label,
        string foreignKey,
        List<(int ParentRow, JsonElement Element)> elements)
    {
        var child = new TabularTable { Name = childName, SourceLabel = label };
        child.Columns.Add(new TabularColumn { Name = foreignKey, Type = "int", HoldsRowNumbers = true });
        child.Columns.Add(new TabularColumn { Name = ScalarValueColumn });

        foreach (var (parentRow, element) in elements)
            child.Rows.Add([parentRow.ToString(), Scalar(element)]);

        return child;
    }

    /// <summary>
    /// Writes one object's scalars into <paramref name="row"/>, recording any new column name.
    /// A nested object is flattened under its property name (one-to-one needs no table); a
    /// nested array is set aside as a child table (one-to-many does).
    /// </summary>
    private static void Flatten(
        JsonElement element,
        string prefix,
        int parentRow,
        Dictionary<string, string?> row,
        List<string> columnNames,
        Dictionary<string, List<(int ParentRow, JsonElement Element)>> arrays)
    {
        foreach (var property in element.EnumerateObject())
        {
            var name = prefix + NameNormalizer.ToFieldName(property.Name);

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    Flatten(property.Value, name, parentRow, row, columnNames, arrays);
                    break;

                case JsonValueKind.Array:
                    if (!arrays.TryGetValue(property.Name, out var elements))
                        arrays[property.Name] = elements = new();

                    foreach (var item in property.Value.EnumerateArray())
                        elements.Add((parentRow, item.Clone()));
                    break;

                default:
                    if (!columnNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                        columnNames.Add(name);

                    row[name] = Scalar(property.Value);
                    break;
            }
        }
    }

    /// <summary>A scalar as the raw string every reader produces. JSON null is an absent value,
    /// which is what an empty CSV cell means too.</summary>
    private static string? Scalar(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            // A JSON number is numeric by its format, so an exponent is dropped here: inference
            // reads exponent-form TEXT as a code.
            JsonValueKind.Number => AppScaffolding.ImportedValues.PlainNumberText(element.GetRawText()),
            _ => element.GetRawText(),
        };
}
