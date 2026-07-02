using NaturalLanguageEngine.Core.Diagnostics;
using NaturalLanguageEngine.Models.Diagnostics;

namespace NaturalLanguageEngine.Core;

public sealed class Pipeline
{
    private readonly List<PipelineStageBase> _stages = new();

    public Pipeline Add(PipelineStageBase stage)
    {
        _stages.Add(stage);
        _stages.Sort((a, b) => a.Order.CompareTo(b.Order));

        return this;
    }

    /// <summary>
    /// Runs every stage in order. If a stage throws, the exception is
    /// captured as a <see cref="Diagnostic"/> on the context instead of
    /// propagating, and the pipeline stops (later stages generally assume
    /// the context left behind by earlier ones is well-formed, so
    /// continuing after a failure is more likely to produce misleading
    /// results than useful ones). Whatever was successfully computed
    /// before the failure - tokens, entities, etc. - is preserved and
    /// returned along with the diagnostic.
    /// </summary>
    public AnalysisContext Execute(string text)
    {
        var context = new AnalysisContext(text);

        foreach (var stage in _stages)
        {
            try
            {
                context = stage.Execute(context)
                    ?? throw new InvalidOperationException(
                        $"Stage '{stage.Name}' returned a null context.");
            }
            catch (Exception ex)
            {
                context.AddDiagnostic(
                    code: $"NLE.Stage.{stage.Name}",
                    severity: DiagnosticSeverity.Critical,
                    message: $"Stage '{stage.Name}' failed and analysis was stopped: {ex.Message}");

                break;
            }
        }

        return context;
    }
}