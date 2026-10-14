using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;

namespace CodeGenerator.Tests.Core.Validation;

[TestClass]
[DoNotParallelize]
public class DataValidationCommandTests
{
    private static string _root = string.Empty;

    [TestInitialize]
    public void Reset()
    {
        ValidationRegistry.Clear();
        LearnedIntentStore.UseInMemory();
        _root = ValidationTestWorkspace.Create();
    }

    [TestCleanup]
    public void Cleanup() => ValidationTestWorkspace.Delete(_root);

    [TestMethod]
    public void Add_validation_registers_a_data_rule()
    {
        var command = new ParsedCommand
        {
            Intent = IntentType.AddValidation,
            ClassName = "Employee",
            PropertyName = "Name",
            ValidationText = "Name is required",
            OriginalSentence = "add validation should not be blank on Name in Employee"
        };
        ProjectMetaData? project = null;

        var ok = CommandHandlingService.Execute(command, ref project, [], _root, _ => { });

        Assert.IsTrue(ok);
        Assert.HasCount(1, ValidationRegistry.Get("Employee", ValidationCategory.Data));
    }

    private static (bool Ok, string Said) Say(string sentence)
    {
        var command = NaturalLanguageBridge.CommandFront.Parse(sentence, "");
        ProjectMetaData? project = null;
        var writer = new StringWriter();
        var original = Console.Out;
        Console.SetOut(writer);
        try
        {
            var ok = CommandHandlingService.Execute(command, ref project, [], _root, _ => { });
            return (ok, writer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    /// <summary>A partial Customer with a nested class and a sibling record struct in its file,
    /// and a second part in Customer.Notes.cs.</summary>
    private static void WriteCustomer()
    {
        File.WriteAllText(Path.Combine(_root, "Customer.cs"), """
            namespace Shop.Models;

            public partial class Customer
            {
                public string Name { get; set; } = "";

                public class Address
                {
                    public string Street { get; set; } = string.Empty;
                }
            }

            public readonly record struct Money(decimal Amount, string Currency = "GBP");
            """);

        File.WriteAllText(Path.Combine(_root, "Customer.Notes.cs"), """
            namespace Shop.Models;

            public partial class Customer
            {
                public string? Notes { get; set; }
            }
            """);
    }

    /// <summary>Street is Customer.Address's, so a rule on it in Customer would write
    /// <c>this.Street</c> into Customer's validator, which does not compile.</summary>
    [TestMethod]
    [DataRow("Street")]
    [DataRow("Amount")]
    public void A_rule_on_a_property_of_a_nested_or_sibling_type_is_refused(string property)
    {
        WriteCustomer();

        var (ok, said) = Say($"add validation required on {property} in Customer");

        Assert.IsFalse(ok, said);
        StringAssert.Contains(said, $"'Customer' has no property '{property}'");
        Assert.IsEmpty(ValidationRegistry.Get("Customer"));
    }

    /// <summary>Notes is declared in Customer.Notes.cs, another part of the same class.</summary>
    [TestMethod]
    public void A_rule_on_a_property_in_another_partial_file_is_added()
    {
        WriteCustomer();

        var (ok, said) = Say("add validation at most 50 characters on Notes in Customer");

        Assert.IsTrue(ok, said);
        Assert.HasCount(1, ValidationRegistry.Get("Customer", ValidationCategory.Data));
    }

    [TestMethod]
    public void Show_properties_lists_only_the_class_own_properties_from_every_part()
    {
        WriteCustomer();

        var (_, said) = Say("show properties of Customer");

        StringAssert.Contains(said, "string Name");
        StringAssert.Contains(said, "string? Notes");
        Assert.DoesNotContain("Street", said);
        Assert.DoesNotContain("Amount", said);
    }

    [TestMethod]
    public void A_rule_on_a_property_the_class_does_not_have_is_refused_with_a_close_name()
    {
        var (ok, said) = Say("add validation required on Nmae in Employee");

        Assert.IsFalse(ok);
        StringAssert.Contains(said, "'Employee' has no property 'Nmae'");
        StringAssert.Contains(said, "Did you mean Name");
        Assert.IsEmpty(ValidationRegistry.Get("Employee"));
    }

    [TestMethod]
    public void A_rule_on_a_class_that_is_not_there_is_refused_with_a_close_name()
    {
        var (ok, said) = Say("add validation required on Name in Employe");

        Assert.IsFalse(ok);
        StringAssert.Contains(said, "Class 'Employe' not found");
        StringAssert.Contains(said, "Did you mean Employee");
        Assert.IsEmpty(ValidationRegistry.Get("Employe"));
    }

    [TestMethod]
    public void A_business_rule_naming_a_missing_property_is_refused()
    {
        var (ok, said) = Say("add business validation Foo must be after StartDate in Employee");

        Assert.IsFalse(ok);
        StringAssert.Contains(said, "has no property 'Foo'");
        Assert.IsEmpty(ValidationRegistry.Get("Employee"));
    }

    [TestMethod]
    public void The_same_rule_twice_is_added_once()
    {
        Assert.IsTrue(Say("add validation required on Name in Employee").Ok);
        var (ok, said) = Say("add validation should not be blank on Name in Employee");

        Assert.IsTrue(ok);
        StringAssert.Contains(said, "already has this rule");
        Assert.HasCount(1, ValidationRegistry.Get("Employee"));
    }

    [TestMethod]
    public void Show_validation_of_a_class_that_is_not_there_says_so()
    {
        var (_, said) = Say("show validation Nope");

        StringAssert.Contains(said, "Class 'Nope' not found");
    }

    [TestMethod]
    public void Add_validation_reports_failure_on_unparseable_text()
    {
        var command = new ParsedCommand
        {
            Intent = IntentType.AddValidation,
            ClassName = "Employee",
            ValidationText = "flibbertigibbet",
            OriginalSentence = "add validation flibbertigibbet"
        };
        ProjectMetaData? project = null;

        var ok = CommandHandlingService.Execute(command, ref project, [], _root, _ => { });

        Assert.IsFalse(ok);
    }
}
