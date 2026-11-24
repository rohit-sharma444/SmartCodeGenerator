using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// <c>create enum</c> writes an enum with the values it is given, and refuses what C# forbids.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EnumCommandTests
{
    private string _workspace = string.Empty;
    private string LastOutput { get; set; } = string.Empty;
    private string Shop => Path.Combine(_workspace, "Shop");

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_enum_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Shop);
        File.WriteAllText(Path.Combine(Shop, "Shop.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Write("Shape.cs", "namespace Shop;\n\npublic class Shape\n{\n}\n");
        Write("IShape.cs", "namespace Shop;\n\npublic interface IShape\n{\n}\n");
        ProjectManager.CurrentProject = new ProjectMetaData
        {
            Success = true,
            ProjectName = "Shop",
            ProjectPath = Shop,
            CsProjPath = Path.Combine(Shop, "Shop.csproj")
        };
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private void Write(string name, string text) => File.WriteAllText(Path.Combine(Shop, name), text);

    private string Read(string name) => File.ReadAllText(Path.Combine(Shop, name));

    private bool Run(string input)
    {
        ProjectMetaData? project = ProjectManager.CurrentProject;
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try { return CommandFront.Run(input, ref project, [], _workspace, _ => { }); }
        finally
        {
            Console.SetOut(original);
            LastOutput = writer.ToString();
        }
    }

    private string[] ProjectErrors() =>
    [
        .. Compiler.CompileSources(Directory.GetFiles(Shop, "*.cs").Select(f => (f, File.ReadAllText(f))))
            .Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.ToString())
    ];

    private void AssertValuesInOrder(string code, params string[] values)
    {
        var last = -1;
        foreach (var value in values)
        {
            var at = code.IndexOf(value, last + 1, StringComparison.Ordinal);
            Assert.IsGreaterThan(last, at, $"{value} should follow the value before it in:\n{code}");
            last = at;
        }
    }

    [TestMethod]
    [DataRow("create enum Status with values Active, Closed, Pending")]
    [DataRow("create enum Status with members Active, Closed, Pending")]
    [DataRow("create enum Status with Active, Closed, Pending")]
    [DataRow("create enum Status having Active, Closed, Pending")]
    [DataRow("create enum Status with values Active, Closed and Pending")]
    [DataRow("create enum Status with values Active, Closed, and Pending")]
    [DataRow("create enum Status having values Active, Closed, Pending")]
    [DataRow("create enum Status having members Active, Closed, Pending")]
    public void CreateEnum_WritesTheValuesInOrder(string input)
    {
        Assert.IsTrue(Run(input), LastOutput);

        var code = Read("Status.cs");
        StringAssert.Contains(code, "public enum Status");
        AssertValuesInOrder(code, "Active,", "Closed,", "Pending");
        Assert.IsEmpty(ProjectErrors(), string.Join(Environment.NewLine, ProjectErrors()));
    }

    [TestMethod]
    public void CreateEnum_WithNoValues_WritesAnEmptyEnum()
    {
        Assert.IsTrue(Run("create enum Status"), LastOutput);

        StringAssert.Contains(Read("Status.cs"), "public enum Status");
        Assert.IsEmpty(ProjectErrors(), string.Join(Environment.NewLine, ProjectErrors()));
    }

    [TestMethod]
    [DataRow("create enum Status with values")]
    [DataRow("create enum Status with members")]
    [DataRow("create enum Status having values")]
    public void CreateEnum_WithTheListWordAndNoValues_WritesAnEmptyEnum(string input)
    {
        Assert.IsTrue(Run(input), LastOutput);

        var code = Read("Status.cs");
        StringAssert.Contains(code, "public enum Status");
        Assert.DoesNotContain("values", code);
        Assert.DoesNotContain("members", code);
        Assert.IsEmpty(ProjectErrors(), string.Join(Environment.NewLine, ProjectErrors()));
    }

    [TestMethod]
    public void CreateClass_WhenAnEnumHasTheName_RecommendsAddValue()
    {
        Write("Status.cs", StatusSource);

        Assert.IsFalse(Run("create class Status"));

        StringAssert.Contains(LastOutput, "already exists");
        StringAssert.Contains(LastOutput, "add value <name> to Status");
        Assert.DoesNotContain("add property", LastOutput);
        Assert.AreEqual(StatusSource, Read("Status.cs"));
    }

    [TestMethod]
    public void CreateEnum_LowerCaseName_IsPascalCasedAndValueKeptAsTyped()
    {
        Assert.IsTrue(Run("create enum status with values active"), LastOutput);

        var code = Read("Status.cs");
        StringAssert.Contains(code, "public enum Status");
        StringAssert.Contains(code, "active");
        Assert.IsEmpty(ProjectErrors(), string.Join(Environment.NewLine, ProjectErrors()));
    }

    [TestMethod]
    [DataRow("create enum Status with values Active, active", "'active' is repeated")]
    [DataRow("create enum Status with values Active, class", "'class' is not a valid enum value name")]
    [DataRow("create enum Status inherits Base", "An enum cannot inherit")]
    public void CreateEnum_Refused_WritesNothing(string input, string reason)
    {
        Assert.IsFalse(Run(input));

        StringAssert.Contains(LastOutput, reason);
        Assert.IsFalse(File.Exists(Path.Combine(Shop, "Status.cs")), "a refusal writes nothing");
    }

    [TestMethod]
    public void CreateEnum_ExistingFile_IsRefusedAndLeftAsItIs()
    {
        Write("Status.cs", "namespace Shop;\n\npublic enum Status\n{\n    Old\n}\n");

        Assert.IsFalse(Run("create enum Status with values Active"));

        StringAssert.Contains(LastOutput, "already exists");
        StringAssert.Contains(Read("Status.cs"), "Old");
    }

    private const string StatusSource = "namespace Shop;\n\npublic enum Status\n{\n    Active,\n    Closed,\n    Pending\n}\n";

    private const string OrderSource = "namespace Shop;\n\npublic class Order\n{\n}\n";

    [TestMethod]
    public void AddValue_AppendsAfterTheLastValue()
    {
        Write("Status.cs", StatusSource);

        Assert.IsTrue(Run("add value Archived to Status"), LastOutput);

        AssertValuesInOrder(Read("Status.cs"), "Pending,", "Archived");
        StringAssert.Contains(LastOutput, "Value 'Archived' added to 'Status'.");
        Assert.IsEmpty(ProjectErrors(), string.Join(Environment.NewLine, ProjectErrors()));
    }

    [TestMethod]
    public void AddValue_OnOneLineEnum_Compiles()
    {
        Write("Status.cs", "namespace Shop;\n\npublic enum Status { Active, Closed, Pending }\n");

        Assert.IsTrue(Run("add value Archived to Status"), LastOutput);

        AssertValuesInOrder(Read("Status.cs"), "Pending,", "Archived");
        Assert.IsEmpty(ProjectErrors(), string.Join(Environment.NewLine, ProjectErrors()));
    }

    [TestMethod]
    public void AddValue_ToAnEmptyEnum_Compiles()
    {
        Write("Status.cs", "namespace Shop;\n\npublic enum Status\n{\n}\n");

        Assert.IsTrue(Run("add value Active to Status"), LastOutput);

        StringAssert.Contains(Read("Status.cs"), "Active");
        Assert.IsEmpty(ProjectErrors(), string.Join(Environment.NewLine, ProjectErrors()));
    }

    [TestMethod]
    public void AddValue_WhenTheEnumNumbersItsValues_NumbersTheNewOne()
    {
        Write("Level.cs", "namespace Shop;\n\npublic enum Level { Low = 1, High = 5 }\n");

        Assert.IsTrue(Run("add value Top to Level"), LastOutput);

        StringAssert.Contains(Read("Level.cs"), "Top = 6");
        Assert.IsEmpty(ProjectErrors(), string.Join(Environment.NewLine, ProjectErrors()));
    }

    [TestMethod]
    [DataRow("byte", "Low = 1, High = 255")]
    [DataRow("sbyte", "Low = -1, High = 127")]
    [DataRow("short", "Low = 1, High = 32767")]
    [DataRow("ushort", "Low = 1, High = 65535")]
    [DataRow("int", "Low = 1, High = 2147483647")]
    public void AddValue_WhenTheNextNumberDoesNotFitTheUnderlyingType_IsRefused(string type, string members)
    {
        var source = $"namespace Shop;\n\npublic enum Prio : {type} {{ {members} }}\n";
        Write("Prio.cs", source);

        Assert.IsFalse(Run("add value Urgent to Prio"));

        StringAssert.Contains(LastOutput, "does not fit");
        StringAssert.Contains(LastOutput, type);
        Assert.AreEqual(source, Read("Prio.cs"));
    }

    [TestMethod]
    [DataRow("byte", "Low = 1, High = 254", "Urgent = 255")]
    [DataRow("sbyte", "Low = -5, High = -3", "Urgent = -2")]
    public void AddValue_WhenTheNextNumberFitsTheUnderlyingType_NumbersIt(string type, string members, string expected)
    {
        Write("Prio.cs", $"namespace Shop;\n\npublic enum Prio : {type} {{ {members} }}\n");

        Assert.IsTrue(Run("add value Urgent to Prio"), LastOutput);

        StringAssert.Contains(Read("Prio.cs"), expected);
        Assert.IsEmpty(ProjectErrors(), string.Join(Environment.NewLine, ProjectErrors()));
    }

    [TestMethod]
    [DataRow("add value Archived to enum Status")]
    [DataRow("add a value Archived to Status")]
    [DataRow("add the value Archived to Status")]
    [DataRow("add the value Archived to the enum Status")]
    public void AddValue_WithAnArticleOrTheWordEnum_AddsIt(string input)
    {
        Write("Status.cs", StatusSource);

        Assert.IsTrue(Run(input), LastOutput);

        AssertValuesInOrder(Read("Status.cs"), "Pending,", "Archived");
        Assert.IsEmpty(ProjectErrors(), string.Join(Environment.NewLine, ProjectErrors()));
    }

    [TestMethod]
    [DataRow("remove the value Pending from Status")]
    [DataRow("remove a value Pending from enum Status")]
    [DataRow("delete the value Pending from the enum Status")]
    public void RemoveValue_WithAnArticleOrTheWordEnum_RemovesIt(string input)
    {
        Write("Status.cs", StatusSource);

        Assert.IsTrue(Run(input), LastOutput);

        Assert.DoesNotContain("Pending", Read("Status.cs"));
        Assert.IsEmpty(ProjectErrors(), string.Join(Environment.NewLine, ProjectErrors()));
    }

    private const string RenumberNote =
        "The values after Closed now have numbers one lower; data stored as numbers will read differently.";

    [TestMethod]
    public void RemoveValue_FromTheMiddleOfAnUnnumberedEnum_SaysTheNumbersMoved()
    {
        Write("Status.cs", StatusSource);

        Assert.IsTrue(Run("remove value Closed from Status"), LastOutput);

        StringAssert.Contains(LastOutput, RenumberNote);
    }

    [TestMethod]
    [DataRow("namespace Shop;\n\npublic enum Status\n{\n    Active,\n    Pending,\n    Closed\n}\n")]
    [DataRow("namespace Shop;\n\npublic enum Status\n{\n    Active = 1,\n    Closed = 2,\n    Pending = 3\n}\n")]
    public void RemoveValue_LastOrNumbered_SaysNothingOfNumbers(string source)
    {
        Write("Status.cs", source);

        Assert.IsTrue(Run("remove value Closed from Status"), LastOutput);

        Assert.DoesNotContain("numbers one lower", LastOutput);
    }

    [TestMethod]
    public void RemoveValue_RemovesIt()
    {
        Write("Status.cs", StatusSource);

        Assert.IsTrue(Run("remove value Pending from Status"), LastOutput);

        var code = Read("Status.cs");
        Assert.DoesNotContain("Pending", code);
        StringAssert.Contains(code, "Closed");
        Assert.IsEmpty(ProjectErrors(), string.Join(Environment.NewLine, ProjectErrors()));
    }

    [TestMethod]
    public void RemoveValue_FromTheMiddle_KeepsTheOthers()
    {
        Write("Status.cs", StatusSource);

        Assert.IsTrue(Run("delete value Closed from Status"), LastOutput);

        var code = Read("Status.cs");
        Assert.DoesNotContain("Closed", code);
        AssertValuesInOrder(code, "Active,", "Pending");
        Assert.IsEmpty(ProjectErrors(), string.Join(Environment.NewLine, ProjectErrors()));
    }

    [TestMethod]
    public void RemoveValue_StillUsedElsewhere_WarnsWhichCodeUsesIt()
    {
        Write("Status.cs", StatusSource);
        Write("Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public bool IsOpen() => Status.Pending == Status.Pending;\n}\n");

        Assert.IsTrue(Run("remove value Pending from Status"), LastOutput);

        StringAssert.Contains(LastOutput, "code may still use it");
        StringAssert.Contains(LastOutput, "Order.IsOpen()");
    }

    [TestMethod]
    public void RemoveValue_TypedInAnotherCase_WarnsOfUsesOfTheDeclaredName()
    {
        Write("Status.cs", StatusSource);
        Write("Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public bool IsOpen() => Status.Pending == Status.Pending;\n}\n");

        Assert.IsTrue(Run("remove value pending from Status"), LastOutput);

        Assert.DoesNotContain("Pending", Read("Status.cs"));
        StringAssert.Contains(LastOutput, "code may still use it");
        StringAssert.Contains(LastOutput, "Order.IsOpen()");
    }

    [TestMethod]
    public void AddValue_ToAFlagsEnum_IsRefused()
    {
        var source = "namespace Shop;\n\n[System.Flags]\npublic enum Access { Read = 1, Write = 2 }\n";
        Write("Access.cs", source);

        Assert.IsFalse(Run("add value Run to Access"));

        StringAssert.Contains(LastOutput, "is a [Flags] enum");
        Assert.AreEqual(source, Read("Access.cs"));
    }

    [TestMethod]
    [DataRow("add value Active to Status", "already")]
    [DataRow("add value class to Status", "'class' is not a valid enum value name")]
    [DataRow("remove value Ghost from Status", "'Ghost' is not a value of 'Status'")]
    [DataRow("add value X to Ghost", "Enum 'Ghost' not found.")]
    [DataRow("add value X to Order", "'Order' is not an enum")]
    [DataRow("remove value X from Order", "'Order' is not an enum")]
    [DataRow("add property Name string to Status", "'Status' is an enum; use add value <name> to Status.")]
    [DataRow("add method Save to Status", "'Status' is an enum; use add value <name> to Status.")]
    [DataRow("add validation required on Name in Status", "'Status' is an enum; use add value <name> to Status.")]
    public void EnumEdit_Refused_LeavesTheFilesAsTheyAre(string input, string reason)
    {
        Write("Status.cs", StatusSource);
        Write("Order.cs", OrderSource);

        Assert.IsFalse(Run(input));

        StringAssert.Contains(LastOutput, reason);
        Assert.AreEqual(StatusSource, Read("Status.cs"));
        Assert.AreEqual(OrderSource, Read("Order.cs"));
    }

    [TestMethod]
    public void TwoValueCommandsOnOneLine_AreRefusedWhole()
    {
        Write("Status.cs", StatusSource);

        Assert.IsFalse(Run("add value Archived to Status and add value Late to Status"));

        Assert.AreEqual(StatusSource, Read("Status.cs"));
    }

    [TestMethod]
    public void CreateClassAndAddMethod_IsStillOneCommand()
    {
        Assert.IsNull(CommandFront.TwoCommands("create class Employee and add method AddEmployee"));
    }

    [TestMethod]
    [DataRow("A, B and C", new[] { "A", "B", "C" })]
    [DataRow("A,B", new[] { "A", "B" })]
    [DataRow("A, B, and C", new[] { "A", "B", "C" })]
    [DataRow("", new string[0])]
    public void EnumValueParser_SplitsOnCommasAndAnd(string text, string[] expected)
    {
        Assert.IsTrue(EnumValueParser.TryParse(text, out var values, out var error), error);
        CollectionAssert.AreEqual(expected, values);
    }
}
