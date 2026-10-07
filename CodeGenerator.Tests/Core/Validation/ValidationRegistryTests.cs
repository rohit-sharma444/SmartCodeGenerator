using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;

namespace CodeGenerator.Tests.Core.Validation;

[TestClass]
[DoNotParallelize]
public class ValidationRegistryTests
{
    private sealed class FakeRule : IValidationRule
    {
        public ValidationCategory Category { get; init; }
        public IReadOnlyList<string> Properties => [Category.ToString()];
        public string Describe() => Category.ToString();
        public void Emit(ValidationEmitContext ctx) { }
    }

    [TestInitialize]
    public void Reset() => ValidationRegistry.Clear();

    [TestMethod]
    public void Get_by_category_filters_stored_rules()
    {
        ValidationRegistry.Add("Order", new FakeRule { Category = ValidationCategory.Data });
        ValidationRegistry.Add("Order", new FakeRule { Category = ValidationCategory.Business });

        Assert.HasCount(2, ValidationRegistry.Get("Order"));
        Assert.HasCount(1, ValidationRegistry.Get("Order", ValidationCategory.Data));
        Assert.HasCount(1, ValidationRegistry.Get("Order", ValidationCategory.Business));
    }

    [TestMethod]
    public void Get_is_case_insensitive_and_empty_for_unknown_class()
    {
        ValidationRegistry.Add("Order", new FakeRule { Category = ValidationCategory.Data });
        Assert.HasCount(1, ValidationRegistry.Get("order"));
        Assert.IsEmpty(ValidationRegistry.Get("Missing"));
    }

    [TestMethod]
    public void A_snapshot_restores_the_rules_as_they_were()
    {
        ValidationRegistry.Add("Order", new FakeRule { Category = ValidationCategory.Data });
        var snapshot = ValidationRegistry.Snapshot();

        ValidationRegistry.Add("Order", new FakeRule { Category = ValidationCategory.Business });
        ValidationRegistry.Add("Invoice", new FakeRule { Category = ValidationCategory.Data });
        ValidationRegistry.Restore(snapshot);

        Assert.HasCount(1, ValidationRegistry.Get("Order"));
        Assert.IsEmpty(ValidationRegistry.Get("Invoice"));
    }

    [TestMethod]
    public void Rename_class_moves_its_rules_to_the_new_name()
    {
        ValidationRegistry.Add("Order", new FakeRule { Category = ValidationCategory.Data });
        ValidationRegistry.Add("Order", new FakeRule { Category = ValidationCategory.Business });

        var moved = ValidationRegistry.RenameClass("Order", "Purchase");

        Assert.AreEqual(2, moved);
        Assert.IsEmpty(ValidationRegistry.Get("Order"));
        Assert.HasCount(2, ValidationRegistry.Get("Purchase"));
    }

    [TestMethod]
    public void Remove_class_drops_its_rules_and_leaves_the_others()
    {
        ValidationRegistry.Add("Order", new FakeRule { Category = ValidationCategory.Data });
        ValidationRegistry.Add("Invoice", new FakeRule { Category = ValidationCategory.Data });

        var removed = ValidationRegistry.RemoveClass("order");

        Assert.AreEqual(1, removed);
        Assert.IsEmpty(ValidationRegistry.Get("Order"));
        Assert.HasCount(1, ValidationRegistry.Get("Invoice"));
    }

    [TestMethod]
    public void A_snapshot_is_not_changed_by_later_adds()
    {
        var snapshot = ValidationRegistry.Snapshot();
        ValidationRegistry.Add("Order", new FakeRule { Category = ValidationCategory.Data });

        Assert.IsEmpty(snapshot);
    }

    // ----- saved in the workspace -------------------------------------------------------------

    private static string NewWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "vr_" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        return root;
    }

    private static void AddParsed(string className, string text, ValidationCategory category)
    {
        var rule = ValidationRuleCatalog.Default.ParseOne(
            text, category, new ValidationParseContext { Fragment = text, ClassName = className }).Single();
        ValidationRegistry.Add(className, rule, text);
    }

    private static List<string> Described(string className) =>
        ValidationRegistry.Get(className).Select(r => r.Describe()).ToList();

    [TestMethod]
    public void Rules_added_in_one_session_are_there_in_the_next()
    {
        var root = NewWorkspace();
        try
        {
            ValidationRegistry.Attach(root);
            AddParsed("Employee", "Name is required", ValidationCategory.Data);
            AddParsed("Employee", "Age must be between 18 and 65", ValidationCategory.Data);
            AddParsed("Employee", "EndDate must be after StartDate", ValidationCategory.Business);
            var before = Described("Employee");

            // A restart: the process's memory is gone; only the workspace is left.
            ValidationRegistry.Clear();
            Assert.IsEmpty(ValidationRegistry.Get("Employee"));
            var load = ValidationRegistry.Attach(root);

            Assert.IsNull(load.Problem);
            CollectionAssert.AreEqual(before, Described("Employee"));
            Assert.HasCount(1, ValidationRegistry.Get("Employee", ValidationCategory.Business));
            Assert.IsTrue(File.Exists(ValidationRuleFile.PathFor(root)));
        }
        finally
        {
            ValidationRegistry.Clear();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Remove_rename_and_class_removal_are_saved()
    {
        var root = NewWorkspace();
        try
        {
            ValidationRegistry.Attach(root);
            AddParsed("Order", "Name is required", ValidationCategory.Data);
            AddParsed("Order", "Code is required", ValidationCategory.Data);
            AddParsed("Invoice", "Total is required", ValidationCategory.Data);

            ValidationRegistry.RemoveNumber("Order", ValidationCategory.Data, 1);
            ValidationRegistry.RenameClass("Order", "Purchase");
            ValidationRegistry.RemoveClass("Invoice");

            ValidationRegistry.Attach(root);

            Assert.IsEmpty(ValidationRegistry.Get("Order"));
            Assert.IsEmpty(ValidationRegistry.Get("Invoice"));
            CollectionAssert.AreEqual(new[] { "Code is required" }, Described("Purchase"));
        }
        finally
        {
            ValidationRegistry.Clear();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Undoing_an_add_through_a_snapshot_saves_the_file_too()
    {
        var root = NewWorkspace();
        try
        {
            ValidationRegistry.Attach(root);
            AddParsed("Order", "Name is required", ValidationCategory.Data);
            var snapshot = ValidationRegistry.Snapshot();
            AddParsed("Order", "Code is required", ValidationCategory.Data);

            ValidationRegistry.Restore(snapshot);
            ValidationRegistry.Attach(root);

            CollectionAssert.AreEqual(new[] { "Name is required" }, Described("Order"));
        }
        finally
        {
            ValidationRegistry.Clear();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void A_saved_rule_no_parser_reads_is_kept_on_the_next_save()
    {
        var root = NewWorkspace();
        try
        {
            ValidationRuleFile.Write(ValidationRuleFile.PathFor(root),
                [new SavedValidationRule("Order", ValidationCategory.Data, "flibbertigibbet")]);

            var load = ValidationRegistry.Attach(root);
            Assert.HasCount(1, load.Unreadable);

            AddParsed("Order", "Name is required", ValidationCategory.Data);
            var reread = ValidationRuleFile.Read(ValidationRuleFile.PathFor(root));

            Assert.HasCount(1, reread.Rules);
            Assert.AreEqual("flibbertigibbet", reread.Unreadable.Single().Text);
        }
        finally
        {
            ValidationRegistry.Clear();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Removing_the_last_rule_deletes_the_file()
    {
        var root = NewWorkspace();
        try
        {
            ValidationRegistry.Attach(root);
            AddParsed("Order", "Name is required", ValidationCategory.Data);
            ValidationRegistry.RemoveOn("Order", ValidationCategory.Data, "Name");

            Assert.IsFalse(File.Exists(ValidationRuleFile.PathFor(root)));
        }
        finally
        {
            ValidationRegistry.Clear();
            Directory.Delete(root, recursive: true);
        }
    }
}
