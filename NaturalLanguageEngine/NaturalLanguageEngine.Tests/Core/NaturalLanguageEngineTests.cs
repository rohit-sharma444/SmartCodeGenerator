using NaturalLanguageEngine.Models.Diagnostics;
using NaturalLanguageEngine.Models.Semantics;
using Xunit;

namespace NaturalLanguageEngine.Tests.Core;

/// <summary>
/// Exercises the fully-wired engine (as built by EngineBuilder.BuildDefault,
/// exactly as Program.cs does) through its public API only.
/// </summary>
public class NaturalLanguageEngineTests
{
    private readonly NaturalLanguageEngine.Core.NaturalLanguageEngine _engine =
        EngineBuilder.BuildDefault();

    [Fact]
    public void Analyze_SimpleActiveSentence_RecognizesExpectedEntitiesIntentAndRelationships()
    {
        var result = _engine.Analyze("Sarah booked a flight to London tomorrow.");

        Assert.Equal("BookedIntent", result.Intent?.Name);

        Assert.Contains(result.Entities, e => e.Text == "Sarah" && e.Type == EntityType.Person);
        Assert.Contains(result.Entities, e => e.Text == "booked" && e.Type == EntityType.Action);
        Assert.Contains(result.Entities, e => e.Text == "London" && e.Type == EntityType.Location);

        Assert.Contains(result.Relationships, r => r.Type == RelationshipType.Subject);
        Assert.Contains(result.Relationships, r => r.Type == RelationshipType.Location);

        Assert.True(result.Confidence > 0);
    }

    [Fact]
    public void Analyze_SentenceWithSpecialTokens_RecognizesEmailDateAndTime()
    {
        var result = _engine.Analyze("Email john@example.com about the meeting at 14:30 on 2025-06-30.");

        Assert.Contains(result.Entities, e => e.Type == EntityType.Value && e.Text == "john@example.com");
        Assert.Contains(result.Entities, e => e.Type == EntityType.Time && e.Text == "14:30");
        Assert.Contains(result.Entities, e => e.Type == EntityType.Date && e.Text == "2025-06-30");
    }

    [Fact]
    public void Analyze_MultiSentenceInput_DoesNotThrowAndRecognizesEntitiesFromBothSentences()
    {
        // Regression test for the sentence-splitter wiring fix: entities
        // from a second sentence must still be recognized, not silently
        // dropped or merged incorrectly into the first sentence's parse.
        var result = _engine.Analyze("Sarah booked a flight. Send the report to John on Monday.");

        Assert.Contains(result.Entities, e => e.Text == "Sarah");
        Assert.Contains(result.Entities, e => e.Text == "John");
    }

    [Fact]
    public void Analyze_NormalInput_ProducesNoCriticalOrErrorDiagnostics()
    {
        var result = _engine.Analyze("Sarah booked a flight to London tomorrow.");

        Assert.DoesNotContain(result.Diagnostics, d =>
            d.Severity is DiagnosticSeverity.Critical or DiagnosticSeverity.Error);
    }

    [Fact]
    public void Analyze_SentenceWithRelationships_EmitsSyntacticRelationshipsDiagnostic()
    {
        // Now that a dependency parser is wired into the pipeline, a
        // simple active-voice sentence like this one is resolved via a
        // real dependency parse rather than the positional fallback.
        var result = _engine.Analyze("Sarah booked a flight to London tomorrow.");

        Assert.Contains(result.Diagnostics, d => d.Code == "NLE.Semantics.SyntacticRelationships");
    }

    [Fact]
    public void Analyze_SentenceWithNoVerb_DoesNotThrowAndProducesNoCriticalDiagnostics()
    {
        // No verb phrase for the dependency parser to anchor a clause on -
        // this exercises the "dependency edges exist but don't resolve to
        // any entity-level relationship" path through the full engine,
        // not just the extractor's own unit tests.
        var result = _engine.Analyze("The big red car.");

        Assert.DoesNotContain(result.Diagnostics, d =>
            d.Severity is DiagnosticSeverity.Critical or DiagnosticSeverity.Error);
    }

    [Fact]
    public void Analyze_SentenceWithNoRecognizableEntities_EmitsNoEntitiesWarning()
    {
        // Purely stop-words / unknown tokens - nothing for EntityRecognizer to latch onto.
        var result = _engine.Analyze("of the and but");

        Assert.Contains(result.Diagnostics, d => d.Code == "NLE.Semantics.NoEntities");
        Assert.Null(result.Intent);
        Assert.Equal(0, result.Confidence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Analyze_NullOrBlankInput_ThrowsArgumentException(string? input)
    {
        Assert.Throws<ArgumentException>(() => _engine.Analyze(input!));
    }

    [Fact]
    public void Constructor_NullPipeline_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new NaturalLanguageEngine.Core.NaturalLanguageEngine(null!));
    }
}
