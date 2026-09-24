using System.Text.Json;

namespace CodeGenerator.Web.Workflow;

/// <summary>
/// Whether the workflow page was last switched to dark, in <c>.codegen/workflow-theme.json</c>.
/// </summary>
/// <remarks>
/// Kept in the workspace rather than in the browser: the host listens on a new port every run,
/// and browser storage belongs to an origin, so a choice kept there would be forgotten each time.
/// <para>
/// Never throws, in either direction — the rule <c>BuildSnapshotStore</c> and
/// <c>CustomizationDefaults</c> already follow. A broken or unwritable file means the page opens
/// light, never that it fails to open.
/// </para>
/// </remarks>
internal static class WorkflowThemeStore
{
    public const string FileName = "workflow-theme.json";
    private const string FolderName = ".codegen";

    public static string PathFor(string workspaceRoot)
        => Path.Combine(workspaceRoot, FolderName, FileName);

    public static bool IsDark(string workspaceRoot)
    {
        try
        {
            var path = PathFor(workspaceRoot);

            if (!File.Exists(path))
                return false;

            return JsonSerializer.Deserialize<Choice>(File.ReadAllText(path))?.Dark ?? false;
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or JsonException
                                      or NotSupportedException)
        {
            return false;
        }
    }

    public static void Save(string workspaceRoot, bool dark)
    {
        try
        {
            var path = PathFor(workspaceRoot);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new Choice { Dark = dark }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The switch still works for this run; it is only not remembered.
        }
    }

    private sealed class Choice
    {
        public bool Dark { get; set; }
    }
}
