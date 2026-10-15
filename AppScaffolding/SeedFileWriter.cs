using System.Globalization;
using System.Text.Json;
using CodeGenerationEngine.Models;

namespace AppScaffolding;

/// <summary>
/// Renders imported rows as the seed files the generated app loads on first run: one
/// <c>&lt;Class&gt;.json</c> per entity that contributed data.
/// </summary>
/// <remarks>
/// It walks each class's FINAL property list rather than the data's keys, and that is the whole
/// design. The review loop can remove, rename, add and retype fields after the data was read, so
/// projecting through the class is what keeps the seed file and the generated class in step
/// automatically — a removed field stops being written, and no file can ever name a property that
/// does not exist.
/// <para>
/// Everything goes through <see cref="Utf8JsonWriter"/>, never string concatenation, so a cell
/// containing quotes and braces stays one string instead of becoming syntax.
/// </para>
/// </remarks>
public static class SeedFileWriter
{
    /// <summary>Folder under the generated project's <c>Data/</c> that holds the seed files.</summary>
    public const string SeedFolderName = "Seed";

    /// <summary>
    /// The files to write, as (file name, JSON) pairs. A class with no imported rows gets none —
    /// the generated seeder falls back to its sample rows for those.
    /// </summary>
    public static IReadOnlyList<(string FileName, string Json)> Files(
        IReadOnlyList<ClassDefinition> classes,
        IReadOnlyDictionary<string, string> classNames,
        ImportedData data)
        => Files(classes, classNames, data, out _);

    /// <summary>
    /// As <see cref="Files(IReadOnlyList{ClassDefinition}, IReadOnlyDictionary{string, string}, ImportedData)"/>,
    /// and says what was left out: one sentence per field holding a value its type cannot read.
    /// </summary>
    /// <remarks>
    /// Left out rather than written, because a JSON string in an int property fails the whole file
    /// at load. But never silently: the user's own value is not in the app, and this is the only
    /// place that knows it.
    /// </remarks>
    public static IReadOnlyList<(string FileName, string Json)> Files(
        IReadOnlyList<ClassDefinition> classes,
        IReadOnlyDictionary<string, string> classNames,
        ImportedData data,
        out IReadOnlyList<string> unreadable)
    {
        var skipped = new List<string>();
        unreadable = skipped;

        // Class name -> entity name. Rows are keyed by entity; files are named for the class; and
        // DomainModelMapper.ToUniqueClassName can rename one ("Type" -> "TypeItem").
        var entityByClass = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (entity, className) in classNames)
            entityByClass[className] = entity;

        // A foreign key renamed with its class (LibraryId -> LibraryItemId) still has its value
        // filed under the old name in the rows, which were read before the rename.
        var renamedKeys = DomainModelMapper.RenamedKeys(classNames);

        var files = new List<(string, string)>();

        foreach (var definition in classes)
        {
            if (!entityByClass.TryGetValue(definition.Name, out var entityName))
                continue;

            var rows = data.For(entityName);
            if (rows.Count == 0)
                continue;

            files.Add((definition.Name + ".json", Render(definition, rows, renamedKeys, skipped)));
        }

        return files;
    }

    private static string Render(
        ClassDefinition definition,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows,
        IReadOnlyDictionary<string, string> renamedKeys,
        List<string> skipped)
    {
        // Per property, in declaration order: how many values it could not read, and the first.
        var misses = new Dictionary<string, (int Count, string First)>(StringComparer.Ordinal);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartArray();

            for (var index = 0; index < rows.Count; index++)
            {
                writer.WriteStartObject();

                foreach (var property in definition.Properties)
                {
                    // Ids are assigned here, 1..N in row order — which is exactly what the foreign
                    // keys the readers and NormalizationPlan emitted already point at, and what
                    // TabularImport rewrote every key read from the source to.
                    if (string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
                    {
                        writer.WriteNumber("Id", index + 1);
                        continue;
                    }

                    // A navigation property is resolved from its foreign key at load time. Writing
                    // it would make Order -> Customer -> Orders a cycle, which is why
                    // DomainModelMapper marks these [JsonIgnore].
                    if (!IsScalar(property.Type))
                        continue;

                    if ((!rows[index].TryGetValue(property.Name, out var raw)
                         && !(renamedKeys.TryGetValue(property.Name, out var oldName)
                              && rows[index].TryGetValue(oldName, out raw)))
                        || SeedText(property, raw) is not { } value)
                    {
                        WriteMissing(writer, property);
                        continue;
                    }

                    if (!Write(writer, property, value))
                    {
                        WriteMissing(writer, property);

                        misses[property.Name] = misses.TryGetValue(property.Name, out var miss)
                            ? (miss.Count + 1, miss.First)
                            : (1, value);
                    }
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        foreach (var property in definition.Properties)
        {
            if (misses.TryGetValue(property.Name, out var miss))
                skipped.Add(
                    $"{definition.Name}.{property.Name}: {miss.Count} value(s) could not be read as "
                    + $"{DomainModelMapper.BaseType(property.Type)} and were left out of the seed data, the first being "
                    + $"\"{miss.First}\"");
        }

        NoteDroppedOffsets(definition, rows, renamedKeys, skipped);

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// One sentence per DateTime field whose source values named a UTC offset, which the seed
    /// leaves out (see <see cref="Write"/>): how many, and the first, as the source wrote it.
    /// </summary>
    private static void NoteDroppedOffsets(
        ClassDefinition definition,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows,
        IReadOnlyDictionary<string, string> renamedKeys,
        List<string> notes)
    {
        foreach (var property in definition.Properties)
        {
            if (DomainModelMapper.BaseType(property.Type) != "DateTime")
                continue;

            var count = 0;
            string? first = null;

            foreach (var row in rows)
            {
                if ((row.TryGetValue(property.Name, out var raw)
                     || (renamedKeys.TryGetValue(property.Name, out var oldName)
                         && row.TryGetValue(oldName, out raw)))
                    && raw is not null
                    && ImportedValues.TryDateTimeOffset(raw.Trim(), out _))
                {
                    count++;
                    first ??= raw.Trim();
                }
            }

            if (count > 0)
                notes.Add(
                    $"{definition.Name}.{property.Name}: {count} value(s) named a UTC offset, which a "
                    + "DateTime cannot hold; each keeps the date and time the source shows, without its "
                    + $"offset, the first being \"{first}\"");
        }
    }

    /// <summary>
    /// The text <paramref name="property"/>'s value is written from, or null for a missing one.
    /// </summary>
    /// <remarks>
    /// A string from a source that DECLARED its type (SQL Server, the one source that does) is
    /// the value as stored: its leading and trailing spaces are the user's data, and a value of
    /// spaces alone is not a missing one. Only a fixed-width char/nchar loses its trailing spaces,
    /// which are padding the database adds and ignores when it compares. Everything else is
    /// read from text a person typed into a file, and is trimmed as it always was.
    /// </remarks>
    private static string? SeedText(PropertyDefinition property, string? raw)
    {
        if (raw is null)
            return null;

        if (DomainModelMapper.BaseType(property.Type) == "string" && property.Facts?.DeclaredType is { } declared)
        {
            return declared.ToLowerInvariant() is "char" or "nchar"
                ? raw.TrimEnd(' ')
                : raw;
        }

        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    /// <summary>
    /// Writes the value a missing one loads as without a declared DEFAULT, when leaving it out
    /// would load something else: a property with a DEFAULT starts every new instance at it, so
    /// a row the source stored NULL in would have loaded as that default instead.
    /// </summary>
    /// <remarks>
    /// Null for a nullable property; an empty string for a string, which is what a missing text
    /// loads as everywhere else (a null there would break a SQLite store's NOT NULL column). Any
    /// other property still leaves a missing value out, as it always has.
    /// </remarks>
    private static void WriteMissing(Utf8JsonWriter writer, PropertyDefinition property)
    {
        if (property.Initializer is null)
            return;

        if (DomainModelMapper.IsNullable(property.Type))
            writer.WriteNull(property.Name);
        else if (property.Type == "string")
            writer.WriteString(property.Name, string.Empty);
    }

    /// <summary>The five types the model can carry. Anything else is a navigation property.</summary>
    private static bool IsScalar(string type)
        => DomainModelMapper.BaseType(type) is "string" or "int" or "decimal" or "bool" or "DateTime";

    /// <summary>
    /// Writes one value as its declared JSON type, or returns false and writes nothing. A value
    /// that does not parse is OMITTED, leaving the class's default: the user may have retyped the
    /// field in the review loop, and the row should still load rather than the whole file failing.
    /// </summary>
    /// <remarks>
    /// Parsed by <see cref="ImportedValues"/>, the same rules column inference typed the field by,
    /// so an inferred type always reads its own column.
    /// </remarks>
    private static bool Write(Utf8JsonWriter writer, PropertyDefinition property, string raw)
    {
        // The base type: a nullable property reads its values as its kind does. A missing value is
        // omitted before this is reached, and an omitted nullable property loads as null.
        switch (DomainModelMapper.BaseType(property.Type))
        {
            case "int":
                if (!ImportedValues.TryInt(raw, out var i))
                    return false;
                writer.WriteNumber(property.Name, i);
                return true;

            case "decimal":
                if (!ImportedValues.TryDecimal(raw, out var d))
                    return false;
                writer.WriteNumber(property.Name, d);
                return true;

            case "bool":
                if (!ImportedValues.TryBoolean(raw, out var b))
                    return false;
                writer.WriteBoolean(property.Name, b);
                return true;

            case "DateTime":
                if (ImportedValues.TryDateTimeOffset(raw, out var withZone))
                {
                    // The wall-clock time the source shows, WITHOUT its offset. A DateTime cannot
                    // hold one: written with it, System.Text.Json loaded the value as the RUNNING
                    // machine's local time (08:00-08:00 showed as 21:30 in India), and a re-save
                    // lost the offset anyway. NoteDroppedOffsets says so, per field.
                    writer.WriteString(
                        property.Name,
                        withZone.DateTime.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture));
                    return true;
                }
                if (!ImportedValues.TryDateTime(raw, out var moment))
                    return false;
                writer.WriteString(property.Name, moment);
                return true;

            default:
                writer.WriteString(property.Name, raw);
                return true;
        }
    }
}
