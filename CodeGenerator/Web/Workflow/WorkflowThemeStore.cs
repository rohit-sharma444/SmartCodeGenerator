using System.Text.Json;

namespace CodeGenerator.Web.Workflow;

/// <summary>One theme the workflow page can be drawn in.</summary>
/// <param name="Key">What goes in <c>data-theme</c> and in the file. Never renamed once
/// shipped: a saved choice with the old key would quietly open light.</param>
/// <param name="Label">What the drop-down shows.</param>
public sealed record WorkflowTheme(string Key, string Label);

/// <summary>The themes the workflow page's drop-down offers, in the order it offers them.</summary>
/// <remarks>
/// Each key other than <see cref="Default"/> needs a block of its own in <c>workflow.css</c>,
/// keyed on <c>:root:has(.workflow[data-theme="…"])</c>. A test checks that every key here has
/// one, because a theme listed without its colours would be offered and repaint nothing.
/// </remarks>
public static class WorkflowThemes
{
    public const string Default = "light";

    public static IReadOnlyList<WorkflowTheme> All { get; } =
    [
        new(Default, "Light"),
        new("dark", "Dark"),
        new("contrast", "High contrast"),
        new("warm", "Warm"),
    ];

    /// <summary><paramref name="key"/> if it names a theme, otherwise <see cref="Default"/>.</summary>
    public static string Known(string? key)
        => All.Any(t => t.Key == key) ? key! : Default;
}

/// <summary>
/// Which theme the workflow page was last set to, in <c>.codegen/workflow-theme.json</c>.
/// </summary>
/// <remarks>
/// Kept in the workspace rather than in the browser: the host listens on a new port every run,
/// and browser storage belongs to an origin, so a choice kept there would be forgotten each time.
/// <para>
/// Never throws, in either direction — the rule <c>BuildSnapshotStore</c> and
/// <c>CustomizationDefaults</c> already follow. A broken, unknown or unwritable file means the
/// page opens light, never that it fails to open.
/// </para>
/// </remarks>
internal static class WorkflowThemeStore
{
    public const string FileName = "workflow-theme.json";
    private const string FolderName = ".codegen";

    public static string PathFor(string workspaceRoot)
        => Path.Combine(workspaceRoot, FolderName, FileName);

    public static string Load(string workspaceRoot)
    {
        try
        {
            var path = PathFor(workspaceRoot);

            if (!File.Exists(path))
                return WorkflowThemes.Default;

            return WorkflowThemes.Known(
                JsonSerializer.Deserialize<Choice>(File.ReadAllText(path))?.Theme);
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or JsonException
                                      or NotSupportedException)
        {
            return WorkflowThemes.Default;
        }
    }

    public static void Save(string workspaceRoot, string theme)
    {
        try
        {
            var path = PathFor(workspaceRoot);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new Choice { Theme = theme }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The drop-down still works for this run; it is only not remembered.
        }
    }

    private sealed class Choice
    {
        public string? Theme { get; set; }
    }
}
