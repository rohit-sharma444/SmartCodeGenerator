namespace CodeGenerationEngine.Models;

public sealed record IntentMatch(
    IntentType Intent,
    int Score,
    double Confidence,
    string Reason);