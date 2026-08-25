using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// What <see cref="EntityExtractor.ExtractProperties"/> counts as the data a type declares.
/// </summary>
/// <remarks>
/// This drives three things at once — <c>list properties</c>, <c>generate validator</c>, and the
/// chat page's project tree — so what it does and does not pick up is worth pinning. It was found
/// by the tree: a positional record drew its methods and none of its parameters, with the source
/// on screen beside it saying otherwise.
/// </remarks>
[TestClass]
public class EntityExtractorPropertyTests
{
    [TestMethod]
    public void An_auto_property_is_data()
    {
        var found = Extract(
            """
            public class Customer
            {
                public int Id { get; set; }
                public string? Name { get; set; }
            }
            """);

        CollectionAssert.AreEqual(new List<string> { "Id", "Name" }, found.Select(p => p.Name).ToList());
        CollectionAssert.AreEqual(new List<string> { "int", "string?" }, found.Select(p => p.Type).ToList());
    }

    /// <summary>The gap the tree exposed.</summary>
    [TestMethod]
    public void The_parameters_of_a_positional_record_are_data()
    {
        var found = Extract(
            """
            public sealed record AppCustomization(
                string Company,
                AppTheme Theme,
                string? Accent);
            """);

        CollectionAssert.AreEqual(
            new List<string> { "Company", "Theme", "Accent" }, found.Select(p => p.Name).ToList());

        CollectionAssert.AreEqual(
            new List<string> { "string", "AppTheme", "string?" }, found.Select(p => p.Type).ToList());
    }

    [TestMethod]
    public void A_record_declares_both_its_parameters_and_its_properties()
    {
        var found = Extract(
            """
            public sealed record Invoice(int Number)
            {
                public decimal Total { get; init; }
            }
            """);

        CollectionAssert.AreEqual(
            new List<string> { "Number", "Total" }, found.Select(p => p.Name).ToList());
    }

    /// <summary>Missed by the regex for the same reason records were.</summary>
    [TestMethod]
    public void An_init_only_and_a_get_only_property_are_data()
    {
        var found = Extract(
            """
            public class Customer
            {
                public int Id { get; init; }
                public string Name { get; }
            }
            """);

        CollectionAssert.AreEqual(new List<string> { "Id", "Name" }, found.Select(p => p.Name).ToList());
    }

    /// <summary>
    /// Computed members are not data.
    /// </summary>
    /// <remarks>
    /// This is the line that keeps <c>generate validator</c> honest: a rule written against
    /// something with nothing to set is a rule that can never be satisfied by the caller.
    /// </remarks>
    [TestMethod]
    public void A_computed_property_is_not_data()
    {
        var found = Extract(
            """
            public class Customer
            {
                public string First { get; set; }
                public string Last { get; set; }

                public string Full => First + " " + Last;

                public string Shouty { get { return Full.ToUpper(); } }
            }
            """);

        CollectionAssert.AreEqual(
            new List<string> { "First", "Last" }, found.Select(p => p.Name).ToList());
    }

    /// <summary>
    /// A static member belongs to the type, not to an instance of it.
    /// </summary>
    /// <remarks>
    /// Found by looking at the running tree: <c>AppCustomization</c>'s
    /// <c>public static AppCustomization Default</c> was listed among the record's own fields.
    /// The old regex missed it by accident, because <c>static</c> broke its `public T X` shape.
    /// </remarks>
    [TestMethod]
    public void A_static_property_is_not_data()
    {
        var found = Extract(
            """
            public sealed record AppCustomization(string Company)
            {
                public static AppCustomization Default { get; } = new("Acme");
            }
            """);

        CollectionAssert.AreEqual(new List<string> { "Company" }, found.Select(p => p.Name).ToList());
    }

    [TestMethod]
    public void A_non_public_property_is_not_data()
    {
        var found = Extract(
            """
            public class Customer
            {
                public int Id { get; set; }
                private string Secret { get; set; }
                internal string Hidden { get; set; }
            }
            """);

        CollectionAssert.AreEqual(new List<string> { "Id" }, found.Select(p => p.Name).ToList());
    }

    [TestMethod]
    public void A_nested_type_contributes_its_own_properties_once()
    {
        var found = Extract(
            """
            public class Outer
            {
                public int OuterId { get; set; }

                public class Inner
                {
                    public int InnerId { get; set; }
                }
            }
            """);

        CollectionAssert.AreEqual(
            new List<string> { "OuterId", "InnerId" }, found.Select(p => p.Name).ToList());
    }

    [TestMethod]
    public void A_file_that_is_not_there_yields_nothing()
        => Assert.IsEmpty(EntityExtractor.ExtractProperties(
            Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N") + ".cs")));

    private static List<CodeGenerationEngine.Models.PropertyDefinition> Extract(string source)
    {
        var file = Path.Combine(
            Path.GetTempPath(), "extract-" + Guid.NewGuid().ToString("N")[..8] + ".cs");

        File.WriteAllText(file, source);

        try
        {
            return EntityExtractor.ExtractProperties(file);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
