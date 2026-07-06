namespace CodeGenerationEngine.Models;

public sealed class IntentScore
{
    public IntentType Intent { get; init; }

    public double Score { get; init; }

    public List<string> Evidence { get; } = [];

    public override string ToString()
        => $"{Intent} ({Score:P2})";
}