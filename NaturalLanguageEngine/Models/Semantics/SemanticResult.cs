using NaturalLanguageEngine.Models.Common;
using NaturalLanguageEngine.Models.Diagnostics;

namespace NaturalLanguageEngine.Models.Semantics;

public sealed class SemanticResult
{
    public IList<Entity> Entities { get; } = new List<Entity>();

    public IList<Relationship> Relationships { get; } = new List<Relationship>();

    public Intent? Intent { get; set; }

    public double Confidence { get; set; }

    /// <summary>
    /// Diagnostics produced during analysis. A <see cref="DiagnosticSeverity.Critical"/>
    /// entry means a pipeline stage failed and analysis stopped early, so
    /// downstream fields (entities, relationships, intent) may be partial
    /// or empty - callers that care about completeness should check this
    /// collection rather than assuming a normal empty result.
    /// </summary>
    public IList<Diagnostic> Diagnostics { get; } = new List<Diagnostic>();
}