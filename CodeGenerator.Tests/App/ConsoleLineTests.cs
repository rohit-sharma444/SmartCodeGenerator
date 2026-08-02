using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Covers the one clipping rule shared by every console row that must not exceed its width —
/// <see cref="CommandLineEditor.FormatSuggestionLine"/> and <see cref="SelectionPrompt"/> both
/// delegate to <see cref="ConsoleLine.Clip"/> rather than keeping their own copy.
/// </summary>
[TestClass]
public class ConsoleLineTests
{
    [TestMethod]
    public void Clip_ShortText_IsUnchanged()
        => Assert.AreEqual("hello", ConsoleLine.Clip("hello", 80));

    [TestMethod]
    public void Clip_TextExactlyAtWidthMinusOne_IsUnchanged()
    {
        var text = new string('x', 9);

        Assert.AreEqual(text, ConsoleLine.Clip(text, 10));
    }

    [TestMethod]
    public void Clip_LongerText_IsTruncatedToWidthMinusOneWithEllipsis()
    {
        var text = new string('x', 20);

        var clipped = ConsoleLine.Clip(text, 10);

        Assert.HasCount(9, clipped);
        Assert.IsTrue(clipped.EndsWith("...", StringComparison.Ordinal), clipped);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(-5)]
    public void Clip_NonPositiveEffectiveWidth_ReturnsEmpty(int width)
        => Assert.AreEqual(string.Empty, ConsoleLine.Clip("hello world", width));

    [TestMethod]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public void Clip_WidthTooNarrowForTheEllipsis_TruncatesBareWithNoEllipsis(int width)
    {
        var clipped = ConsoleLine.Clip("hello world", width);

        Assert.HasCount(width - 1, clipped);
        Assert.IsFalse(clipped.EndsWith("...", StringComparison.Ordinal), clipped);
    }
}
