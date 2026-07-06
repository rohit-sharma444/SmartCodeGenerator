namespace CodeGenerationEngine.Models;

public sealed class LearnedToken
{
    public string Word { get; set; } = string.Empty;

    public string NormalizedWord { get; set; } = string.Empty;

    public TokenCategory Category { get; set; }

    public int Frequency { get; set; }

    public double Confidence { get; set; }
}