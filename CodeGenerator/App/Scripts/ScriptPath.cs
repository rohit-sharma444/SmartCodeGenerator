namespace CodeGenerator.App.Scripts;

/// <summary>Where a script named on the command line lives, and how to name it back.</summary>
internal static class ScriptPath
{
    // The names a script is kept under as typed. Anything else after a dot — acme.v2 — is part of
    // the name, not an extension.
    private static readonly string[] Kept = [ScriptFile.Extension, ".txt"];

    /// <summary>Relative to the workspace unless absolute; quotes stripped; <c>.cgs</c> added unless
    /// the name already ends in <c>.cgs</c> or <c>.txt</c>.</summary>
    public static string Resolve(string argument, string workspaceRoot)
    {
        var name = argument.Trim();
        if (name.Length >= 2 && name[0] == '"' && name[^1] == '"')
            name = name[1..^1];

        if (!Kept.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            name += ScriptFile.Extension;

        return Path.GetFullPath(Path.Combine(workspaceRoot, name));
    }

    /// <summary>Relative when inside the workspace, in full when not.</summary>
    public static string Shown(string path, string workspaceRoot)
    {
        var relative = Path.GetRelativePath(workspaceRoot, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? path
            : relative;
    }
}
