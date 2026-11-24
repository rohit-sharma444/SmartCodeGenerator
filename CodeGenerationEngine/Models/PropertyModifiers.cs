namespace CodeGenerationEngine.Models;

/// <summary>Modifiers a property can be given when it is created: <c>readonly</c>,
/// <c>private</c>, <c>static</c> and <c>required</c>.</summary>
[Flags]
public enum PropertyModifiers
{
    None = 0,
    ReadOnly = 1,
    Private = 2,
    Static = 4,
    Required = 8,
}
