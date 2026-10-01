using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Undo;

namespace CodeGenerator.App.Scripts;

/// <summary>How a run ended, and the lines it ran when it worked.</summary>
internal sealed record ScriptOutcome(bool Succeeded, IReadOnlyList<string> Ran, string Message);

/// <summary>
/// Runs a script's steps under one snapshot: all of them, or — at the first failure — none.
/// </summary>
/// <remarks>
/// The snapshot is <c>batch</c>'s: files and the project registry. Validation rules live only in
/// memory and the snapshot does not hold them, so <see cref="SessionState"/> is captured beside it
/// and put back with it. How one step runs is the caller's: the console prints, the chat page
/// makes a turn of it.
/// </remarks>
internal static class ScriptRunner
{
    public static ScriptOutcome Run(
        string name,
        IReadOnlyList<ScriptStep> steps,
        string workspaceRoot,
        Func<ScriptStep, bool> execute,
        Action<string> say,
        Func<bool> confirmSnapshot)
    {
        if (!confirmSnapshot())
            return new ScriptOutcome(false, [], "Nothing was run.");

        WorkspaceSnapshot snapshot;
        try
        {
            snapshot = WorkspaceSnapshot.Capture(workspaceRoot);
        }
        catch (Exception ex)
        {
            return new ScriptOutcome(false, [], $"Could not snapshot the workspace: {ex.Message}. Nothing was run.");
        }

        var state = SessionState.Capture();
        var ran = new List<string>();

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            say($"[{i + 1}/{steps.Count}] {Text(step)}");

            var reason = string.Empty;
            bool ok;
            try
            {
                ok = execute(step);
            }
            catch (Exception ex)
            {
                ok = false;
                reason = $": {ex.GetType().Name}: {ex.Message}";
            }

            if (ok)
            {
                ran.AddRange(step is BuildAppStep build ? build.Lines : [Text(step)]);
                continue;
            }

            try
            {
                snapshot.Restore();
            }
            catch (Exception ex)
            {
                return new ScriptOutcome(false, [],
                    $"Line {step.Line} failed{reason}. Putting it back failed too: {ex.Message}. "
                    + $"The workspace is in an unknown state; the copy taken before the run is at {snapshot.SnapshotPath}.");
            }

            state.Restore(SessionState.Capture());
            snapshot.Dispose();

            return new ScriptOutcome(false, [], $"Line {step.Line} failed{reason}. Everything the script did was put back.");
        }

        snapshot.Dispose();

        return new ScriptOutcome(true, ran,
            $"Ran {steps.Count} command{(steps.Count == 1 ? "" : "s")} from {name}.");
    }

    /// <summary>The line a step is shown as: its command, or a build's first line.</summary>
    public static string Text(ScriptStep step) => step switch
    {
        CommandStep command => command.Text,
        BuildAppStep build => build.Lines[0],
        _ => throw new ArgumentOutOfRangeException(nameof(step))
    };
}
