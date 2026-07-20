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
}
