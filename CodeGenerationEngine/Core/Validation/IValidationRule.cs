namespace CodeGenerationEngine.Core.Validation;

/// <summary>A parsed validation rule that knows how to describe itself (for the
/// show commands) and emit its own check into a generated Validate() method.</summary>
public interface IValidationRule
{
    ValidationCategory Category { get; }
    string Describe();
    void Emit(ValidationEmitContext ctx);
}
