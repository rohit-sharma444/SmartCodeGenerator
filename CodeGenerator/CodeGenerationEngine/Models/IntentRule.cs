namespace CodeGenerationEngine.Models;

public sealed class IntentRule
{
    public IntentType Intent { get; set; }

    public int Priority { get; set; }

    public List<string> Actions { get; set; } = [];

    public List<string> Targets { get; set; } = [];

    public List<string> Keywords { get; set; } = [];

    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

    public bool IsEnabled { get; set; } = true;
}