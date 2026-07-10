using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

/// <summary>
/// A copy of the mutable fields of a <see cref="SemanticContext"/>, so a caller
/// can put the process-wide context back the way it found it.
/// </summary>
/// <remarks>
/// <see cref="SemanticAnalyzer.Analyze"/> mutates <see cref="ContextManager.Current"/>
/// via <see cref="ContextResolver"/>. Two callers need to undo that: batch
/// detection, which must not let a preview change how the batch then parses, and
/// <see cref="WorkspaceSnapshot"/>, which must restore in-memory state on rollback.
/// The field list lives here once so the two cannot drift apart.
/// </remarks>
public sealed class SemanticContextSnapshot
{
    private readonly string? _currentProject;
    private readonly string? _currentClass;
    private readonly string? _currentNamespace;
    private readonly string? _currentFile;
    private readonly string? _lastEntity;

    private SemanticContextSnapshot(SemanticContext context)
    {
        _currentProject = context.CurrentProject;
        _currentClass = context.CurrentClass;
        _currentNamespace = context.CurrentNamespace;
        _currentFile = context.CurrentFile;
        _lastEntity = context.LastEntity;
    }

    /// <summary>Copies the current values out of <paramref name="context"/>.</summary>
    public static SemanticContextSnapshot Capture(SemanticContext context)
        => new(context);

    /// <summary>Writes the captured values back onto <paramref name="context"/>.</summary>
    public void RestoreTo(SemanticContext context)
    {
        context.CurrentProject = _currentProject;
        context.CurrentClass = _currentClass;
        context.CurrentNamespace = _currentNamespace;
        context.CurrentFile = _currentFile;
        context.LastEntity = _lastEntity;
    }
}
