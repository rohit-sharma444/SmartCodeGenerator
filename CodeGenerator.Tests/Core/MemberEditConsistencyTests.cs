using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// The member edits agree with each other: a name typed in another case is found the way
/// <c>remove property</c> already found it, a member in another partial file of the class is found,
/// and a retype keeps what still fits and refuses what would not compile.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MemberEditConsistencyTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_memberedit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionContext.Clear();
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private bool Run(string input)
    {
        ProjectMetaData? project = null;
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

    private string LastOutput { get; set; } = string.Empty;

    private string Write(string name, string body)
    {
        var file = Path.Combine(_workspace, name);
        File.WriteAllText(file, body);
        return file;
    }

    private void AssertWorkspaceCompiles()
    {
        var sources = Directory.GetFiles(_workspace, "*.cs").Select(f => (f, File.ReadAllText(f))).ToList();
        var result = Compiler.CompileSources(sources);
        Assert.IsTrue(result.Success,
            "Edited code does not compile:" + Environment.NewLine
            + string.Join(Environment.NewLine, sources.Select(s => s.Item2))
            + Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
    }

    // --- case ---

    [TestMethod]
    public void Retype_finds_a_property_typed_in_another_case()
    {
        var file = Write("Order.cs", "namespace Shop { public class Order { public string Qty { get; set; } } }");

        Assert.IsTrue(Run("change type of qty to int in order"), LastOutput);

        StringAssert.Contains(File.ReadAllText(file), "public int Qty");
        AssertWorkspaceCompiles();
    }

    [TestMethod]
    public void Rename_property_finds_a_property_typed_in_another_case()
    {
        var file = Write("Order.cs", "namespace Shop { public class Order { public int Qty { get; set; } } }");

        Assert.IsTrue(Run("rename property qty to Count in Order"), LastOutput);

        StringAssert.Contains(File.ReadAllText(file), "public int Count");
        AssertWorkspaceCompiles();
    }

    [TestMethod]
    public void Method_edits_find_a_method_typed_in_another_case()
    {
        var file = Write("Order.cs",
            "namespace Shop { public class Order { public void Save() { } public void Load() { } } }");

        Assert.IsTrue(Run("rename method save to Store in Order"), LastOutput);
        Assert.IsTrue(Run("remove method load from Order"), LastOutput);

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "void Store()");
        Assert.DoesNotContain("Load", code, code);
        AssertWorkspaceCompiles();
    }

    // --- partial files ---

    private void PartialOrder()
    {
        Write("Order.cs", "namespace Shop { public partial class Order { public int Qty { get; set; } } }");
        Write("Order.Logic.cs",
            "namespace Shop { public partial class Order { public string Note { get; set; } = \"\";\n"
            + "  public void Save() { } public void Use() { } } }");
    }

    [TestMethod]
    public void Remove_method_finds_it_in_another_partial_file()
    {
        PartialOrder();

        Assert.IsTrue(Run("remove method Save from Order"), LastOutput);

        Assert.DoesNotContain("Save", File.ReadAllText(Path.Combine(_workspace, "Order.Logic.cs")));
        AssertWorkspaceCompiles();
    }

    [TestMethod]
    public void Rename_method_finds_it_in_another_partial_file()
    {
        PartialOrder();

        Assert.IsTrue(Run("rename method Save to Store in Order"), LastOutput);

        StringAssert.Contains(File.ReadAllText(Path.Combine(_workspace, "Order.Logic.cs")), "void Store()");
        AssertWorkspaceCompiles();
    }

    [TestMethod]
    public void Rename_method_refuses_a_name_another_partial_file_holds()
    {
        PartialOrder();
        var before = File.ReadAllText(Path.Combine(_workspace, "Order.Logic.cs"));

        Assert.IsFalse(Run("rename method Save to Qty in Order"), LastOutput);

        StringAssert.Contains(LastOutput, "already has a property named 'Qty'", LastOutput);
        Assert.AreEqual(before, File.ReadAllText(Path.Combine(_workspace, "Order.Logic.cs")));
    }

    [TestMethod]
    public void Property_edits_find_it_in_another_partial_file()
    {
        PartialOrder();

        Assert.IsTrue(Run("change type of Note to int in Order"), LastOutput);
        Assert.IsTrue(Run("rename property Note to Memo in Order"), LastOutput);
        AssertWorkspaceCompiles();
        Assert.IsTrue(Run("remove property Memo from Order"), LastOutput);

        Assert.DoesNotContain("Memo", File.ReadAllText(Path.Combine(_workspace, "Order.Logic.cs")));
        AssertWorkspaceCompiles();
    }

    // --- retype details ---

    [TestMethod]
    public void Retype_keeps_an_initial_value_the_new_type_still_holds()
    {
        var file = Write("Order.cs", "namespace Shop { public class Order { public int Qty { get; set; } = 1; } }");

        Assert.IsTrue(Run("change type of Qty to long in Order"), LastOutput);

        StringAssert.Contains(File.ReadAllText(file), "public long Qty { get; set; } = 1;");
        Assert.DoesNotContain("removed", LastOutput, LastOutput);
        AssertWorkspaceCompiles();
    }

    [TestMethod]
    public void Retype_to_a_number_drops_a_string_length_attribute_and_keeps_the_rest()
    {
        var file = Write("Order.cs",
            "using System.ComponentModel.DataAnnotations;\r\n"
            + "namespace Shop\r\n{\r\n    public class Order\r\n    {\r\n"
            + "        /// <summary>How many.</summary>\r\n"
            + "        [StringLength(50)]\r\n"
            + "        [Required, MaxLength(10)]\r\n"
            + "        public string Qty { get; set; }\r\n"
            + "    }\r\n}\r\n");

        Assert.IsTrue(Run("change type of Qty to int in Order"), LastOutput);

        var code = File.ReadAllText(file);
        Assert.DoesNotContain("StringLength", code, code);
        Assert.DoesNotContain("MaxLength", code, code);
        StringAssert.Contains(code, "        /// <summary>How many.</summary>\r\n        [Required]\r\n        public int Qty", code);
        StringAssert.Contains(LastOutput, "StringLength", LastOutput);
        AssertWorkspaceCompiles();
    }

    [TestMethod]
    public void Retype_to_a_list_keeps_a_max_length()
    {
        var file = Write("Order.cs",
            "using System.Collections.Generic; using System.ComponentModel.DataAnnotations;\n"
            + "namespace Shop { public class Order { [MaxLength(5)] public string[] Tags { get; set; } } }");

        Assert.IsTrue(Run("change type of Tags to List<string> in Order"), LastOutput);

        StringAssert.Contains(File.ReadAllText(file), "[MaxLength(5)]");
        AssertWorkspaceCompiles();
    }

    [TestMethod]
    public void Retype_refuses_a_computed_property_whose_value_no_longer_fits()
    {
        var file = Write("Order.cs",
            "namespace Shop { public class Order { public int A { get; set; } public int B { get; set; }\n"
            + "  public int Total => A + B; } }");
        var before = File.ReadAllText(file);

        Assert.IsFalse(Run("change type of Total to string in Order"), LastOutput);

        Assert.AreEqual(before, File.ReadAllText(file));
        StringAssert.Contains(LastOutput, "Total", LastOutput);
    }

    [TestMethod]
    public void Retype_widens_a_computed_property_when_its_value_still_fits()
    {
        var file = Write("Order.cs",
            "namespace Shop { public class Order { public int A { get; set; } public int B { get; set; }\n"
            + "  public int Total => A + B; } }");

        Assert.IsTrue(Run("change type of Total to long in Order"), LastOutput);

        StringAssert.Contains(File.ReadAllText(file), "public long Total => A + B;");
        AssertWorkspaceCompiles();
    }
}
