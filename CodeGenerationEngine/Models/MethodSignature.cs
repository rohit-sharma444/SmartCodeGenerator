namespace CodeGenerationEngine.Models;

/// <summary>One public method of a class: what it is called, what it returns, and its parameters
/// as written between the brackets.</summary>
/// <remarks>
/// <c>show methods</c> printed <c>void RaiseLimit()</c> for <c>RaiseLimit(decimal amount)</c>,
/// because the parser read only as far as the opening bracket.
/// </remarks>
public sealed record MethodSignature(string Name, string ReturnType, string Parameters = "")
{
    /// <summary><c>void RaiseLimit(decimal amount)</c>.</summary>
    public string Display => $"{ReturnType} {Name}({Parameters})";
}
