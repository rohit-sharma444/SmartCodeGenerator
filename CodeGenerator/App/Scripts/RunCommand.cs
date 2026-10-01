using System.Text.RegularExpressions;

namespace CodeGenerator.App.Scripts;

/// <summary>A script ready to run, or why it is not.</summary>
internal sealed record ScriptLoad(IReadOnlyList<ScriptStep>? Steps, string Name, string? Problem);

/// <summary>
/// <c>run &lt;file&gt;</c>: replays a script, all or nothing.
/// </summary>
internal static partial class RunCommand
{
    // Anchored on the whole word: "running total in Order" is a sentence for the pipeline.
    [GeneratedRegex(@"^\s*run(?:\s+(?<file>.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex Shape();

    public static bool IsRun(string? input) => Shape().IsMatch(input ?? string.Empty);

    /// <summary>The file named after <c>run</c>, trimmed, or empty.</summary>
    public static string FileArgument(string input) => Shape().Match(input).Groups["file"].Value.Trim();

    /// <summary>Reads and checks the named script; any reason it cannot run is in
    /// <see cref="ScriptLoad.Problem"/>, before a line of it has run.</summary>
    public static ScriptLoad Load(string input, string workspaceRoot)
    {
        var argument = FileArgument(input);
        if (argument.Length == 0)
            return new ScriptLoad(null, string.Empty, "Name the script to run, for example: run library.cgs");

        var path = ScriptPath.Resolve(argument, workspaceRoot);
        var name = ScriptPath.Shown(path, workspaceRoot);

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Says what `run` is for: a sentence like "run the app" lands here too, since the word
            // now belongs to scripts.
            return new ScriptLoad(null, name,
                $"No script at {name}. `run` replays a .cgs script made with save session or by hand; see help run. Nothing was run.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ScriptLoad(null, name, $"Could not read {name}: {ex.Message}. Nothing was run.");
        }

        var parse = ScriptFile.Parse(text);
        if (parse.Refusal is not null)
            return new ScriptLoad(null, name, parse.Refusal);
        if (parse.Steps.Count == 0)
            return new ScriptLoad(null, name, $"{name} has no commands. Nothing was run.");

        return new ScriptLoad(parse.Steps, name, null);
    }
}
