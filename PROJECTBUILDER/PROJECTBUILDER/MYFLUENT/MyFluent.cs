namespace PROJECTBUILDER.MyFluent;

using System.Collections.Generic;
using global::MyFluent.Framework;
using PROJECTBUILDER.MyFluent;
/// <summary>
/// Represents Base Validator.
/// </summary>

public abstract class BaseValidator<TValidator, T> : Validator<T>
    where TValidator : Validator<T>, new()
{
    static TValidator validator = new TValidator();

    public static Validator<T> GetValidator()
    {
        return validator;
    }

    public new static ValidationResult Validate(T item)
    {
        return validator.Validate(item);
    }
}
/// <summary>
/// Represents Validation Error.
/// </summary>

public class ValidationError
{
    public string ErrorMessage { get; set; }
}
/// <summary>
/// Represents Validation Result.
/// </summary>

public class ValidationResult
{
    public bool IsValid { get; set; }

    public List<ValidationError> Errors { get; set; }
}
