using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerator.Web.Chat;

namespace CodeGenerator.Tests.Web.Chat;

// ValidationRegistry is a process-wide static and MSTestSettings parallelizes at method level.
[TestClass]
[DoNotParallelize]
public class ValidationAddTests
{
    private string _folder = string.Empty;
    private string _file = string.Empty;

    [TestInitialize]
    public void MakeClass()
    {
        ValidationRegistry.Clear();

        _folder = Path.Combine(Path.GetTempPath(), "va-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_folder);

        _file = Path.Combine(_folder, "Employee.cs");
        File.WriteAllText(_file,
            "namespace Acme\r\n" +
            "{\r\n" +
            "    public class Employee\r\n" +
            "    {\r\n" +
            "        public int Age { get; set; }\r\n" +
            "\r\n" +
            "        public string Name { get; set; } = string.Empty;\r\n" +
            "    }\r\n" +
            "}\r\n");
    }

    [TestCleanup]
    public void RemoveFolder()
    {
        ValidationRegistry.Clear();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private ValidationTarget Target => new("Employee", _file);

    [TestMethod]
    public void Adds_a_data_rule()
    {
        var outcome = ValidationAdd.Apply(Target, "Age must be at least 18");

        Assert.HasCount(1, outcome.Added);
        Assert.IsEmpty(outcome.Refused);
        StringAssert.Contains(File.ReadAllText(_file), "// Age must be at least 18");
    }

    [TestMethod]
    public void Adds_a_business_rule_without_being_told_which_it_is()
    {
        var outcome = ValidationAdd.Apply(Target, "Name must be different from Age");

        Assert.HasCount(1, outcome.Added);
        StringAssert.Contains(File.ReadAllText(_file), "// Name must be different from Age");
    }

    [TestMethod]
    public void Registers_the_rule_so_show_validation_agrees_with_the_file()
    {
        ValidationAdd.Apply(Target, "Age must be at least 18");

        Assert.HasCount(1, ValidationRegistry.Get("Employee", ValidationCategory.Data));
    }

    [TestMethod]
    public void Names_the_fragment_it_could_not_understand_and_writes_nothing()
    {
        var outcome = ValidationAdd.Apply(Target, "Age should be sensible really");

        Assert.IsEmpty(outcome.Added);
        Assert.HasCount(1, outcome.Refused);
        StringAssert.Contains(outcome.Refused[0], "Age should be sensible really");
        StringAssert.DoesNotMatch(File.ReadAllText(_file),
            new System.Text.RegularExpressions.Regex("Validate"));
    }

    [TestMethod]
    public void Adds_what_it_understood_and_names_what_it_did_not()
    {
        var outcome = ValidationAdd.Apply(
            Target, "Age must be at least 18\r\nName ought to be nice");

        Assert.HasCount(1, outcome.Added);
        Assert.HasCount(1, outcome.Refused);
        StringAssert.Contains(File.ReadAllText(_file), "// Age must be at least 18");
    }

    [TestMethod]
    public void A_rule_naming_an_absent_property_is_refused_by_name()
    {
        var outcome = ValidationAdd.Apply(Target, "Salary must be positive");

        Assert.IsEmpty(outcome.Added);
        StringAssert.Contains(outcome.Refused[0], "Salary");
    }

    [TestMethod]
    public void The_accepted_phrasings_come_from_the_catalog()
    {
        var phrasings = ValidationAdd.Phrasings();

        Assert.IsGreaterThan(20, phrasings.Count);
        CollectionAssert.Contains(phrasings.ToList(), "Name is required");
    }
}
