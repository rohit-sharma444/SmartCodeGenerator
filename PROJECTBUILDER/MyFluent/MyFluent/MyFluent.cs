using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace MyFluent
{
    using MyFluent.Framework;

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

    public class ValidationError
    {
        public string ErrorMessage { get; set; }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }

        public List<ValidationError> Errors { get; set; }
    }
}
