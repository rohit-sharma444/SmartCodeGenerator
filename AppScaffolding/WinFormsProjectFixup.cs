namespace AppScaffolding;

/// <summary>
/// Reconciles the files `dotnet new winforms` emits with the foldered layout this scaffolder
/// writes. Runs only against a project this scaffolder just created.
/// </summary>
/// <remarks>
/// Unlike the WPF case, the template's form does not break the build — no duplicated x:Class —
/// so this is about not shipping a dead Form1 alongside the real Views/MainForm. Program.cs needs
/// no repointing: ours overwrote the template's, the way ConsoleTarget and WorkerTarget do.
/// </remarks>
public static class WinFormsProjectFixup
{
    // Form1.resx is listed defensively: in case some template version emits one. Deleting a file
    // that was never there is a no-op.
    private static readonly string[] TemplateFiles =
        ["Form1.cs", "Form1.Designer.cs", "Form1.resx"];

    /// <summary>Deletes the template's own form. Best-effort and idempotent.</summary>
    public static void Apply(string projectFolder)
    {
        foreach (var name in TemplateFiles)
            Delete(Path.Combine(projectFolder, name));
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leave it; the build error that follows is clearer than a crash here.
        }
    }
}
