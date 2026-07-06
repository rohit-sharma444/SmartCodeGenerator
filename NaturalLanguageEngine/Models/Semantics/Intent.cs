using NaturalLanguageEngine.Models.Common;

namespace NaturalLanguageEngine.Models.Semantics;

public sealed class Intent
{
    public string Name { get; set; } = string.Empty;

    public ConfidenceScore Confidence { get; set; }
}