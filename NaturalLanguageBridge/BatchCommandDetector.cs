using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;

namespace NaturalLanguageBridge;

/// <summary>
/// Turns the lines of a batch into structured actions without executing any of
/// them. Reconstructs the chain the command handling service runs
/// privately, from the three stages it composes — all of which are public.
/// </summary>
public static class BatchCommandDetector
{
    /// <summary>
    /// Detects the command, if any, on each non-blank line. Input order is
    /// preserved. Never throws: a line that fails to parse comes back
    /// unactionable, carrying the reason in <see cref="DetectedAction.Note"/>.
    /// </summary>
    public static IReadOnlyList<DetectedAction> Detect(
        IEnumerable<string> lines, string workspaceRoot)
    {
        // Parsing writes to two process-wide singletons: LastEntity on
        // ContextManager.Current, and SessionContext.CurrentClass. Previewing
        // must leave the process exactly as it found it, or execution re-parses
        // line 1 against line N's entity -- and a rejected `create class Foo`
        // still leaves Foo as the implicit target of every later command.
        var saved = ParsingStateSnapshot.Capture();

        try
        {
            var actions = new List<DetectedAction>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                actions.Add(DetectOne(line.Trim(), workspaceRoot));
            }

            return actions;
        }
        finally
        {
            saved.Restore();
        }
    }

    private static DetectedAction DetectOne(string sentence, string workspaceRoot)
    {
        try
        {
            var command = CommandFront.Parse(sentence, workspaceRoot);

            if (command.Intent == IntentType.Unknown)
                return new DetectedAction(sentence, IntentType.Unknown, command.Confidence, null, "not understood");

            // Recognizing an intent is not the same as being able to run it.
            if (!CommandHandlingService.IsSupported(command.Intent))
                return new DetectedAction(
                    sentence, command.Intent, command.Confidence, null,
                    $"{command.Intent} is not supported yet");

            return new DetectedAction(sentence, command.Intent, command.Confidence, command, null);
        }
        catch (Exception ex)
        {
            return new DetectedAction(sentence, IntentType.Unknown, 0, null, $"parse failed: {ex.Message}");
        }
    }
}
