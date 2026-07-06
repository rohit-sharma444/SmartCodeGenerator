using System.Text.Json;
using System.Text.Json.Serialization;

namespace NaturalLanguageEngine.Core.Prompts;

/// <summary>
/// Persists prompts (and their analysis results) submitted to the engine as a
/// JSON array on disk. Every call to <see cref="Append"/> reads whatever is
/// already saved at the target path, adds the new record, and writes the
/// combined list back out — so the file accumulates every prompt ever
/// submitted across runs, rather than being overwritten each time.
/// </summary>
public sealed class PromptStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,

        // SemanticResult's Entities/Relationships/Diagnostics (and Entity's
        // Metadata) are read-only collection properties - no setter, just a
        // getter backed by a field initializer. Without this, the default
        // "Replace" creation handling can't assign a new collection (no
        // setter to assign it through) and, since there's nothing else it
        // can do, silently discards that property's JSON on deserialize
        // instead of raising an error. "Populate" instead adds the
        // deserialized items into the existing collection instance, which is
        // what actually round-trips this data correctly.
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
    };

    /// <summary>
    /// Loads all previously saved prompt records from <paramref name="filePath"/>.
    /// Returns an empty list if the file does not exist or is empty.
    /// </summary>
    public List<PromptRecord> Load(string filePath)
    {
        if (!File.Exists(filePath))
            return new List<PromptRecord>();

        var json = File.ReadAllText(filePath);
        if (string.IsNullOrWhiteSpace(json))
            return new List<PromptRecord>();

        return JsonSerializer.Deserialize<List<PromptRecord>>(json, SerializerOptions)
               ?? new List<PromptRecord>();
    }

    /// <summary>
    /// Appends <paramref name="record"/> to the JSON file at <paramref name="filePath"/>,
    /// preserving any records already saved there.
    /// </summary>
    public void Append(string filePath, PromptRecord record)
    {
        var records = Load(filePath);
        records.Add(record);
        Save(filePath, records);
    }

    /// <summary>
    /// Overwrites the JSON file at <paramref name="filePath"/> with exactly
    /// <paramref name="records"/>.
    /// </summary>
    public void Save(string filePath, IReadOnlyList<PromptRecord> records)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(records, SerializerOptions);
        File.WriteAllText(filePath, json);
    }
}
