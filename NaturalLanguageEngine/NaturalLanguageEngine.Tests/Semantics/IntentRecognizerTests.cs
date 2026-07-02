using NaturalLanguageEngine.Models.Semantics;
using NaturalLanguageEngine.Semantics;
using Xunit;

namespace NaturalLanguageEngine.Tests.Semantics;

public class IntentRecognizerTests
{
    private readonly IntentRecognizer _recognizer = new();

    private static Entity MakeEntity(string text, EntityType type, double confidence)
        => new() { Text = text, Type = type, Confidence = confidence };

    [Fact]
    public void Recognize_NoEntities_ReturnsNull()
    {
        Assert.Null(_recognizer.Recognize(Array.Empty<Entity>()));
    }

    [Fact]
    public void Recognize_WithActionEntity_ProducesVerbNamedIntent()
    {
        var entities = new[]
        {
            MakeEntity("Sarah", EntityType.Person, 0.6),
            MakeEntity("booked", EntityType.Action, 0.8),
        };

        var intent = _recognizer.Recognize(entities);

        Assert.NotNull(intent);
        Assert.Equal("BookedIntent", intent!.Name);
        Assert.Equal(0.8, (double)intent.Confidence, precision: 6);
    }

    [Fact]
    public void Recognize_MultiWordAction_UsesLastWordAsVerb()
    {
        var entities = new[] { MakeEntity("is running", EntityType.Action, 0.7) };

        var intent = _recognizer.Recognize(entities);

        Assert.Equal("RunningIntent", intent!.Name);
    }

    [Fact]
    public void Recognize_NoActionEntity_ProducesDescribeIntent()
    {
        var entities = new[] { MakeEntity("car", EntityType.Object, 0.7) };

        var intent = _recognizer.Recognize(entities);

        Assert.NotNull(intent);
        Assert.Equal("DescribeIntent", intent!.Name);
        Assert.Equal(0.56, (double)intent.Confidence, precision: 6); // 0.7 * 0.8
    }
}
