namespace CodeGenerationEngine.Core;

/// <summary>
/// A copy of the process-wide state that parsing a sentence mutates, so a caller
/// can put it back the way it found it.
/// </summary>
/// <remarks>
/// Two singletons are involved, and missing either one reintroduces the bug this
/// type exists to prevent. <see cref="SemanticAnalyzer.Analyze"/> writes
/// <c>LastEntity</c> on <see cref="ContextManager.Current"/> via
/// <see cref="ContextResolver"/>; entity extraction writes
/// <see cref="SessionContext.CurrentClass"/> and reads it back as a fallback for
/// sentences that name no class.
/// <para>
/// Two callers need to undo that: batch detection, which must not let a preview
/// change how anything afterwards parses, and <see cref="WorkspaceSnapshot"/>,
/// which must restore in-memory state on rollback.
/// </para>
/// </remarks>
public sealed class ParsingStateSnapshot
{
    private readonly string? _semanticProject;
    private readonly string? _semanticClass;
    private readonly string? _semanticNamespace;
    private readonly string? _semanticFile;
    private readonly string? _semanticLastEntity;

    private readonly string? _sessionProject;
    private readonly string? _sessionClass;
    private readonly string? _sessionNamespace;

    private ParsingStateSnapshot()
    {
        var context = ContextManager.Current;
        _semanticProject = context.CurrentProject;
        _semanticClass = context.CurrentClass;
        _semanticNamespace = context.CurrentNamespace;
        _semanticFile = context.CurrentFile;
        _semanticLastEntity = context.LastEntity;

        _sessionProject = SessionContext.CurrentProject;
        _sessionClass = SessionContext.CurrentClass;
        _sessionNamespace = SessionContext.CurrentNamespace;
    }

    /// <summary>Copies the current values out of both parsing singletons.</summary>
    public static ParsingStateSnapshot Capture() => new();

    /// <summary>Writes the captured values back onto both parsing singletons.</summary>
    public void Restore()
    {
        var context = ContextManager.Current;
        context.CurrentProject = _semanticProject;
        context.CurrentClass = _semanticClass;
        context.CurrentNamespace = _semanticNamespace;
        context.CurrentFile = _semanticFile;
        context.LastEntity = _semanticLastEntity;

        SessionContext.CurrentProject = _sessionProject;
        SessionContext.CurrentClass = _sessionClass;
        SessionContext.CurrentNamespace = _sessionNamespace;
    }
}
