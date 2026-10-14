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
                        continue;

                    if (!Write(writer, property, value))
                    {
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

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
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
                    // Written by hand, not the DateTimeOffset overload: that one renders a zero
                    // offset as "+00:00", where "Z" is what the source itself wrote.
                    var offset = withZone.Offset == TimeSpan.Zero
                        ? "Z"
                        : withZone.ToString("zzz", CultureInfo.InvariantCulture);
                    writer.WriteString(
                        property.Name,
                        withZone.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture)
                            + offset);
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
