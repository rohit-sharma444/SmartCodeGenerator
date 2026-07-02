using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

using MyFluent;

namespace MvcApplication1.Validators
{
    public class RegisterValidator : BaseValidator<RegisterValidator, MvcApplication1.Models.RegisterModel>
    {
        public RegisterValidator()
        {
            RuleFor(m => m.UserName)
               .NotNullOrEmpty("User name cannot be null or empty")
               .Matches(@"^[a-zA-Z0-9]+$", "User name should only contain letters and numbers");

            RuleFor(m => m.Email)
                .NotNullOrEmpty("Email cannot be null or empty")
                .Email("Not a valid email address");

            RuleFor(m => m.CreditCard)
                .NotNullOrEmpty("Credit card cannot be null or empty")
                .CreditCard("Not a valid credit card number");

            RuleFor(m => m.Password)
                .NotNullOrEmpty("Password cannot be null or empty")
                .JQueryRequired("UserNameNotInPassword", "User name cannot be a part of the password");

            RuleFor(m => m.ConfirmPassword)
                .NotNullOrEmpty("Confirm password cannot be null or empty")
                .JQueryRequired("PasswordAndConfirmAreSame", "Password and confirm are not the same");
        }
    }
}