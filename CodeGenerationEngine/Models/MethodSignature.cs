namespace CodeGenerationEngine.Models;

/// <summary>One public method of a class: what it is called, and what it returns.</summary>
/// <remarks>
/// Parameters are deliberately absent. The parser this comes from reads as far as the opening
/// bracket and no further, and a record with an empty parameter list would invite a caller to
/// draw <c>Save()</c> for a method that takes three arguments.
/// </remarks>
public sealed record MethodSignature(string Name, string ReturnType);
