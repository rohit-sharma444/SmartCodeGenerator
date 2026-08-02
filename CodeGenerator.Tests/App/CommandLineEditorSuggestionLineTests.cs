using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Covers the width contract for the suggestion rows drawn under the prompt.
///
/// The editor clears those rows by blanking one console row per suggestion. That only
/// works while every suggestion occupies exactly one row, so a rendered row must never
/// reach the console width — at the width the console wraps the overflow onto a row the
/// clear loop does not know about, and it survives on screen after Enter.
/// </summary>
[TestClass]
public class CommandLineEditorSuggestionLineTests
{
    private static CommandInfo Suggestion(string command, string description)
        => new() { Command = command, Description = description };

    [TestMethod]
    [DataRow(80)]
    [DataRow(100)]
    [DataRow(120)]
    public void FormatSuggestionLine_EveryCommandInTheDictionary_StaysOnOneRow(int width)
    {
        // The dictionary is the real source of suggestions, so it — not a sample — is
        // what has to fit. 29 of its entries render past 80 columns; the widest is 139.
        foreach (var command in CommandDictionary.Commands)
        {
            var line = CommandLineEditor.FormatSuggestionLine(command, width);

            Assert.IsLessThan(
                width,
                line.Length,
                $"'{command.Command}' rendered {line.Length} columns wide "
                + $"in a {width}-column console, so it wraps onto an uncleared row.");
        }
    }

    [TestMethod]
    public void FormatSuggestionLine_LineNarrowerThanTheConsole_IsLeftAlone()
    {
        var line = CommandLineEditor.FormatSuggestionLine(
            Suggestion("show classes", "Lists the classes."), 120);

        Assert.AreEqual(
            $"  {"show classes",-40} Lists the classes.", line);
    }

    [TestMethod]
    public void FormatSuggestionLine_ClippedLine_MarksTheCutWithAnEllipsis()
    {
        var line = CommandLineEditor.FormatSuggestionLine(
            Suggestion("show learning", new string('x', 200)), 80);

        Assert.AreEqual(79, line.Length);
        Assert.IsTrue(line.EndsWith("...", StringComparison.Ordinal), line);
        Assert.IsTrue(line.StartsWith("  show learning", StringComparison.Ordinal), line);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(4)]
    [DataRow(20)]
    public void FormatSuggestionLine_ConsoleTooNarrowForTheLayout_StillFits(int width)
    {
        // A console can be narrower than the 40-column command field. Clipping still has
        // to hold, and it must not throw on the way — the editor draws on every keystroke.
        var line = CommandLineEditor.FormatSuggestionLine(
            Suggestion("add validation <rule> on <property> in <class name>",
                "Adds a field-level data rule to a class."),
            width);

        Assert.IsLessThan(
            width, line.Length, $"{line.Length} columns in a {width}-column console.");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-5)]
    public void FormatSuggestionLine_NonPositiveWidth_RendersNothing(int width)
        => Assert.AreEqual(
            string.Empty,
            CommandLineEditor.FormatSuggestionLine(Suggestion("show classes", "Lists."), width));
}
