using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;
using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageBridge;

/// <summary>Result of orchestrating one command: whether CodeGenerator handled
/// it, and the NLE analysis produced for it (null if NLE failed).</summary>
public sealed record PipelineOutcome(bool Handled, SemanticResult? Analysis);

/// <summary>
/// NLE-led entry point for a single command: analyze with NLE once, surface the
/// analysis, then delegate parsing + code generation to CodeGenerator with the
/// extracted action verb.
/// </summary>
public sealed class NaturalLanguagePipeline
{
    public PipelineOutcome Handle(
        string input,
        ref ProjectMetaData? project,
        List<PropertyRule> configuredRules,
        string workspaceRoot,
        Action<ProjectMetaData?> setLastCreatedProject,
        Action<SemanticResult?>? onAnalyzed = null)
    {
        var analysis = NlpActionProvider.Analyze(input);
        onAnalyzed?.Invoke(analysis);

        var nlpAction = NlpActionProvider.GetPrimaryAction(analysis);

        bool handled = CommandHandlingService.ProcessInput(
            input,
            ref project,
            configuredRules,
            workspaceRoot,
            setLastCreatedProject,
            nlpAction);

        return new PipelineOutcome(handled, analysis);
    }
}
