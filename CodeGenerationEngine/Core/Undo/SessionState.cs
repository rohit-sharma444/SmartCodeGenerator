using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core.Undo;

/// <summary>
/// The session's in-memory state a command can change: the current project, class and namespace
/// the parser carries, and the validation rules — which live only here, so without this an undone
/// `add validation` would change nothing.
/// </summary>
public sealed class SessionState : ISessionState
{
    private readonly ParsingStateSnapshot _parsing;
    private readonly (string?, string?, string?, string?, string?, string?, string?, string?) _parsingValues;
    private readonly ProjectMetaData? _project;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<IValidationRule>> _rules;

    private SessionState()
    {
        _parsing = ParsingStateSnapshot.Capture();
        _parsingValues = ParsingValues();
        _project = ProjectManager.CurrentProject;
        _rules = ValidationRegistry.Snapshot();
    }

    public static ISessionState Capture() => new SessionState();

    public bool SameAs(ISessionState other) =>
        other is SessionState s
        && s._parsingValues == _parsingValues
        && ReferenceEquals(s._project, _project)
        && SameRules(s._rules, _rules);

    public void Restore()
    {
        _parsing.Restore();
        ProjectManager.CurrentProject = _project;
        ValidationRegistry.Restore(_rules);
    }

    // The same fields ParsingStateSnapshot keeps, which it holds privately.
    private static (string?, string?, string?, string?, string?, string?, string?, string?) ParsingValues()
    {
        var context = ContextManager.Current;
        return (context.CurrentProject, context.CurrentClass, context.CurrentNamespace, context.CurrentFile,
            context.LastEntity, SessionContext.CurrentProject, SessionContext.CurrentClass,
            SessionContext.CurrentNamespace);
    }

    // Rules are immutable objects, so the same rule is the same reference.
    private static bool SameRules(
        IReadOnlyDictionary<string, IReadOnlyList<IValidationRule>> a,
        IReadOnlyDictionary<string, IReadOnlyList<IValidationRule>> b)
    {
        var classes = a.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key)
            .Union(b.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key), StringComparer.OrdinalIgnoreCase);

        foreach (var name in classes)
        {
            var x = a.TryGetValue(name, out var ra) ? ra : [];
            var y = b.TryGetValue(name, out var rb) ? rb : [];
            if (x.Count != y.Count || x.Where((rule, i) => !ReferenceEquals(rule, y[i])).Any())
                return false;
        }

        return true;
    }
}
