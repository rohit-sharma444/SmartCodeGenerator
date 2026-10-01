namespace CodeGenerator.App.Scripts;

/// <summary>Where a script named on the command line lives, and how to name it back.</summary>
internal static class ScriptPath
{
    /// <summary>Relative to the workspace unless absolute; quotes stripped; <c>.cgs</c> added when
    /// there is no extension.</summary>
    public static string Resolve(string argument, string workspaceRoot)
    {
        var name = argument.Trim();
        if (name.Length >= 2 && name[0] == '"' && name[^1] == '"')
            name = name[1..^1];

        if (Path.GetExtension(name).Length == 0)
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
