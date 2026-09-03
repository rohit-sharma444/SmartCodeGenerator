using DomainDiscovery.Llm;
using Xunit;

namespace DomainDiscovery.Tests.Llm;

/// <summary>
/// The salvage path, which is what turns a cut-off generation from nothing into most of an answer.
/// </summary>
/// <remarks>
/// Measured against the real server before being written: a generation stopped at 60% of its
/// length had five complete, valid classes in the bytes already received, and the old
/// non-streaming client threw all five away because the document as a whole did not parse.
/// </remarks>
public class PartialLlmJsonTests
{
    private const string Complete = """
        {"classes":[
          {"name":"Product","role":"Item","purpose":"a thing sold","properties":[{"name":"Id","type":"int"}],"relationships":[]},
          {"name":"Sale","role":"Transaction","purpose":"a sale","properties":[{"name":"Id","type":"int"}],"relationships":[]}
        ]}
        """;

    [Fact]
    public void ReadsEveryClassOutOfACompleteDocument()
    {
        var model = PartialLlmJson.Salvage(Complete);

        Assert.NotNull(model);
        Assert.Equal(["Product", "Sale"], model!.Classes.Select(c => c.Name));
    }

    [Fact]
    public void KeepsTheCompleteClassesWhenTheDocumentStopsMidClass()
    {
        // Exactly the shape a cut-off stream leaves: Product closed, Sale opened and abandoned
        // partway through its own property list.
        var cut = Complete[..(Complete.IndexOf("\"Sale\"", StringComparison.Ordinal) + 20)];

        var model = PartialLlmJson.Salvage(cut);

        Assert.NotNull(model);
        Assert.Equal("Product", Assert.Single(model!.Classes).Name);
    }

    [Fact]
    public void ReturnsNothingWhenNotOneClassIsComplete()
    {
        Assert.Null(PartialLlmJson.Salvage("{\"classes\":[{\"name\":\"Prod"));
    }

    [Fact]
    public void IsNotFooledByABraceInsideAString()
    {
        // A purpose that contains the very character the scan counts. Without string tracking this
        // closes the class early and salvages a half-read object as though it were whole.
        var text = """
            {"classes":[{"name":"Product","role":"Item","purpose":"holds } and { literally","properties":[],"relationships":[]},{"name":"Br
            """;

        var model = PartialLlmJson.Salvage(text);

        Assert.NotNull(model);
        var only = Assert.Single(model!.Classes);
        Assert.Equal("Product", only.Name);
        Assert.Equal("holds } and { literally", only.Purpose);
    }

    [Fact]
    public void IsNotFooledByAnEscapedQuote()
    {
        var text = """
            {"classes":[{"name":"Product","role":"Item","purpose":"a \"boxed\" thing","properties":[],"relationships":[]},{"name":"Br
            """;

        var model = PartialLlmJson.Salvage(text);

        Assert.Equal("a \"boxed\" thing", Assert.Single(model!.Classes).Purpose);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"classes\":[]}")]
    public void ReturnsNothingRatherThanThrowingOnAnythingUnusable(string text)
    {
        Assert.Null(PartialLlmJson.Salvage(text));
    }

    [Fact]
    public void NamesTheClassesItCanSeeSoFar()
    {
        // What the Discover panel draws while the model is still generating.
        var cut = Complete[..(Complete.IndexOf("\"Sale\"", StringComparison.Ordinal) + 20)];

        Assert.Equal(["Product [Item]"], PartialLlmJson.ClassesSoFar(cut));
        Assert.Equal(["Product [Item]", "Sale [Transaction]"], PartialLlmJson.ClassesSoFar(Complete));
        Assert.Empty(PartialLlmJson.ClassesSoFar("{\"classes\":[{\"na"));
    }
}
