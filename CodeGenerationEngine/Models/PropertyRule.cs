namespace CodeGenerationEngine.Models;

public class PropertyRule
{
    public string PropertyName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    // Backward-compatible primary flag
    public bool Required { get; set; }

    // Alias used by validators
    public bool IsRequired
    {
        get => Required;
        set => Required = value;
    }

    // Uniqueness
    public bool IsUnique { get; set; }

    // Expected CLR/type name (e.g. "int", "string", "System.Guid")
    public string? ExpectedType { get; set; }

    // Controls null allowance
    public bool AllowNull { get; set; } = true;

    public int? MinLength { get; set; }

    public int? MaxLength { get; set; }

    public decimal? MinValue { get; set; }

    public decimal? MaxValue { get; set; }

    public List<string> AllowedValues { get; set; } = new();

    // Primary regex property
    public string? RegexPattern { get; set; }

    // Alias expected by some validators
    public string? Pattern
    {
        get => RegexPattern;
        set => RegexPattern = value;
    }

    // Generic custom rule text
    public string? CustomRule { get; set; }
}