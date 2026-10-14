using AppScaffolding.Incremental;

namespace CodeGenerator.Tests.App.Incremental;

/// <summary>
/// Putting an add's lines into a file the user has edited, and only those lines.
/// </summary>
[TestClass]
public class LineMergeTests
{
    private const string Before =
        "var app = builder.Build();\n" +
        "\n" +
        "MapEntity<Book>(app, \"book\");\n" +
        "MapEntity<Member>(app, \"member\");\n" +
        "\n" +
        "app.Run();\n";

    private const string After =
        "var app = builder.Build();\n" +
        "\n" +
        "MapEntity<Book>(app, \"book\");\n" +
        "MapEntity<Member>(app, \"member\");\n" +
        "MapEntity<Author>(app, \"author\");\n" +
        "\n" +
        "app.Run();\n";

    [TestMethod]
    public void The_new_line_goes_in_and_the_users_line_stays()
    {
        var existing = Before.Replace(
            "MapEntity<Book>", "app.MapGet(\"/ping\", () => \"pong\"); // HAND\nMapEntity<Book>");

        var result = LineMerge.Apply(Before, After, existing);

        Assert.AreEqual(
            existing.Replace(
                "MapEntity<Member>(app, \"member\");\n",
                "MapEntity<Member>(app, \"member\");\nMapEntity<Author>(app, \"author\");\n"),
            result.Merged);
    }

    /// <summary>The file's own line ends are kept, and the new line takes them too.</summary>
    [TestMethod]
    public void A_crlf_file_stays_crlf()
    {
        var existing = Before.Replace("\n", "\r\n");

        var merged = LineMerge.Apply(Before, After, existing).Merged;

        Assert.AreEqual(After.Replace("\n", "\r\n"), merged);
    }

    /// <summary>A changed line - the masters list growing a name - replaces the old one where the
    /// old one still stands.</summary>
    [TestMethod]
    public void A_changed_line_is_replaced()
    {
        const string before = "a\nb\nMasters = { \"Book\" };\nc\nd\n";
        const string after = "a\nb\nMasters = { \"Book\", \"Author\" };\nc\nd\n";

        var merged = LineMerge.Apply(before, after, "// HAND\n" + before).Merged;

        Assert.AreEqual("// HAND\n" + after, merged);
    }

    /// <summary>When the lines around the change were edited away, nothing is guessed.</summary>
    [TestMethod]
    public void A_change_with_no_certain_place_is_not_made()
    {
        const string existing = "var app = builder.Build();\nRegisterEverything(app);\napp.Run();\n";

        var result = LineMerge.Apply(Before, After, existing);

        Assert.IsNull(result.Merged);
        Assert.HasCount(1, result.Changes);
        CollectionAssert.AreEqual(new[] { "MapEntity<Author>(app, \"author\");" }, result.Changes[0].Added.ToArray());
    }

    /// <summary>Two equally good places is no place: a line in the wrong one compiles as often as
    /// not.</summary>
    [TestMethod]
    public void A_change_with_two_equal_places_is_not_made()
    {
        const string before = "x\n}\n}\n";
        const string after = "x\n}\nNEW\n}\n";
        const string existing = "y\n}\n}\nz\n}\n}\n";

        Assert.IsNull(LineMerge.Apply(before, after, existing).Merged);
    }
}
