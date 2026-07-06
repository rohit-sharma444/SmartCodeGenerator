namespace CodeGenerationEngine.Models;

public sealed class SemanticModel
{
    public string OriginalText { get; set; } = string.Empty;
    public string NormalizedText { get; set; } = string.Empty;
    public List<Token> Tokens { get; set; } = [];
    public List<SemanticEntity> Entities { get; set; } = [];
    public List<SemanticRelation> Relations { get; set; } = [];
    public List<string> ValidationErrors { get; set; } = [];
    public SemanticContext? Context { get; set; }
    public IntentType Intent { get; set; }
    public double Confidence { get; set; }
    public bool IsValid => ValidationErrors.Count == 0;
    public List<IntentScore> RankedIntents { get; } = [];
    public List<string> Diagnostics { get; } = [];
}