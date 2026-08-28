namespace CodeGenerationEngine.Core.Validation;

/// <summary>A parsed validation rule that knows how to describe itself (for the
/// show commands) and emit its own check into a generated Validate() method.</summary>
public interface IValidationRule
{
    ValidationCategory Category { get; }

    /// <summary>
    /// Every property this rule refers to.
    /// </summary>
    /// <remarks>
    /// So a caller can refuse a rule naming a property the class does not have, rather than
    /// emitting it and finding out at emit time — which is a skipped-rule comment and a reported
    /// success over a check that is not there.
    /// </remarks>
    IReadOnlyList<string> Properties { get; }

    string Describe();
    void Emit(ValidationEmitContext ctx);
}
