using NaturalLanguageEngine.Models.Common;

namespace NaturalLanguageEngine.Models.Semantics;

public sealed class Entity
{
    public string Text { get; set; } = string.Empty;

    public EntityType Type { get; set; }

    public ConfidenceScore Confidence { get; set; }

    public IDictionary<string, object> Metadata { get; } =
        new Dictionary<string, object>();
}