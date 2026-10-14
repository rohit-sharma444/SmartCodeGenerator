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

        var tables = ReadJson(
            SourceText.Read(spec.Location, out var encodingNote),
            Path.GetFileNameWithoutExtension(spec.Location),
            Path.GetFileName(spec.Location));

        if (encodingNote is not null)
            tables[0].Notes.Insert(0, encodingNote);

        return tables;
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

            // {"orders": [ ... ]} is a wrapper, not a record: read as one, it became an entity of
            // its own with one row and no columns of its own, and the orders were its children.
            // The one array of objects IS the data, named by its property.
            if (Wrapped(root) is { } wrapped)
            {
                root = wrapped.Value;
                rootName = wrapped.Name;
            }

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
    /// The array inside a root object whose ONLY property is a non-empty array of objects, with
    /// that property's name; null for any other root.
    /// </summary>
    private static (string Name, JsonElement Value)? Wrapped(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var properties = root.EnumerateObject().ToList();

        if (properties.Count != 1)
            return null;

        var value = properties[0].Value;

        return value.ValueKind == JsonValueKind.Array
               && value.GetArrayLength() > 0
               && value.EnumerateArray().All(e => e.ValueKind == JsonValueKind.Object)
            ? (properties[0].Name, value)
            : null;
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

        var columns = new Columns();
        var cells = new List<Dictionary<string, string?>>();

        // Nested arrays, keyed by their PATH (billing.contacts), each element tagged with the
        // 1-BASED index of the parent row it came from. That index is the child's foreign key,
        // and it is 1-based because seeded Ids are assigned 1..N in row order. Keyed by the
        // property name alone, billing.contacts and shipping.contacts became one table.
        var arrays = new Dictionary<string, NestedArray>(StringComparer.Ordinal);

        for (var rowIndex = 0; rowIndex < objects.Count; rowIndex++)
        {
            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            Flatten(objects[rowIndex], string.Empty, string.Empty, rowIndex + 1, row, columns, arrays);
            cells.Add(row);
        }

        var columnNames = columns.Names;

        foreach (var columnName in columnNames)
            table.Columns.Add(new TabularColumn
            {
                Name = columnName,
                DuplicateOf = columns.DuplicateOf.GetValueOrDefault(columnName)
            });

        if (columns.DuplicateOf.Count > 0)
            table.Notes.Add(
                $"In {label}, two properties of each {entityName} would both be called "
                + string.Join(" and ", columns.DuplicateOf.Values.Distinct())
                + ", so the later one was numbered to keep its own values: "
                + string.Join(", ", columns.DuplicateOf.Select(d => $"{d.Value} as {d.Key}"))
                + ".");

        foreach (var row in cells)
            table.Rows.Add(columnNames
                .Select(c => row.TryGetValue(c, out var v) ? v : null)
                .ToArray());

        // Two arrays that would share a name (billing.contacts, shipping.contacts) are each named
        // for their parent as well: BillingContact and ShippingContact.
        var sharedNames = arrays.Values
            .GroupBy(a => NameNormalizer.ToUserTypedEntityName(a.Property), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Recursive: an array inside an array is a grandchild, and the same rule applies.
        foreach (var (property, namePrefix, elements) in arrays.Values)
        {
            var foreignKey = entityName + ParentIdSuffix;
            var childName = NameNormalizer.ToUserTypedEntityName(property);

            if (sharedNames.Contains(childName) && namePrefix.Length > 0)
                childName = NameNormalizer.ToUserTypedEntityName(namePrefix + NameNormalizer.ToFieldName(property));

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
        child.Notes.AddRange(inner.Notes);
        child.Columns.Add(new TabularColumn { Name = foreignKey, Type = "int", HoldsRowNumbers = true });

        // A child that also carries its parent's key itself ("orderId": 1001 inside each line)
        // holds the same link the nesting already gives. Kept as a second column of the same name,
        // every lookup by name found the first, and the import then read the source keys as row
        // numbers or the row numbers as source keys: links cleared, or pointed at the wrong
        // parent. The nesting is the link that cannot be wrong, so the copy is left out.
        var duplicate = inner.IndexOf(foreignKey);

        for (var c = 0; c < inner.Columns.Count; c++)
            if (c != duplicate)
                child.Columns.Add(inner.Columns[c]);

        if (duplicate >= 0)
            child.Notes.Add(
                $"In {label}, each {childName} item's own {inner.Columns[duplicate].Name} names the "
                + $"{NameNormalizer.ToUserTypedEntityName(foreignKey[..^ParentIdSuffix.Length])} it is "
                + "nested in, so it was read as that link rather than kept as a second column.");

        for (var i = 0; i < inner.Rows.Count; i++)
        {
            var row = new string?[child.Columns.Count];
            row[0] = elements[i].ParentRow.ToString();

            var to = 1;
            for (var c = 0; c < inner.Rows[i].Length; c++)
                if (c != duplicate)
                    row[to++] = inner.Rows[i][c];

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
        string pathPrefix,
        int parentRow,
        Dictionary<string, string?> row,
        Columns columns,
        Dictionary<string, NestedArray> arrays)
    {
        foreach (var property in element.EnumerateObject())
        {
            var name = prefix + NameNormalizer.ToFieldName(property.Name);

            // The property's place in the source, exactly as spelled: what tells "customerName"
            // from customer.name, and "total" from "Total", once both are CustomerName or Total.
            var path = pathPrefix + "/" + property.Name;

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    Flatten(property.Value, name, path, parentRow, row, columns, arrays);
                    break;

                case JsonValueKind.Array:
                    if (!arrays.TryGetValue(path, out var nested))
                        arrays[path] = nested = new NestedArray(property.Name, prefix, new());

                    foreach (var item in property.Value.EnumerateArray())
                        nested.Elements.Add((parentRow, item.Clone()));
                    break;

                default:
                    row[columns.For(path, name)] = Scalar(property.Value);
                    break;
            }
        }
    }

    /// <summary>One nested array of a table: the property it was under, the flattened name of
    /// the object holding it (empty at the top), and its elements.</summary>
    private sealed record NestedArray(
        string Property, string NamePrefix, List<(int ParentRow, JsonElement Element)> Elements);

    /// <summary>
    /// A table's columns, one per source PATH. Two paths that flatten to one name are two
    /// columns, the later numbered (CustomerName2), where one dictionary entry used to take
    /// whichever value came last.
    /// </summary>
    private sealed class Columns
    {
        private readonly Dictionary<string, string> byPath = new(StringComparer.Ordinal);

        public List<string> Names { get; } = new();

        /// <summary>Numbered column -> the name it would have had.</summary>
        public Dictionary<string, string> DuplicateOf { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string For(string path, string name)
        {
            if (byPath.TryGetValue(path, out var column))
                return column;

            column = CsvReader.UniqueColumnName(name, Names);

            if (column != name)
                DuplicateOf[column] = name;

            Names.Add(column);
            byPath[path] = column;
            return column;
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
