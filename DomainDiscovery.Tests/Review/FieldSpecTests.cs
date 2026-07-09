using DomainDiscovery.Review;
using Xunit;

namespace DomainDiscovery.Tests.Review;

public class FieldSpecTests
{
    [Fact]
    public void Parse_WithoutAColon_LeavesTheTypeToInference()
    {
        var spec = FieldSpec.Parse("prep time");

        Assert.Equal("prep time", spec.Name);
        Assert.Null(spec.Type);
    }

    [Fact]
    public void Parse_WithAColon_TakesTheExplicitType()
    {
        var spec = FieldSpec.Parse("Servings:int");

        Assert.Equal("Servings", spec.Name);
        Assert.Equal("int", spec.Type);
    }

    [Fact]
    public void Parse_KeepsAMultiWordNameWithAnExplicitType()
    {
        var spec = FieldSpec.Parse("prep time : DateTime");

        Assert.Equal("prep time", spec.Name);
        Assert.Equal("DateTime", spec.Type);
    }

    [Fact]
    public void Parse_AMisspelledType_IsATypeNotPartOfTheName()
    {
        // Guessing from position would silently create a field named "PriceDecimel";
        // as a type, ModelEditor.AddField rejects it instead.
        var spec = FieldSpec.Parse("Price:decimel");

        Assert.Equal("Price", spec.Name);
        Assert.Equal("decimel", spec.Type);
    }

    [Fact]
    public void Parse_ABareTrailingWord_IsPartOfTheName()
    {
        var spec = FieldSpec.Parse("bogus Widget");

        Assert.Equal("bogus Widget", spec.Name);
        Assert.Null(spec.Type);
    }
}
