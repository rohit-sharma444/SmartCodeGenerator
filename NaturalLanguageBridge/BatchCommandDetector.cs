using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace NaturalLanguageBridge;

/// <summary>
/// Turns the lines of a batch into structured actions without executing any of
/// them. Reconstructs the chain <c>CommandHandlingService.ParseInput</c> runs
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
            var nlpAction = NlpActionProvider.GetPrimaryAction(sentence);
            var semantic = SemanticAnalyzer.Analyze(sentence, nlpAction);

            // Fully qualified: CodeGenerationEngine.Models also declares a
            // legacy IntentAnalyzer (see SemanticIntentAnalyzer.cs), so the
            // unqualified name is ambiguous with both usings in scope.
            var match = CodeGenerationEngine.Core.IntentAnalyzer.DetectWithConfidence(semantic);

            if (match.Intent == IntentType.Unknown)
            {
                return new DetectedAction(
                    sentence, IntentType.Unknown, match.Confidence, null, "not understood");
            }

            var preview = EntityExtractor.Extract(semantic, match.Intent, workspaceRoot);
            return new DetectedAction(sentence, match.Intent, match.Confidence, preview, null);
        }
        catch (Exception ex)
        {
            // One malformed line must not destroy the batch.
            return new DetectedAction(
                sentence, IntentType.Unknown, 0, null, $"parse failed: {ex.Message}");
        }
    }
}
