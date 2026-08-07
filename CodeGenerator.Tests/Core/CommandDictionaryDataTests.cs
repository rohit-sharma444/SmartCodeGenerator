using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// Guards the hand-authored command catalog against data-entry mistakes that
/// would otherwise surface verbatim in <c>help</c> and TAB suggestions.
/// </summary>
[TestClass]
public class CommandDictionaryDataTests
{
    [TestMethod]
    public void EveryExample_HasBalancedAngleBrackets()
    {
        foreach (var command in CommandDictionary.Commands)
        {
            var opens = command.Example.Count(c => c == '<');
            var closes = command.Example.Count(c => c == '>');
            Assert.AreEqual(opens, closes,
                $"Example has unbalanced <> placeholders: '{command.Example}'");
        }
    }

    [TestMethod]
    public void EveryCommand_HasNonEmptyDescriptionAndExample()
    {
        foreach (var command in CommandDictionary.Commands)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(command.Description),
                $"Command '{command.Command}' has an empty description.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(command.Example),
                $"Command '{command.Command}' has an empty example.");
        }
    }

    [TestMethod]
    public void MarkdownReference_KeepsEveryRowThreeColumns()
    {
        // A bare '|' in a command or description ends the table cell, so a row
        // documenting alternatives silently renders with extra columns. Backticks do
        // not protect it. This went unnoticed until `ai mode <retrieval|generative|
        // hybrid>` became the first entry to contain one.
        var markdown = CommandDictionary.GetMarkdownReference();

        var rows = markdown
            .Split('\n')
            .Where(l => l.StartsWith("| ", StringComparison.Ordinal))
            .Where(l => !l.StartsWith("| ---", StringComparison.Ordinal));

        foreach (var row in rows)
        {
            // Count only UNESCAPED pipes: 4 delimit the 3 columns.
            var delimiters = 0;
            for (var i = 0; i < row.Length; i++)
                if (row[i] == '|' && (i == 0 || row[i - 1] != '\\'))
                    delimiters++;

            Assert.AreEqual(4, delimiters, $"row is not 3 columns: {row}");
        }
    }

    [TestMethod]
    public void HelpText_RendersEveryCommandExample()
    {
        var help = CommandDictionary.GetHelpText();

        foreach (var command in CommandDictionary.Commands)
            StringAssert.Contains(help, $"e.g. {command.Example}",
                $"help output is missing the example for '{command.Command}'.");
    }
}
