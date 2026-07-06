using NaturalLanguageEngine.Models.Common;
using NaturalLanguageEngine.Models.Diagnostics;
using NaturalLanguageEngine.Models.Lexical;
using NaturalLanguageEngine.Models.Semantics;
using NaturalLanguageEngine.Models.Syntax;

namespace NaturalLanguageEngine.Core;

public sealed class AnalysisContext
{
    public AnalysisContext(string text)
    {
        OriginalText = text;
    }

    public IList<Diagnostic> Diagnostics { get; } = new List<Diagnostic>();

    public IReadOnlyList<Token> Tokens { get; set; } = Array.Empty<Token>();

    public string OriginalText { get; }

    public string NormalizedText { get; set; } = string.Empty;

    public Document Document { get; set; } = new();

    public Intent? Intent { get; set; }

    public IList<Relationship> Relationships { get; }
        = new List<Relationship>();

    public IList<Entity> Entities { get; } = new List<Entity>();

    /// <summary>
    /// Dependency edges produced by <see cref="Stages.DependencyParsingStage"/>,
    /// one sentence's worth appended after another. Empty until that stage
    /// has run (or if it isn't wired into the pipeline at all).
    /// </summary>
    public IList<Dependency> Dependencies { get; } = new List<Dependency>();

    public double Confidence { get; set; }

    public IDictionary<string, object> Properties { get; }
        = new Dictionary<string, object>();
}