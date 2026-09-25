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
    {
        // Class name -> entity name. Rows are keyed by entity; files are named for the class; and
        // DomainModelMapper.ToUniqueClassName can rename one ("Type" -> "TypeItem").
        var entityByClass = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (entity, className) in classNames)
            entityByClass[className] = entity;

        var files = new List<(string, string)>();

        foreach (var definition in classes)
        {
            if (!entityByClass.TryGetValue(definition.Name, out var entityName))
                continue;

            var rows = data.For(entityName);
            if (rows.Count == 0)
                continue;

            files.Add((definition.Name + ".json", Render(definition, rows)));
        }

        return files;
    }

    private static string Render(
        ClassDefinition definition, IReadOnlyList<IReadOnlyDictionary<string, string?>> rows)
    {
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

                    if (!rows[index].TryGetValue(property.Name, out var raw)
                        || string.IsNullOrWhiteSpace(raw))
                        continue;

                    Write(writer, property, raw.Trim());
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>The five types the model can carry. Anything else is a navigation property.</summary>
    private static bool IsScalar(string type)
        => type is "string" or "int" or "decimal" or "bool" or "DateTime";

    /// <summary>
    /// Writes one value as its declared JSON type. A value that does not parse is OMITTED, leaving
    /// the class's default: the user may have retyped the field in the review loop, and the row
    /// should still load rather than the whole file failing.
    /// </summary>
    private static void Write(Utf8JsonWriter writer, PropertyDefinition property, string raw)
    {
        switch (property.Type)
        {
            case "int":
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                    writer.WriteNumber(property.Name, i);
                break;

            case "decimal":
                if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                    writer.WriteNumber(property.Name, d);
                break;

            case "bool":
                if (TryBoolean(raw, out var b))
                    writer.WriteBoolean(property.Name, b);
                break;

            case "DateTime":
                if (DateTime.TryParse(
                        raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment))
                    writer.WriteString(property.Name, moment);
                break;

            default:
                writer.WriteString(property.Name, raw);
                break;
        }
    }

    /// <summary>The word forms <c>ColumnTypeInference</c> accepts as boolean. Kept in step with it:
    /// a column typed bool from "yes"/"no" has to be writable as one.</summary>
    private static bool TryBoolean(string raw, out bool value)
    {
        switch (raw.ToLowerInvariant())
        {
            case "true" or "yes" or "y" or "1":
                value = true;
                return true;
            case "false" or "no" or "n" or "0":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }
}
