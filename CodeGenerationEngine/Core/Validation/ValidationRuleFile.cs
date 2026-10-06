using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeGenerationEngine.Core.Validation;

/// <summary>One saved rule: the class it belongs to, its category, and the rule as it was typed.</summary>
public sealed record SavedValidationRule(
    [property: JsonPropertyName("class")] string ClassName,
    [property: JsonPropertyName("category")] ValidationCategory Category,
    [property: JsonPropertyName("text")] string Text);

/// <summary>What reading the file found.</summary>
/// <param name="Rules">The rules that parsed, in file order, each with the entry it came from.</param>
/// <param name="Unreadable">Entries no parser accepts any more. Kept so the next save writes them
/// back instead of losing them.</param>
/// <param name="Problem">Why the file could not be read at all, or null.</param>
public sealed record ValidationRuleFileLoad(
    IReadOnlyList<(SavedValidationRule Entry, IValidationRule Rule)> Rules,
    IReadOnlyList<SavedValidationRule> Unreadable,
    string? Problem);

/// <summary>
/// The workspace's validation rules on disk: <c>.codegen/validation-rules.json</c> under the
/// workspace root.
/// </summary>
/// <remarks>
/// The rules were held only in memory, so a rule added in one session was gone in the next, and
/// the next <c>generate validator</c> rewrote the class's validator with only the rules added since
/// — the earlier ones silently lost.
/// <para>
/// A rule is saved as the text it was parsed from and parsed again on load, rather than as the
/// parsed object: the rule classes hold their state privately, and the text is what the user typed
/// and can read. It lives under <c>.codegen/</c> with the tool's other per-workspace state
/// (<c>workflow-theme.json</c>, <c>learned.json</c>); the .NET SDK leaves dot-folders out of every
/// default item glob, so it is never compiled or published.
/// </para>
/// <para>
/// Not the same thing as <c>rules.json</c> / <c>rules.txt</c> at the workspace root: those are
/// property rules the user writes by hand, read when a class is generated, and never written by the
/// tool. This file is written by the tool on every change to the add-validation rules.
/// </para>
/// </remarks>
public static class ValidationRuleFile
{
    public const string FolderName = ".codegen";

    public const string FileName = "validation-rules.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string PathFor(string workspaceRoot) =>
        Path.Combine(workspaceRoot, FolderName, FileName);

    public static ValidationRuleFileLoad Read(string path, ValidationRuleCatalog? catalog = null)
    {
        catalog ??= ValidationRuleCatalog.Default;

        if (!File.Exists(path))
            return new([], [], null);

        Document? document;
        try
        {
            document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new([], [], ex.Message);
        }

        var rules = new List<(SavedValidationRule, IValidationRule)>();
        var unreadable = new List<SavedValidationRule>();

        foreach (var entry in document?.Rules ?? [])
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.ClassName) || string.IsNullOrWhiteSpace(entry.Text))
                continue;

            var ctx = new ValidationParseContext { Fragment = entry.Text, ClassName = entry.ClassName };
            var parsed = catalog.ParseOne(entry.Text, entry.Category, ctx);

            if (parsed.Count == 0)
                unreadable.Add(entry);
            else
                rules.AddRange(parsed.Select(rule => (entry, rule)));
        }

        return new(rules, unreadable, null);
    }

    /// <summary>
    /// Writes <paramref name="rules"/>, or deletes the file when there are none. Returns why it
    /// could not, or null.
    /// </summary>
    public static string? Write(string path, IReadOnlyList<SavedValidationRule> rules)
    {
        try
        {
            if (rules.Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Written beside and moved over, so a session killed mid-write leaves the old file
            // rather than half of a new one.
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Document { Rules = [.. rules] }, Options));
            File.Move(temporary, path, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    private sealed class Document
    {
        [JsonPropertyName("rules")]
        public List<SavedValidationRule?> Rules { get; set; } = [];
    }
}
