using DomainDiscovery.Model;
using Xunit;

namespace DomainDiscovery.Tests.Model;

public class CandidateDomainModelTests
{
    [Fact]
    public void NewEntity_HasEmptyFieldAndRelationshipLists()
    {
        var entity = new CandidateEntity { Name = "Book" };

        Assert.Empty(entity.Fields);
        Assert.Empty(entity.Relationships);
    }

    [Fact]
    public void TopicResult_None_IsNotRecognized()
    {
        Assert.False(TopicResult.None.IsRecognized);
    }

    [Fact]
    public void TopicResult_WithUrl_IsRecognized()
    {
        var topic = new TopicResult("Library", "https://en.wikipedia.org/wiki/Library", 0.8);

        Assert.True(topic.IsRecognized);
    }
}
