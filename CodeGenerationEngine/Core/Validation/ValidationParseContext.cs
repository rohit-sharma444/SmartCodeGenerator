namespace CodeGenerationEngine.Core.Validation;

/// <summary>Everything a parser needs to turn one rule fragment into an
/// <see cref="IValidationRule"/>. Operand resolution against
/// <see cref="KnownProperties"/> is optional at parse time (business rules
/// resolve lazily at emit time).</summary>
public sealed class ValidationParseContext
{
    public required string Fragment { get; init; }
    public required string ClassName { get; init; }

    // Forward-looking context: not yet read by any shipped parser. Business rules
    // resolve their operand properties lazily at emit time via
    // ValidationEmitContext.HasProperty rather than consulting these at parse time.
    public string? TargetProperty { get; init; }
    public IReadOnlyList<string> KnownProperties { get; init; } = System.Array.Empty<string>();
}
