using System.Text;
using System.Text.RegularExpressions;
using CodeGenerationEngine.Core.Undo;

namespace CodeGenerator.App.Scripts;

/// <summary>What a save did, or why it did nothing.</summary>
internal sealed record SaveResult(bool Saved, string Message);

/// <summary>
/// <c>save session &lt;file&gt; [replace]</c>: writes this session's commands as a script.
/// </summary>
internal static partial class SaveSessionCommand
{
    private const string ReplaceWord = "replace";

    [GeneratedRegex(@"^\s*save\s+session(?:\s+(?<rest>.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex Shape();

    public static bool IsSaveSession(string? input) => Shape().IsMatch(input ?? string.Empty);

    /// <summary>
    /// Writes a header and the recorder's lines. Refuses an existing file unless the input ends
    /// in <c>replace</c>: a hand-written recipe is worth more than the session that would overwrite it.
    /// </summary>
    public static SaveResult Handle(string input, string workspaceRoot, SessionRecorder recorder, DateTime now)
    {
        var rest = Shape().Match(input).Groups["rest"].Value.Trim();

        var replace = rest.EndsWith(" " + ReplaceWord, StringComparison.OrdinalIgnoreCase);
        if (replace)
            rest = rest[..^(ReplaceWord.Length + 1)].TrimEnd();

        if (rest.Length == 0)
            return new SaveResult(false, "Name the file to save to, for example: save session library.cgs");

        var lines = recorder.Lines;
        if (lines.Count == 0)
            return new SaveResult(false, "Nothing to save — no command in this session changed anything.");

        var path = ScriptPath.Resolve(rest, workspaceRoot);
        var shown = ScriptPath.Shown(path, workspaceRoot);

        if (File.Exists(path) && !replace)
        {
            return new SaveResult(false,
                $"{shown} already exists. Add 'replace' to overwrite it: save session {rest} replace");
        }

        var text = new List<string> { $"# Saved {now:yyyy-MM-dd HH:mm} from {workspaceRoot}" };
        if (recorder.UndoWasOff)
            text.Add("# Undo was off in this session, so this keeps every command that succeeded.");
        text.AddRange(lines);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SaveResult(false, $"Could not save {shown}: {ex.Message}");
        }

        var notes = lines.Count(l => l.TrimStart().StartsWith('#'));
        var commands = lines.Count - notes;

        return new SaveResult(true, $"Saved {Count(commands, "command")}"
            + (notes > 0 ? $" and {Count(notes, "note")}" : string.Empty) + $" to {shown}.");
    }

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
}
