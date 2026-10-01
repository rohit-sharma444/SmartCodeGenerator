using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;
using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageBridge;

/// <summary>Result of orchestrating one command: whether CodeGenerator handled
/// it, the NLE analysis produced for it (null if NLE failed), and — distinct from
/// Handled — whether the input was recognized as a command at all. A recognized
/// command can still fail (Handled=false, Recognized=true); only genuinely
/// unrecognized input is Recognized=false.</summary>
public sealed record PipelineOutcome(bool Handled, SemanticResult? Analysis, bool Recognized, bool NeededAi = false);

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
        Action<SemanticResult?>? onAnalyzed = null,
        IIntentResolver? aiResolver = null,
        Func<AiResolution, bool>? confirmAi = null,
        CancellationToken ct = default)
    {
        SemanticResult? captured = null;
        var result = CommandFront.RunDetailed(
            input, ref project, configuredRules, workspaceRoot, setLastCreatedProject,
            a => { captured = a; onAnalyzed?.Invoke(a); },
            aiResolver, confirmAi, ct);

        return new PipelineOutcome(result.Handled, captured, result.Recognized, result.NeededAi);
    }
}
