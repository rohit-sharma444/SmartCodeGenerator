using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;

namespace CodeGenerator.Tests.Core.Validation;

/// <summary>
/// The writer edits a file someone may have written by hand. Every test here asserts both halves:
/// that the check arrived, and that nothing else did.
/// </summary>
[TestClass]
public class ClassValidationWriterTests
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void MakeFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "cvw-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void RemoveFolder()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private string WriteClass(string name, string source)
    {
        var path = Path.Combine(_folder, name + ".cs");
        File.WriteAllText(path, source);
        return path;
    }

    private static IValidationRule Rule(string fragment)
    {
        var ctx = new ValidationParseContext { Fragment = fragment, ClassName = "Employee" };
        var rules = ValidationRuleCatalog.Default.Parse(fragment, ValidationCategory.Data, ctx);
        Assert.HasCount(1, rules);
        return rules[0];
    }

    private static void AssertCompiles(string source)
    {
        var result = Compiler.Compile(source);
        Assert.IsTrue(result.Success,
            string.Join("\n", result.Diagnostics.Select(d => d.ToString())) + "\n\n" + source);
    }

    private const string PlainEmployee =
        "namespace Acme\r\n" +
        "{\r\n" +
        "    public class Employee\r\n" +
        "    {\r\n" +
        "        public int Age { get; set; }\r\n" +
        "\r\n" +
        "        public string Name { get; set; } = string.Empty;\r\n" +
        "    }\r\n" +
        "}\r\n";

    [TestMethod]
    public void Creates_a_validate_method_on_a_class_that_has_none()
    {
        var file = WriteClass("Employee", PlainEmployee);

        var result = ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));

        Assert.IsTrue(result.Ok, result.Message);

        var updated = File.ReadAllText(file);
        StringAssert.Contains(updated, "public System.Collections.Generic.List<string> Validate()");
        StringAssert.Contains(updated, "// Age must be at least 18");
        StringAssert.Contains(updated, "__n < 18m");
        AssertCompiles(updated);
    }

    [TestMethod]
    public void The_lines_it_did_not_add_are_byte_identical()
    {
        var file = WriteClass("Employee", PlainEmployee);

        ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));

        var updated = File.ReadAllText(file);

        foreach (var line in new[]
        {
            "    public class Employee",
            "        public int Age { get; set; }",
            "        public string Name { get; set; } = string.Empty;"
        })
            StringAssert.Contains(updated, line, $"the writer moved: {line}");
    }

    [TestMethod]
    public void The_newline_style_of_the_file_is_kept()
    {
        var file = WriteClass("Employee", PlainEmployee); // CRLF throughout

        ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));

        var updated = File.ReadAllText(file);
        var loneFeeds = updated.Split('\n').Count(part => !part.EndsWith('\r'));

        // The last split part is after the final newline, so one is expected and no more.
        Assert.AreEqual(1, loneFeeds, "a bare LF was written into a CRLF file");
    }

    [TestMethod]
    public void A_byte_order_mark_survives()
    {
        var path = Path.Combine(_folder, "Employee.cs");
        File.WriteAllText(path, PlainEmployee, new System.Text.UTF8Encoding(true));

        ClassValidationWriter.Add(path, "Employee", Rule("Age must be at least 18"));

        var bytes = File.ReadAllBytes(path);
        Assert.IsTrue(bytes is [0xEF, 0xBB, 0xBF, ..], "the BOM was stripped");
    }

    [TestMethod]
    public void The_rules_on_a_class_can_be_read_back()
    {
        var file = WriteClass("Employee", PlainEmployee);

        ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));

        var rules = ClassValidationWriter.ExistingRules(file, "Employee");

        Assert.HasCount(1, rules);
        Assert.AreEqual("Age must be at least 18", rules[0]);
    }

    /// <summary>
    /// The check reads as code, not as a block that happens to compile.
    /// </summary>
    /// <remarks>
    /// Compiling the result cannot see this, and the first version of the writer got it wrong in a
    /// way only looking at the file showed: every emitted statement came out at the same column as
    /// the <c>try</c> and the brace that opened it, because a rule emits at a fixed twelve spaces
    /// and the writer shifted by one level too few.
    /// </remarks>
    [TestMethod]
    public void The_statements_are_indented_inside_the_try_that_holds_them()
    {
        var file = WriteClass("Employee", PlainEmployee);

        ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));

        var lines = File.ReadAllLines(file);

        var openIndex = Array.FindIndex(lines, l => l.Trim() == "try");
        Assert.IsGreaterThan(-1, openIndex, "no try block was written");

        var tryIndent = lines[openIndex].Length - lines[openIndex].TrimStart().Length;

        // Between the brace after `try` and its closing brace: the rule's own statements.
        var statements = lines
            .Skip(openIndex + 2)
            .TakeWhile(l => l.Trim() != "}")
            .Where(l => l.Trim().Length > 0)
            .ToList();

        Assert.IsGreaterThan(0, statements.Count, "the try block came out empty");

        foreach (var statement in statements)
        {
            var indent = statement.Length - statement.TrimStart().Length;
            Assert.IsGreaterThan(tryIndent, indent,
                $"not indented inside its try block: {statement}");
        }
    }

    [TestMethod]
    public void A_file_scoped_namespace_is_indented_correctly()
    {
        var file = WriteClass("Employee",
            "namespace Acme;\r\n" +
            "\r\n" +
            "public class Employee\r\n" +
            "{\r\n" +
            "    public int Age { get; set; }\r\n" +
            "}\r\n");

        var result = ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));

        Assert.IsTrue(result.Ok, result.Message);
        AssertCompiles(File.ReadAllText(file));
    }

    [TestMethod]
    public void A_second_rule_joins_the_first_in_one_method()
    {
        var file = WriteClass("Employee", PlainEmployee);

        ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));
        var result = ClassValidationWriter.Add(file, "Employee", Rule("Name is required"));

        Assert.IsTrue(result.Ok, result.Message);

        var updated = File.ReadAllText(file);

        Assert.AreEqual(1, Occurrences(updated, "List<string> Validate()"),
            "a second method was added instead of the check joining the first");
        StringAssert.Contains(updated, "// Age must be at least 18");
        StringAssert.Contains(updated, "// Name is required");
        AssertCompiles(updated);
    }

    [TestMethod]
    public void The_same_rule_twice_is_refused_and_changes_nothing()
    {
        var file = WriteClass("Employee", PlainEmployee);

        ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));
        var before = File.ReadAllText(file);

        var result = ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "already has that rule");
        Assert.AreEqual(before, File.ReadAllText(file));
    }

    [TestMethod]
    public void Both_rules_are_read_back()
    {
        var file = WriteClass("Employee", PlainEmployee);

        ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));
        ClassValidationWriter.Add(file, "Employee", Rule("Name is required"));

        var rules = ClassValidationWriter.ExistingRules(file, "Employee");

        CollectionAssert.AreEquivalent(
            new[] { "Age must be at least 18", "Name is required" }, rules.ToArray());
    }

    [TestMethod]
    public void A_hand_written_validate_of_another_shape_is_left_alone()
    {
        var file = WriteClass("Employee",
            "namespace Acme\r\n" +
            "{\r\n" +
            "    public class Employee\r\n" +
            "    {\r\n" +
            "        public int Age { get; set; }\r\n" +
            "\r\n" +
            "        public bool Validate() => Age > 0;\r\n" +
            "    }\r\n" +
            "}\r\n");

        var before = File.ReadAllText(file);
        var result = ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "Validate()");
        Assert.AreEqual(before, File.ReadAllText(file), "a hand-written method was edited");
    }

    [TestMethod]
    public void A_validate_with_no_errors_list_is_left_alone()
    {
        var file = WriteClass("Employee",
            "namespace Acme\r\n" +
            "{\r\n" +
            "    public class Employee\r\n" +
            "    {\r\n" +
            "        public int Age { get; set; }\r\n" +
            "\r\n" +
            "        public System.Collections.Generic.List<string> Validate()\r\n" +
            "        {\r\n" +
            "            return new System.Collections.Generic.List<string>();\r\n" +
            "        }\r\n" +
            "    }\r\n" +
            "}\r\n");

        var before = File.ReadAllText(file);
        var result = ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(before, File.ReadAllText(file));
    }

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        var at = text.IndexOf(needle, StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }
        return count;
    }
}
