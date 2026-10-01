using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.App;

/// <summary>
/// The <c>batch</c> command: reads a block of sentences, shows the commands
/// detected in them, executes only those the user approves, and rolls the
/// workspace back if any approved command fails.
/// </summary>
internal sealed class BatchCommand
{
    private const string Keyword = "batch";

    /// <summary>Whether <paramref name="input"/> opens a batch.</summary>
    internal static bool IsBatch(string input)
        => input.Trim().Equals(Keyword, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs one batch from start to finish. Returns the sentences it applied, or none when it
    /// applied nothing — what <c>save session</c> keeps for it.
    /// </summary>
    /// <param name="currentProject">
    /// Threaded by reference so that <c>use project Foo</c> on one line is
    /// visible to <c>create class Bar</c> on the next, and so rollback can put
    /// it back.
    /// </param>
    /// <param name="configuredRules">Property-naming rules, passed through unchanged to the pipeline.</param>
    /// <param name="workspaceRoot">The workspace directory the batch reads from and writes to.</param>
    /// <param name="setLastCreatedProject">Callback the pipeline uses to record a newly created project.</param>
    /// <param name="pipeline">Re-parses each approved sentence at execution time; never the stored preview.</param>
    internal IReadOnlyList<string> Handle(
        ref ProjectMetaData? currentProject,
        List<PropertyRule> configuredRules,
        string workspaceRoot,
        Action<ProjectMetaData?> setLastCreatedProject,
        NaturalLanguagePipeline pipeline)
    {
        var lines = ReadBlock();
        var detected = BatchCommandDetector.Detect(lines, workspaceRoot);

        if (detected.Count == 0)
        {
            Console.WriteLine("Nothing to do.");
            return [];
        }

        PrintDetected(detected);

        var actionable = detected.Where(a => a.IsActionable).ToList();
        if (actionable.Count == 0)
        {
            Console.WriteLine("No actionable commands — nothing was executed.");
            return [];
        }

        var approved = CollectApprovals(actionable);
        if (approved is null)
        {
            Console.WriteLine("Cancelled — nothing was executed.");
            return [];
        }

        if (approved.Count == 0)
        {
            Console.WriteLine("Nothing approved — no snapshot taken, nothing was executed.");
            return [];
        }

        if (!ConfirmSnapshotSize(workspaceRoot))
        {
            Console.WriteLine("Aborted — nothing was executed.");
            return [];
        }

        WorkspaceSnapshot snapshot;
        try
        {
            snapshot = WorkspaceSnapshot.Capture(workspaceRoot);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not snapshot the workspace: {ex.Message}");
            Console.WriteLine("Aborted — nothing was executed.");
            return [];
        }

        return Execute(snapshot, detected, actionable.Count, approved,
            ref currentProject, configuredRules, workspaceRoot, setLastCreatedProject, pipeline);
    }

    private static List<string> ReadBlock()
    {
        Console.WriteLine("Enter one command per line. A blank line ends the batch.");

        var lines = new List<string>();

        while (true)
        {
            Console.Write("... ");
            var line = Console.ReadLine();

            // null is EOF / closed stdin: end the block rather than busy-loop.
            if (line is null || line.Trim().Length == 0)
                break;

            lines.Add(line);
        }

        return lines;
    }

    private static void PrintDetected(IReadOnlyList<DetectedAction> detected)
    {
        var actionableCount = detected.Count(a => a.IsActionable);

        Console.WriteLine();
        Console.WriteLine($"Detected {detected.Count} line(s), {actionableCount} actionable:");
        Console.WriteLine();

        int number = 0;
        foreach (var action in detected)
        {
            if (action.IsActionable)
                Console.WriteLine($"  [{++number}] {ActionFormatter.Format(action)}");
            else
                Console.WriteLine($"   -  {ActionFormatter.Format(action)}");
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Walks the approval prompts. Returns null when the user cancels the batch.
    /// </summary>
    private static List<DetectedAction>? CollectApprovals(IReadOnlyList<DetectedAction> actionable)
    {
        var approved = new List<DetectedAction>();

        for (int i = 0; i < actionable.Count; i++)
        {
            while (true)
            {
                Console.Write($"  [{i + 1}] {ActionFormatter.Format(actionable[i])}   approve? (y/n) ");

                var reply = ApprovalReplyParser.Parse(Console.ReadLine());

                if (reply == ApprovalReply.Cancel)
                    return null;

                if (reply == ApprovalReply.Reprompt)
                    continue;

                if (reply == ApprovalReply.Approve)
                    approved.Add(actionable[i]);

                break;
            }
        }

        return approved;
    }

    internal static bool ConfirmSnapshotSize(string workspaceRoot) =>
        ConfirmSnapshotSize(WorkspaceSnapshot.Measure(workspaceRoot), Console.IsInputRedirected, Console.In);

    /// <remarks>
    /// With input piped nothing is asked, and the answer is the prompt's own default, abort — as the
    /// chat page, which cannot ask either, refuses. Whether the question comes up depends on how
    /// many files the workspace holds that day, so no script can know to answer it: reading one
    /// would swallow its next command as the answer.
    /// </remarks>
    internal static bool ConfirmSnapshotSize(SnapshotSizing sizing, bool inputRedirected, TextReader input)
    {
        if (sizing.FileCount <= WorkspaceSnapshot.MaxFiles && sizing.Bytes <= WorkspaceSnapshot.MaxBytes)
            return true;

        var megabytes = sizing.Bytes / (1024.0 * 1024.0);

        Console.WriteLine($"Workspace is {sizing.FileCount:N0} files ({megabytes:N0} MB) after exclusions.");

        if (inputRedirected)
        {
            Console.WriteLine("Too large to snapshot without asking, and input is piped, so there is no one to ask.");
            Console.WriteLine("Run it from an interactive console, which asks first.");
            return false;
        }

        Console.WriteLine("Snapshotting may take a while.");
        Console.WriteLine("  [p]roceed with snapshot   [a]bort   (Enter = abort)");
        Console.Write("> ");

        var reply = input.ReadLine();

        // Anything but an explicit "p" aborts, including Enter and EOF.
        return reply is not null
            && reply.Trim().Equals("p", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> Execute(
        WorkspaceSnapshot snapshot,
        IReadOnlyList<DetectedAction> detected,
        int actionableCount,
        List<DetectedAction> approved,
        ref ProjectMetaData? currentProject,
        List<PropertyRule> configuredRules,
        string workspaceRoot,
        Action<ProjectMetaData?> setLastCreatedProject,
        NaturalLanguagePipeline pipeline)
    {
        int unactionable = detected.Count - actionableCount;
        int rejected = actionableCount - approved.Count;

        Console.WriteLine($"Executing {approved.Count} approved action(s)...");

        int succeeded = 0;
        DetectedAction? failedAction = null;
        string failureReason = string.Empty;

        foreach (var action in approved)
        {
            try
            {
                var outcome = pipeline.Handle(
                    action.Sentence, ref currentProject, configuredRules,
                    workspaceRoot, setLastCreatedProject);

                if (!outcome.Handled)
                {
                    failedAction = action;
                    failureReason = "command was not handled";
                    break;
                }

                succeeded++;
            }
            catch (Exception ex)
            {
                failedAction = action;
                failureReason = $"{ex.GetType().Name}: {ex.Message}";
                break;
            }
        }

        if (failedAction is null)
        {
            snapshot.Dispose();

            Console.WriteLine();
            Console.WriteLine($"Done. {succeeded} applied, 0 failed, 0 rolled back, " +
                              $"0 not attempted, {rejected} rejected, {unactionable} unactionable.");
            return [.. approved.Select(a => a.Sentence)];
        }

        Console.WriteLine($"  FAILED: {failureReason}");
        Console.WriteLine();
        Console.WriteLine("Rolling back...");

        try
        {
            snapshot.Restore();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ROLLBACK FAILED: {ex.Message}");
            Console.WriteLine("  The workspace is in an unknown state.");
            Console.WriteLine($"  Your pre-batch snapshot is preserved at: {snapshot.SnapshotPath}");
            return [];
        }

        // Restore reset ProjectManager.CurrentProject; keep the caller's copy in step.
        currentProject = ProjectManager.CurrentProject;

        Console.WriteLine("  workspace restored to pre-batch state");
        snapshot.Dispose();

        int notAttempted = approved.Count - succeeded - 1;

        Console.WriteLine();
        Console.WriteLine($"Aborted. 0 applied, 1 failed, {succeeded} rolled back, " +
                          $"{notAttempted} not attempted, {rejected} rejected, {unactionable} unactionable.");
        return [];
    }
}
