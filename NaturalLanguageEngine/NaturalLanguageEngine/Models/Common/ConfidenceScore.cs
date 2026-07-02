namespace NaturalLanguageEngine.Models.Common;

/// <summary>
/// Represents a confidence value between 0.0 and 1.0.
/// </summary>
public readonly record struct ConfidenceScore(double Value)
{
    public double Value { get; } =
        Math.Clamp(Value, 0d, 1d);

    public override string ToString()
        => $"{Value:P2}";

    public static implicit operator double(ConfidenceScore score)
        => score.Value;

    public static implicit operator ConfidenceScore(double value)
        => new(value);
}