using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// Member names in <c>create class ... with properties ...</c> and <c>add property ...</c> are
/// the names the user typed.
/// </summary>
/// <remarks>
/// Both read the normalised sentence, which rewrites new -> create, manager -> service and
/// entity -> class and drops stop words and punctuation: a property typed New was written as
/// Create, and "add property Y string to A" lost the class A as an article and then reported
/// "Property '' already exists."
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class MemberListParseTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_memberlist_" + Guid.NewGuid().ToString("N"));
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
        return CommandFront.Run(input, ref project, [], _workspace, _ => { });
    }

    private string Source(string className) =>
        File.ReadAllText(Directory.GetFiles(_workspace, className + ".cs", SearchOption.AllDirectories).Single());

    private static void AssertCompiles(string code)
    {
        var result = Compiler.Compile(code);
        Assert.IsTrue(result.Success,
            "File does not compile:" + Environment.NewLine
            + string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
    }

    private static string[] Names(ParsedCommand command) => [.. command.Properties.Select(p => p.Name)];

    // ----- create class with properties -----------------------------------

    [TestMethod]
    [DataRow("create class Foo with properties New string, Delete bool", "New,Delete")]
    [DataRow("create class Bar with properties new string, manager string, entity int", "New,Manager,Entity")]
    [DataRow("create class Baz with properties model string, field int", "Model,Field")]
    [DataRow("create class Qux with properties name string, price decimal.", "Name,Price")]
    public void Property_names_are_kept_as_typed(string sentence, string expected)
    {
        var command = CommandFront.Parse(sentence, _workspace);

        Assert.AreEqual(IntentType.CreateClass, command.Intent);
        CollectionAssert.AreEqual(expected.Split(','), Names(command), sentence);
    }

    // The member list is read as typed, so "functions" is no longer rewritten to "methods" for it.
    [TestMethod]
    [DataRow("create class Shop with properties Name string and methods Save")]
    [DataRow("create class Shop with properties Name string and functions Save")]
    public void Methods_are_read_under_either_label(string sentence)
    {
        var command = CommandFront.Parse(sentence, _workspace);

        CollectionAssert.AreEqual(new[] { "Name" }, Names(command), sentence);
        CollectionAssert.AreEqual(new[] { "Save" }, command.Methods.Select(m => m.Name).ToArray(), sentence);
    }

    [TestMethod]
    public void A_class_with_keyword_like_property_names_compiles()
    {
        Assert.IsTrue(Run("create class Bar with properties new string, manager string, entity int"));

        var code = Source("Bar");
        StringAssert.Contains(code, "public string New");
        StringAssert.Contains(code, "public string Manager");
        StringAssert.Contains(code, "public int Entity");
        AssertCompiles(code);
    }

    // "with property Name string" made the class with Name and still warned that
    // 'property Name string' had been ignored.
    [TestMethod]
    [DataRow("create class Item with property Name string")]
    [DataRow("create class Item with properties Name string")]
    [DataRow("create class Item with field Name string")]
    public void The_singular_property_form_is_read(string sentence)
    {
        var command = CommandFront.Parse(sentence, _workspace);

        CollectionAssert.AreEqual(new[] { "Name" }, Names(command), sentence);
        Assert.IsEmpty(command.IgnoredProperties, sentence);
    }

    // The ignored-entry report splits the list on commas, which the normaliser had removed, so on
    // the real command path nothing was ever reported. A name and a type that is not built in is
    // now a property, as add property takes it (the handler binds the type in the project), and a
    // bare name is given string; an entry that is neither is what is reported.
    [TestMethod]
    public void A_non_primitive_entry_is_read_and_an_unreadable_one_reported_from_the_command_line()
    {
        var command = CommandFront.Parse(
            "create class Order with properties CustomerId int, Customer Customer, Notes, Price 9.99", _workspace);

        CollectionAssert.AreEqual(new[] { "CustomerId", "Customer", "Notes" }, Names(command));
        Assert.AreEqual("Customer", command.Properties[1].Type);
        Assert.AreEqual("string", command.Properties[2].Type);
        CollectionAssert.AreEqual(new[] { "Price 9.99" }, command.IgnoredProperties);
    }

    // The primitive pattern matched `Email string` inside `Email string?` and `Tags string` inside
    // `Tags string[]`, so the class got `string Email` and `string Tags`: the user's `?` and `[]`
    // were lost with no word, while add property kept them.
    [TestMethod]
    [DataRow("create class Customer with properties Name string, Email string?", "Email", "string?")]
    [DataRow("create class Customer with properties Name string, Age int?", "Age", "int?")]
    [DataRow("create class Customer with properties Name string, Tags string[]", "Tags", "string[]")]
    [DataRow("create class Customer with properties Name string, Scores List<int>", "Scores", "List<int>")]
    [DataRow("create class Customer with properties Name string, Joined DateTime?", "Joined", "DateTime?")]
    public void A_nullable_array_or_list_type_is_kept_as_typed(string sentence, string name, string type)
    {
        var command = CommandFront.Parse(sentence, _workspace);

        var property = command.Properties.SingleOrDefault(p => p.Name == name);
        Assert.IsNotNull(property, sentence);
        Assert.AreEqual(type, property.Type, sentence);
        Assert.IsEmpty(command.IgnoredProperties, sentence);
    }

    // `2x int` was reported as ignored, but the pattern had matched `x int` inside it and the
    // class got a property X that nobody asked for.
    [TestMethod]
    public void An_entry_with_an_invalid_name_is_ignored_and_not_half_read()
    {
        var command = CommandFront.Parse(
            "create class Thing with properties Name string, 2x int", _workspace);

        CollectionAssert.AreEqual(new[] { "Name" }, Names(command));
        CollectionAssert.AreEqual(new[] { "2x int" }, command.IgnoredProperties);
    }

    [TestMethod]
    public void The_ignored_message_says_why_for_an_invalid_name()
    {
        var output = Captured(() => Run("create class Thing with properties Name string, 2x int"));

        StringAssert.Contains(output, "Ignored '2x int': '2x' is not a valid property name.");
        Assert.DoesNotContain("X {", Source("Thing"));
    }

    // create class wrote `public string Name { get; set; }` with no initial value, and a net10
    // project (Nullable enable) warned CS8618 for each one; add property writes `= string.Empty`.
    [TestMethod]
    public void A_new_class_has_no_nullable_warnings()
    {
        var created = false;
        var output = Captured(() => created = Run(
            "create class Customer with properties Name string, Email string?, Tags string[], Scores List<int>, Age int"));
        Assert.IsTrue(created, output);

        var code = Source("Customer");
        StringAssert.Contains(code, "public string Name { get; set; } = string.Empty;");
        StringAssert.Contains(code, "public string? Email { get; set; }");
        Assert.DoesNotContain("Email { get; set; } =", code);

        var result = Compiler.Compile("using System.Collections.Generic;\n" + code);
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.IsEmpty(
            result.Diagnostics.Where(d => d.Id == "CS8618").ToList(),
            string.Join(Environment.NewLine, result.Diagnostics));
    }

    // "and methods Save, Load implements IEntity" read the last method as "Load implements IEntity"
    // and dropped it; without the implements clause both methods were made.
    [TestMethod]
    [DataRow("create class Product with properties Name string and methods Save, Load implements IEntity")]
    [DataRow("create class Product with properties Name string and methods Save, Load inherits Item")]
    [DataRow("create class Product with properties Name string and methods Save and Load extends Item")]
    public void A_trailing_implements_or_inherits_clause_keeps_the_last_method(string sentence)
    {
        var command = CommandFront.Parse(sentence, _workspace);

        CollectionAssert.AreEqual(new[] { "Save", "Load" }, command.Methods.Select(m => m.Name).ToArray(), sentence);
    }

    // ----- method parameters --------------------------------------------------

    private static string Signatures(ParsedCommand command) =>
        string.Join(" | ", command.Methods.Select(m =>
            $"{m.ReturnType} {m.Name}({string.Join(", ", m.Parameters.Select(p => $"{p.Type} {p.Name}"))})"));

    [TestMethod]
    [DataRow("create class Order with properties Total decimal and methods Apply(decimal rate, string code), Save",
        "void Apply(decimal rate, string code) | void Save()")]
    [DataRow("create class Order with methods Ship with parameters address string, express bool",
        "void Ship(string address, bool express)")]
    [DataRow("create class Order with methods Ship taking address string, Save",
        "void Ship(string address) | void Save()")]
    [DataRow("create class Order with methods Apply(decimal rate) returns decimal implements IEntity",
        "decimal Apply(decimal rate)")]
    [DataRow("create class Calc with methods Add(int a, int b) returns int and Clear",
        "int Add(int a, int b) | void Clear()")]
    [DataRow("create class Order with methods Lines returns List<string>",
        "List<string> Lines()")]
    public void Create_class_reads_method_parameters(string sentence, string expected)
    {
        var command = CommandFront.Parse(sentence, _workspace);

        Assert.AreEqual(IntentType.CreateClass, command.Intent, sentence);
        Assert.AreEqual(expected, Signatures(command), sentence);
        Assert.IsEmpty(command.IgnoredMethods, sentence);
    }

    // Found by the final review: each lost a method, or made one, without a word.
    [TestMethod]
    [DataRow("create class Calc with methods Add with parameters x int, y int",
        "void Add(int x, int y)")]
    [DataRow("create class Calc with methods Create taking x int",
        "void Create(int x)")]
    [DataRow("create class Order with methods Ship with parameters code string, map Dictionary<string, int>",
        "void Ship(string code, Dictionary<string, int> map)")]
    [DataRow("create class Order with methods Ship with parameters count int, a string",
        "void Ship(int count, string a)")]
    [DataRow("create class Order with methods Ship with parameters address string and Print method",
        "void Ship(string address) | void Print()")]
    [DataRow("create interface Shape with methods Ship with parameters address string and Print method",
        "void Ship(string address) | void Print()")]
    [DataRow("create class Order with methods Ship with parameters address string and express bool",
        "void Ship(string address, bool express)")]
    [DataRow("create class Order with methods Map returns Dictionary<string, int>",
        "Dictionary<string, int> Map()")]
    public void English_parameter_lists_keep_every_method(string sentence, string expected)
    {
        var command = CommandFront.Parse(sentence, _workspace);

        Assert.AreEqual(expected, Signatures(command), sentence);
        Assert.IsEmpty(command.IgnoredMethods, sentence);
    }

    [TestMethod]
    public void A_parameter_is_not_also_a_property()
    {
        var command = CommandFront.Parse(
            "create class Order and add method Ship with parameters address string", _workspace);

        Assert.AreEqual("void Ship(string address)", Signatures(command));
        Assert.IsEmpty(command.Properties, string.Join(", ", command.Properties.Select(p => p.Name)));
    }

    [TestMethod]
    public void A_return_type_with_a_space_is_not_read_as_a_type()
    {
        var command = CommandFront.Parse(
            "create class Order with methods Total returns decimal value", _workspace);

        Assert.IsFalse(command.Methods.Any(m => m.ReturnType.Contains(' ')), Signatures(command));
    }

    [TestMethod]
    public void The_implements_clause_after_a_parameter_list_is_kept()
    {
        var command = CommandFront.Parse(
            "create class Order with methods Apply(decimal rate) returns decimal implements IEntity", _workspace);

        CollectionAssert.AreEqual(new[] { "IEntity" }, command.Interfaces);
    }

    [TestMethod]
    public void Create_interface_reads_method_parameters()
    {
        var command = CommandFront.Parse(
            "create interface Shape with methods Area(decimal scale) returns decimal", _workspace);

        Assert.AreEqual(IntentType.CreateInterface, command.Intent);
        Assert.AreEqual("decimal Area(decimal scale)", Signatures(command));
    }

    [TestMethod]
    public void A_class_with_method_parameters_is_written_and_compiles()
    {
        var created = false;
        var output = Captured(() => created = Run(
            "create class Order with methods Apply(decimal rate, string code) returns decimal"));
        Assert.IsTrue(created, output);

        var code = Source("Order");
        StringAssert.Contains(code, "public decimal Apply(decimal rate, string code)");
        AssertCompiles(code);
    }

    [TestMethod]
    public void A_method_whose_parameters_do_not_parse_is_left_out_and_named()
    {
        var created = false;
        var output = Captured(() => created = Run("create class Order with methods Apply(decimal), Save"));

        Assert.IsTrue(created, output);
        StringAssert.Contains(output, "Ignored method 'Apply(decimal)'");
        var code = Source("Order");
        Assert.DoesNotContain("Apply", code);
        StringAssert.Contains(code, "Save()");
    }

    private static string Captured(Action action)
    {
        var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }

        return output.ToString();
    }

    // ----- add property ----------------------------------------------------

    // Each of these reported "Property '' already exists.": the quote, the article-like class
    // name A, the comma inside the generic type and the dot in System.Double were all lost to the
    // normaliser, so the name pattern matched nothing.
    [TestMethod]
    [DataRow("add property \"Title\" string to Employee", "Title", "string", "Employee")]
    [DataRow("add property Y string to A", "Y", "string", "A")]
    [DataRow("add property Map Dictionary<string, int> to Book", "Map", "Dictionary<string, int>", "Book")]
    [DataRow("add property Ratio System.Double to Book", "Ratio", "System.Double", "Book")]
    [DataRow("add property Notes string? to Book", "Notes", "string?", "Book")]
    [DataRow("add property Tags List<string> to Book.", "Tags", "List<string>", "Book")]
    [DataRow("add property New string to Book", "New", "string", "Book")]
    [DataRow("add a property Title string to Book", "Title", "string", "Book")]
    [DataRow("add property Title to Book", "Title", "string", "Book")]
    [DataRow("add property note string to Circle", "Note", "string", "Circle")]
    [DataRow("add field price decimal to item", "Price", "decimal", "Item")]
    public void Add_property_reads_the_name_type_and_class_as_typed(
        string sentence, string name, string type, string className)
    {
        var command = CommandFront.Parse(sentence, _workspace);

        Assert.AreEqual(IntentType.CreateProperty, command.Intent, sentence);
        Assert.AreEqual(name, command.PropertyName, sentence);
        Assert.AreEqual(type, command.PropertyType, sentence);
        Assert.AreEqual(className, command.ClassName, sentence);
    }

    [TestMethod]
    public void A_generic_typed_property_is_written_and_compiles()
    {
        Assert.IsTrue(Run("create class Book"));
        Assert.IsTrue(Run("add property Map Dictionary<string, int> to Book"));
        Assert.IsTrue(Run("add property Notes string? to Book"));

        var code = Source("Book");
        StringAssert.Contains(code, "public Dictionary<string, int> Map");
        StringAssert.Contains(code, "public string? Notes");
        AssertCompiles("using System.Collections.Generic;\n#nullable enable\n" + code);
    }

    // A name that cannot be a property is refused by name, never as "Property ''".
    [TestMethod]
    [DataRow("add property 9x string to Book", "'9x' is not a valid property name")]
    [DataRow("add property \"\" string to Book", "property name is required")]
    public void An_unusable_property_name_is_refused_clearly(string sentence, string expected)
    {
        Assert.IsTrue(Run("create class Book"));
        var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            Assert.IsFalse(Run(sentence), sentence);
        }
        finally
        {
            Console.SetOut(original);
        }

        StringAssert.Contains(output.ToString(), expected, sentence);
        Assert.DoesNotContain("Property ''", output.ToString(), sentence);
    }
}
