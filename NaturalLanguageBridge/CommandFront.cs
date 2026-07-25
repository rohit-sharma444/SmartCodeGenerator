using System.Text.RegularExpressions;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;
using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageBridge;

/// <summary>
/// The single place user text becomes a <see cref="ParsedCommand"/>. Order:
/// literal meta-commands (exact table) → residual pattern guards that the
/// literal table can't express → the NLE-driven intent mapper → prose field
/// extraction. CodeGenerationEngine receives the result and never re-parses text.
/// Stateless.
/// </summary>
public static class CommandFront
{
    // Plan 1 found these two guards un-absorbable by LiteralCommandTable:
    // "classes in <project>" (NLE drops the trailing "in"), and the bare
    // \bvalidator\b substring. Both read the raw sentence here, ahead of the mapper.
    private static readonly Regex ClassesInProject =
        new(@"^(?:list|show|display)\s+classes\s+in\s+(?<project>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Validator =
        new(@"\bvalidator\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "add validation <args>" and "show/list/view validation <args>" — the
    // argument-bearing forms LiteralCommandTable's exact match can't cover, and
    // the mapper has no AddValidation/ShowValidation entries for. Without these,
    // e.g. "add validation Email should not be blank on Email in Customer" fell
    // through to Unknown.
    private static readonly Regex AddValidationRe =
        new(@"\badd\s+validation\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ShowValidationRe =
        new(@"\b(?:show|list|view)\s+validation\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AddBusinessValidationRe =
        new(@"\badd\s+business\s+validation\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ShowBusinessValidationRe =
        new(@"\b(?:show|list|view)\s+business\s+validation\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "use project <name>" and its synonyms. The old ProjectManager.UseProject
    // sliced the sentence itself; the bridge now captures the name so the handler
    // reads a field. Kept ahead of the mapper because the mapper's UseProject rule
    // only covers the bare "use project" form (no name).
    private static readonly Regex UseProjectRe =
        new(@"^(?:use|select|switch|open|activate)\s+project\s+(?<project>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ParsedCommand Parse(string input, string workspace)
    {
        // Correct near-miss spellings of the distinctive validation keywords up
        // front (e.g. "busniess" -> "business") so the guards below and the
        // extractor both see canonical text. Only those keywords are touched;
        // entity names and rule text pass through unchanged.
        var text = KeywordCorrector.Correct((input ?? string.Empty).Trim());

        if (LiteralCommandTable.TryResolve(text, out var literalIntent))
            return new ParsedCommand { Intent = literalIntent, OriginalSentence = input ?? string.Empty, Confidence = 1.0 };

        var classesIn = ClassesInProject.Match(text);
        if (classesIn.Success)
            return new ParsedCommand
            {
                Intent = IntentType.ListClassesInProject,
                ProjectName = classesIn.Groups["project"].Value.Trim(),
                OriginalSentence = input ?? string.Empty,
                Confidence = 1.0
            };

        var useMatch = UseProjectRe.Match(text);
        if (useMatch.Success)
            return new ParsedCommand
            {
                Intent = IntentType.UseProject,
                ProjectName = useMatch.Groups["project"].Value.Trim(),
                OriginalSentence = input ?? string.Empty,
                Confidence = 1.0
            };

        if (AddBusinessValidationRe.IsMatch(text))
        {
            var cmd = CommandEntityExtractor.Extract(text, IntentType.AddBusinessValidation, workspace);
            cmd.Confidence = 1.0;
            return cmd;
        }

        if (ShowBusinessValidationRe.IsMatch(text))
        {
            var cmd = CommandEntityExtractor.Extract(text, IntentType.ShowBusinessValidation, workspace);
            cmd.Confidence = 1.0;
            return cmd;
        }

        if (AddValidationRe.IsMatch(text))
        {
            var addValidationCmd = CommandEntityExtractor.Extract(text, IntentType.AddValidation, workspace);
            addValidationCmd.Confidence = 1.0;
            return addValidationCmd;
        }

        if (ShowValidationRe.IsMatch(text))
        {
            var showValidationCmd = CommandEntityExtractor.Extract(text, IntentType.ShowValidation, workspace);
            showValidationCmd.Confidence = 1.0;
            return showValidationCmd;
        }

        if (Validator.IsMatch(text))
        {
            var validatorCmd = CommandEntityExtractor.Extract(text, IntentType.GenerateValidator, workspace);
            // The extractor's switch has no GenerateValidator case, so fill the
            // class name from the sentence here — exactly what HandleGenerateValidator
            // used to do with the raw input, now done in the bridge.
            CommandEntityExtractor.ExtractClassName(text, validatorCmd);
            validatorCmd.Confidence = 1.0;
            return validatorCmd;
        }

        SemanticResult? analysis = NlpActionProvider.Analyze(input ?? string.Empty);
        var intent = NlpCommandMapper.ResolveIntent(analysis);
        var command = CommandEntityExtractor.Extract(input ?? string.Empty, intent, workspace);
        command.Confidence = analysis?.Confidence ?? 0d;
        return command;
    }

    /// <summary>Parse then execute one command. The demo shortcut and the NLE
    /// analysis callback (for display) live here so callers need only one entry.</summary>
    public static bool Run(
        string input,
        ref ProjectMetaData? project,
        List<PropertyRule> configuredRules,
        string workspace,
        Action<ProjectMetaData?> setLastCreatedProject,
        Action<SemanticResult?>? onAnalyzed = null)
    {
        if ((input ?? string.Empty).Trim().Equals("demo", StringComparison.OrdinalIgnoreCase))
        {
            var (demoCode, _) = CommandHandlingService.BuildDemo();
            Console.WriteLine(demoCode.Replace("//", string.Empty));
            onAnalyzed?.Invoke(null);
            return true;
        }

        if (onAnalyzed is not null)
            onAnalyzed(NlpActionProvider.Analyze(input ?? string.Empty));

        var command = Parse(input ?? string.Empty, workspace);
        return CommandHandlingService.Execute(command, ref project, configuredRules, workspace, setLastCreatedProject);
    }
}
