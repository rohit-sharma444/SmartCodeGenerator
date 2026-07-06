namespace CodeGenerationEngine.Models;

public sealed class CommandInfo
{
    public string Category { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Example { get; init; } = string.Empty;
}