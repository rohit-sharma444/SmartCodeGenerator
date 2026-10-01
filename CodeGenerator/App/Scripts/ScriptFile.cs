namespace CodeGenerator.App.Scripts;

/// <summary>One thing a script does, and the line it starts on.</summary>
internal abstract record ScriptStep(int Line);

/// <summary>A command, run as a typed one would be.</summary>
internal sealed record CommandStep(int Line, string Text) : ScriptStep(Line);

/// <summary>
/// A <c>build app</c> line and the answers after it, up to and including <c>approve …</c> or
/// <c>cancel</c>. Run in a separate copy of the tool with these lines piped in.
/// </summary>
internal sealed record BuildAppStep(int Line, IReadOnlyList<string> Lines) : ScriptStep(Line);

/// <summary>The steps of a script, or why none of it may run.</summary>
internal sealed record ScriptParse(IReadOnlyList<ScriptStep> Steps, string? Refusal);

/// <summary>
/// Reads a <c>.cgs</c> script: one command per line, blank lines and <c>#</c> lines ignored.
/// </summary>
/// <remarks>
/// The whole file is checked before anything runs, so a refusal on line 9 never leaves lines 1-8
/// done. Kinds are decided by <see cref="CommandRoute"/>, the classifier the console uses, so a
/// script and a typed line can never disagree about what a line is.
/// </remarks>
internal static class ScriptFile
{
    public const string Extension = ".cgs";

    // Only these have an executor. Everything else either needs someone at the keyboard (undo,
    // batch, demo, chat, show relationships), would make a run depend on more than its own lines
    // (run, save session), ends the session (exit), or changes nothing (help, status, history,
    // clear, ai).
    private static readonly CommandKind[] Runnable = [CommandKind.Pipeline, CommandKind.BuildApp];

    public static ScriptParse Parse(string text)
    {
        var lines = text.TrimStart('﻿').Replace("\r\n", "\n").Split('\n');
        var steps = new List<ScriptStep>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (IsSkipped(line))
                continue;

            var number = i + 1;
            var kind = CommandRoute.Classify(line);

            if (!Runnable.Contains(kind))
                return Refused($"Line {number}: '{line}' can't be used in a script. Nothing was run.");

            if (kind == CommandKind.Pipeline)
            {
                steps.Add(new CommandStep(number, line));
                continue;
            }

            var block = new List<string> { line };
            var closed = false;

            for (i++; i < lines.Length; i++)
            {
                var answer = lines[i].Trim();
                if (IsSkipped(answer))
                    continue;

                block.Add(answer);
                if (EndsBuild(answer))
                {
                    closed = true;
                    break;
                }
            }

            if (!closed)
                return Refused($"Line {number}: 'build app' needs an 'approve …' or 'cancel' line after it. Nothing was run.");

            steps.Add(new BuildAppStep(number, block));
        }

        return new ScriptParse(steps, null);
    }

    /// <summary>Whether a review-loop answer ends the build: <c>approve</c> with or without a
    /// target, or <c>cancel</c>.</summary>
    public static bool EndsBuild(string line) =>
        line.Equals("cancel", StringComparison.OrdinalIgnoreCase)
        || line.Equals("approve", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("approve ", StringComparison.OrdinalIgnoreCase);

    private static bool IsSkipped(string line) => line.Length == 0 || line.StartsWith('#');

    private static ScriptParse Refused(string message) => new([], message);
}
