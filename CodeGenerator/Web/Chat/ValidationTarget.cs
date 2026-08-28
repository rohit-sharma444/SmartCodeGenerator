using System.IO;

namespace CodeGenerator.Web.Chat;

/// <summary>The class a validation page is about, and the file it lives in.</summary>
/// <remarks>
/// The file, not only the name: two projects in one solution can each declare a <c>Customer</c>,
/// which is the same reason the tree keys its open classes by path.
/// </remarks>
public sealed record ValidationTarget(string ClassName, string File);

/// <summary>
/// Turns the page's query string into a target, or says why it will not.
/// </summary>
/// <remarks>
/// The path arrives from a URL. The host binds loopback, which every process on this machine can
/// reach, and the session token is the only thing in front of it - so the path is resolved and
/// confined to the workspace before anything is opened, let alone written.
/// </remarks>
public static class ValidationTargetReader
{
    public static bool TryResolve(
        string? className,
        string? file,
        string workspaceRoot,
        out ValidationTarget? target,
        out string problem)
    {
        target = null;
        problem = string.Empty;

        if (string.IsNullOrWhiteSpace(className))
        {
            problem = "No class was named.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(file))
        {
            problem = $"No file was given for {className}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            problem = "No workspace is loaded.";
            return false;
        }

        string full;
        string root;

        try
        {
            full = Path.GetFullPath(file);
            root = Path.GetFullPath(workspaceRoot);
        }
        catch (System.Exception ex) when (ex is System.ArgumentException or PathTooLongException
                                             or NotSupportedException)
        {
            problem = "That is not a path this tool can open.";
            return false;
        }

        var confined = full.StartsWith(
            root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar,
            System.StringComparison.OrdinalIgnoreCase);

        if (!confined)
        {
            problem = "That file is outside the loaded workspace.";
            return false;
        }

        if (!full.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase))
        {
            problem = "That is not a .cs file.";
            return false;
        }

        if (!File.Exists(full))
        {
            problem = $"{Path.GetFileName(full)} is not there any more.";
            return false;
        }

        target = new ValidationTarget(className, full);
        return true;
    }
}
