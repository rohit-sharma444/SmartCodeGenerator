using NaturalLanguageEngine.Models.Analysis;
using NaturalLanguageEngine.Models.Lexical;
using NaturalLanguageEngine.Models.Semantics;
using NaturalLanguageEngine.Models.Syntax;
using NaturalLanguageEngine.Semantics;
using Xunit;

namespace NaturalLanguageEngine.Tests.Semantics;

public class RelationshipExtractorTests
{
    private readonly RelationshipExtractor _extractor = new();

    private static Entity MakeEntity(string text, EntityType type)
        => new() { Text = text, Type = type };

    /// <summary>
    /// A TokenAnalysis wired to <paramref name="entity"/> the way
    /// EntityRecognizer wires a phrase head to the entity it produced.
    /// </summary>
    private static TokenAnalysis MakeEntityToken(string text, Entity entity)
        => new() { Token = new Token { Text = text }, Entity = entity };

    /// <summary>A TokenAnalysis with no entity, e.g. a determiner or auxiliary.</summary>
    private static TokenAnalysis MakeNonEntityToken(string text)
        => new() { Token = new Token { Text = text } };

    [Fact]
    public void Extract_SubjectActionObjectLocation_ProducesExpectedRelationships()
    {
        var sarah = MakeEntity("Sarah", EntityType.Person);
        var booked = MakeEntity("booked", EntityType.Action);
        var flight = MakeEntity("a flight", EntityType.Object);
        var london = MakeEntity("London", EntityType.Location);

        var relationships = _extractor.Extract(
            new[] { sarah, booked, flight, london },
            Array.Empty<Dependency>());

        Assert.Equal(3, relationships.Count);

        Assert.Equal(sarah, relationships[0].Source);
        Assert.Equal(booked, relationships[0].Target);
        Assert.Equal(RelationshipType.Subject, relationships[0].Type);

        Assert.Equal(booked, relationships[1].Source);
        Assert.Equal(flight, relationships[1].Target);
        Assert.Equal(RelationshipType.Object, relationships[1].Type);

        Assert.Equal(booked, relationships[2].Source);
        Assert.Equal(london, relationships[2].Target);
        Assert.Equal(RelationshipType.Location, relationships[2].Type);
    }

    [Fact]
    public void Extract_NoActionEntity_ReturnsNoRelationships()
    {
        var entities = new[] { MakeEntity("Sarah", EntityType.Person), MakeEntity("car", EntityType.Object) };

        Assert.Empty(_extractor.Extract(entities, Array.Empty<Dependency>()));
    }

    [Fact]
    public void Extract_ActionIsFirstEntity_ProducesNoSubjectRelationship()
    {
        var booked = MakeEntity("booked", EntityType.Action);
        var flight = MakeEntity("a flight", EntityType.Object);

        var relationships = _extractor.Extract(new[] { booked, flight }, Array.Empty<Dependency>());

        Assert.Single(relationships);
        Assert.Equal(RelationshipType.Object, relationships[0].Type);
    }

    [Fact]
    public void Extract_PersonAfterAction_IsClassifiedAsRecipient()
    {
        var send = MakeEntity("send", EntityType.Action);
        var john = MakeEntity("John", EntityType.Person);

        var relationships = _extractor.Extract(new[] { send, john }, Array.Empty<Dependency>());

        var relationship = Assert.Single(relationships);
        Assert.Equal(RelationshipType.Recipient, relationship.Type);
    }

    [Theory]
    [InlineData(EntityType.Date)]
    [InlineData(EntityType.Time)]
    public void Extract_DateOrTimeAfterAction_IsClassifiedAsTime(EntityType type)
    {
        var send = MakeEntity("send", EntityType.Action);
        var when = MakeEntity("Monday", type);

        var relationships = _extractor.Extract(new[] { send, when }, Array.Empty<Dependency>());

        var relationship = Assert.Single(relationships);
        Assert.Equal(RelationshipType.Time, relationship.Type);
    }

    // --- Dependency-driven path ---
    // These exercise Extract when real dependency edges are supplied
    // (as DependencyParsingStage now does), rather than the empty edge
    // set used by every test above (which exercises the positional
    // fallback exclusively).

    [Fact]
    public void Extract_SubjectDependency_SourceIsSubjectEntityNotVerb()
    {
        // Regression test: a Subject *dependency* edge points governor
        // (verb) -> dependent (subject), but the resulting *Relationship*
        // must read the other way (Source = subject, Target = verb) to
        // match the convention every positional-fallback test above
        // already relies on.
        var sarahEntity = MakeEntity("Sarah", EntityType.Person);
        var bookedEntity = MakeEntity("booked", EntityType.Action);
        var sarahToken = MakeEntityToken("Sarah", sarahEntity);
        var bookedToken = MakeEntityToken("booked", bookedEntity);

        var dependency = new Dependency { Governor = bookedToken, Dependent = sarahToken, Type = DependencyType.Subject };

        var relationships = _extractor.Extract(
            new[] { sarahEntity, bookedEntity },
            new[] { dependency });

        var relationship = Assert.Single(relationships);
        Assert.Equal(sarahEntity, relationship.Source);
        Assert.Equal(bookedEntity, relationship.Target);
        Assert.Equal(RelationshipType.Subject, relationship.Type);
    }

    [Fact]
    public void Extract_IndirectObjectDependency_LocationEntity_IsClassifiedAsLocationNotRecipient()
    {
        // An IndirectObject dependency edge doesn't automatically mean
        // "Recipient" - a Location entity in that position must still
        // come out as Location, matching what the positional fallback
        // would have produced for the same entity type.
        var bookedEntity = MakeEntity("booked", EntityType.Action);
        var londonEntity = MakeEntity("London", EntityType.Location);
        var bookedToken = MakeEntityToken("booked", bookedEntity);
        var londonToken = MakeEntityToken("London", londonEntity);

        var dependency = new Dependency { Governor = bookedToken, Dependent = londonToken, Type = DependencyType.IndirectObject };

        var relationships = _extractor.Extract(
            new[] { bookedEntity, londonEntity },
            new[] { dependency });

        var relationship = Assert.Single(relationships);
        Assert.Equal(bookedEntity, relationship.Source);
        Assert.Equal(londonEntity, relationship.Target);
        Assert.Equal(RelationshipType.Location, relationship.Type);
    }

    [Fact]
    public void Extract_DirectObjectDependency_PlainObjectEntity_IsClassifiedAsObject()
    {
        var bookedEntity = MakeEntity("booked", EntityType.Action);
        var flightEntity = MakeEntity("a flight", EntityType.Object);
        var bookedToken = MakeEntityToken("booked", bookedEntity);
        var flightToken = MakeEntityToken("flight", flightEntity);

        var dependency = new Dependency { Governor = bookedToken, Dependent = flightToken, Type = DependencyType.DirectObject };

        var relationships = _extractor.Extract(
            new[] { bookedEntity, flightEntity },
            new[] { dependency });

        var relationship = Assert.Single(relationships);
        Assert.Equal(RelationshipType.Object, relationship.Type);
    }

    [Fact]
    public void Extract_RootDependency_SelfLoopProducesNoRelationship()
    {
        var bookedEntity = MakeEntity("booked", EntityType.Action);
        var bookedToken = MakeEntityToken("booked", bookedEntity);

        var dependency = new Dependency { Governor = bookedToken, Dependent = bookedToken, Type = DependencyType.Root };

        var relationships = _extractor.Extract(new[] { bookedEntity }, new[] { dependency });

        Assert.Empty(relationships);
    }

    [Fact]
    public void Extract_ModifierDependencyWithoutEntityOnDependent_IsSkipped()
    {
        // Intra-phrase edges (determiner/adjective -> noun head, auxiliary
        // -> verb head) have no entity on the dependent side, since only
        // phrase heads get their own entity. These must not surface as
        // relationships between two entities.
        var flightEntity = MakeEntity("flight", EntityType.Object);
        var flightToken = MakeEntityToken("flight", flightEntity);
        var determinerToken = MakeNonEntityToken("a");

        var dependency = new Dependency { Governor = flightToken, Dependent = determinerToken, Type = DependencyType.Modifier };

        var relationships = _extractor.Extract(new[] { flightEntity }, new[] { dependency });

        Assert.Empty(relationships);
    }

    [Fact]
    public void Extract_DependenciesPresentButUnusable_FallsBackToPositionalHeuristic()
    {
        // Dependencies is non-empty, but none of the edges resolve to an
        // entity-level relationship (both sides lack an Entity) - Extract
        // should still fall back to the positional heuristic rather than
        // returning nothing.
        var sarah = MakeEntity("Sarah", EntityType.Person);
        var booked = MakeEntity("booked", EntityType.Action);
        var uselessDependency = new Dependency
        {
            Governor = MakeNonEntityToken("the"),
            Dependent = MakeNonEntityToken("a"),
            Type = DependencyType.Modifier
        };

        var relationships = _extractor.Extract(new[] { sarah, booked }, new[] { uselessDependency });

        var relationship = Assert.Single(relationships);
        Assert.Equal(RelationshipType.Subject, relationship.Type);
    }
}
