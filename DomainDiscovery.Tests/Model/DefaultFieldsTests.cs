using DomainDiscovery.Model;
using Xunit;

namespace DomainDiscovery.Tests.Model;

public class DefaultFieldsTests
{
    [Fact]
    public void ApplyTo_AddsTheNameField_WhenNoneExists()
    {
        var entity = new CandidateEntity { Name = "Widget" };

        DefaultFields.ApplyTo(entity, Provenance.Seed);

        Assert.Contains(entity.Fields, f => f.Name == DefaultFields.NameField);
    }

    [Fact]
    public void ApplyTo_DoesNotDuplicateAnExistingNameField()
    {
        var entity = new CandidateEntity { Name = "Widget" };
        entity.Fields.Add(new CandidateField
        {
            Name = DefaultFields.NameField, Type = "string", Provenance = Provenance.Seed
        });

        DefaultFields.ApplyTo(entity, Provenance.Seed);

        Assert.Single(entity.Fields, f => f.Name == DefaultFields.NameField);
    }

    [Fact]
    public void ApplyTo_AnEntityCalledName_GetsNoNameField()
    {
        // EntityShaper calls this directly, bypassing EntityShaper.AddField's guard against a
        // field whose name equals the entity's own name. Without this guard, a seed noun that
        // normalizes to "Name" would emit `class Name { public string Name { get; set; } }`,
        // which is CS0542 and does not compile.
        var entity = new CandidateEntity { Name = "Name" };

        DefaultFields.ApplyTo(entity, Provenance.Seed);

        Assert.Empty(entity.Fields);
    }
}
