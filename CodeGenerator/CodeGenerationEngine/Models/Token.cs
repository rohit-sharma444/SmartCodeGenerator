namespace CodeGenerationEngine.Models;

public sealed class Token
{
    public string Original { get; set; } = string.Empty;

    public string Normalized { get; set; } = string.Empty;

    public TokenCategory Category { get; set; }

    public double Confidence { get; set; }

    public override string ToString() => $"{Normalized} ({Category})";
}