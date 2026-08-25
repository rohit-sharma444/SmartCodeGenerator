using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Recognising `chat`, and knowing when it cannot run.
/// </summary>
/// <remarks>
/// <c>Handle</c> is not driven here: it binds a port, opens a browser and blocks. What can be
/// asserted without any of that is what decides whether it runs at all — the same separation
/// <c>BuildAppCommand.UseWebWorkflow</c> exists for.
/// </remarks>
[TestClass]
public class ChatCommandTests
{
    [TestMethod]
    public void Recognises_the_command_and_its_spellings()
    {
        Assert.IsTrue(ChatCommand.IsChat("chat"));
        Assert.IsTrue(ChatCommand.IsChat("CHAT"));
        Assert.IsTrue(ChatCommand.IsChat("  chat  "));
        Assert.IsTrue(ChatCommand.IsChat("ui"));
    }

    [TestMethod]
    public void Does_not_claim_input_that_merely_mentions_it()
    {
        Assert.IsFalse(ChatCommand.IsChat("chat about the weather"));
        Assert.IsFalse(ChatCommand.IsChat("create class Chat"));
        Assert.IsFalse(ChatCommand.IsChat(null));
        Assert.IsFalse(ChatCommand.IsChat(""));
    }

    [TestMethod]
    public void Needs_a_browser_and_says_so_when_there_is_none()
    {
        Assert.IsTrue(ChatCommand.UseWeb(inputRedirected: false, noWeb: false));
        Assert.IsFalse(ChatCommand.UseWeb(inputRedirected: true, noWeb: false));
        Assert.IsFalse(ChatCommand.UseWeb(inputRedirected: false, noWeb: true));
    }

    [TestMethod]
    public void The_route_classifies_it_as_chat()
    {
        Assert.AreEqual(CommandKind.Chat, CommandRoute.Classify("chat"));
        Assert.AreEqual(CommandKind.Chat, CommandRoute.Classify("ui"));
    }

    [TestMethod]
    public void The_catalog_documents_it()
    {
        // Everything the tool can do is in CommandDictionary, which is what `help` renders and
        // docs/commands.md is generated from. A command that is not there is a command nobody
        // finds.
        Assert.IsTrue(
            CodeGenerationEngine.Core.CommandDictionary.Commands.Any(
                c => c.Command.StartsWith("chat", StringComparison.OrdinalIgnoreCase)));
    }
}
