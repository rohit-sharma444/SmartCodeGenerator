using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeGenerator.App.Resume;

/// <summary>
/// The one unfinished build a workspace can be holding, in
/// <c>.codegen/build-in-progress.json</c>.
/// </summary>
/// <remarks>
/// Beside <c>app-profile.json</c> and <c>learned.json</c>, and never throwing in either
/// direction. A broken file must leave the CLI working exactly as it does without one — the rule
/// <see cref="CustomizationDefaults"/> and <c>AiConfig.Load</c> already follow — and the same
/// applies to writing: an unwritable <c>.codegen</c> means no resume, not a dead command.
/// </remarks>
internal static class BuildSnapshotStore
{
    public const string FileName = "build-in-progress.json";
    private const string FolderName = ".codegen";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string PathFor(string workspaceRoot)
        => Path.Combine(workspaceRoot, FolderName, FileName);

    public static BuildSnapshot? Load(string workspaceRoot)
    {
        try
        {
            var path = PathFor(workspaceRoot);

            if (!File.Exists(path))
                return null;

            var snapshot = JsonSerializer.Deserialize<BuildSnapshot>(
                File.ReadAllText(path), Options);

            if (snapshot is null)
                return null;

            // Refused whole, not partially read: half a model is worse than no offer, and someone
            // running two versions of the tool must not have the older one quietly drop what the
            // newer one saved.
            return snapshot.SchemaVersion > BuildSnapshot.CurrentSchemaVersion ? null : snapshot;
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or JsonException
                                      or NotSupportedException)
        {
            return null;
        }
    }

    public static void Save(string workspaceRoot, BuildSnapshot snapshot)
    {
        try
        {
            var path = PathFor(workspaceRoot);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(snapshot, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No resume, rather than no build.
        }
    }

    public static void Clear(string workspaceRoot)
    {
        try
        {
            var path = PathFor(workspaceRoot);

            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A stale snapshot is offered again next time, which is recoverable. Throwing here
            // would fail a build that has already succeeded.
        }
    }
}
