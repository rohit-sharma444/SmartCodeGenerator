using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using MyFluent;
using MyFluent.Framework;

namespace TestMyFluent
{
    public enum Language
    {
        English = 0,
        Bengali = 1
    }

    public enum JobType
    {
        Accounting = 0,
        IT = 1
    }

    public class Employee
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string CrediCardNo { get; set; }
        public string Message { get; set; }
        public Language Language { get; set; }

        public List<JobType> JobTypes { get; set; }
    }

    public class ObjectToValidate
    {
        public Employee Employee { get; set; }
    }

    public class SampleValidator : BaseValidator<SampleValidator, ObjectToValidate>
    {
        public SampleValidator()
        {
            RuleFor(obj => obj)
                .NotNull("ObjectToValidate cannot be null");

            RuleFor(obj => obj.Employee)
                .NotNull("Employee cannot be null");

            If(obj => obj.Employee.Language, language => language == Language.Bengali)
                .ConditionRuleFor(obj => obj.Employee.Name)
                    .NotNullOrEmpty("Name not null or empty")
                    .IsAlpha(@"\u0985-\u09E3\s", "Employee name not Bengali letters with spaces")
                    .Matches(@"^[\u0985-\u09E3\s]+$", "Employee name not Bengali letters with spaces")
                .ConditionRuleFor(obj => obj.Employee.Message)
                    .IsAlpha(@"\u0985-\u09E3\s", "Employee message not Bengali letters with spaces");

            If(obj => obj.Employee.Language, language => language == Language.English)
                .ConditionRuleFor(obj => obj.Employee.Name)
                    .NotNullOrEmpty("Name not null or empty")
                    .IsAlpha(@"a-zA-Z\s", "Employee name not English letters with spaces")
                    .Matches("^[a-zA-Z ]+", "Employee name not English letters with spaces")
                .ConditionRuleFor(obj => obj.Employee.Message)
                    .IsAlpha(@"a-zA-Z\s", "Employee message not English letters with spaces");

            RuleFor(obj => obj.Employee.JobTypes)
                .NotNull("Employee Job types cannot be null")
                .Required(jobTypes => jobTypes.Count > 0, "Employee requires one or more Job type");

            RuleFor(obj => obj.Employee.CrediCardNo)
                .NotNullOrEmpty("Credit card no cannot be null or empty")
                .CreditCard("Invalid credit card no");

            RuleFor(obj => obj.Employee.Email)
                .NotNullOrEmpty("Employee email cannot be null or empty")
                .Email("Invalid email address");
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            ObjectToValidate objToValidate = new ObjectToValidate();

            objToValidate.Employee = new Employee
            {
                Language = TestMyFluent.Language.English,
                //Language = TestMyFluent.Language.Bengali,
                Name = "Shantanu",
                //Name = "ফোটোস", 
                //CrediCardNo = "****************",
                //CrediCardNo = "5213256337202260",                
                Email = "shantanu@hotmail.com",
                //Message = "হ্যালো ফোটোস"
                Message = "Hello World",
                //JobTypes = new List<JobType>{ JobType.IT }
                JobTypes = new List<JobType> { }
            };

            ValidationResult result = SampleValidator.Validate(objToValidate);
        }
    }
}
