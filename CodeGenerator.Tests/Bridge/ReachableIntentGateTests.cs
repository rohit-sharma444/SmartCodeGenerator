using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

/// <summary>
/// Every intent a user can actually reach must have a handler. Recognition comes
/// from exactly three places — LiteralCommandTable, CommandFront's regex guards,
/// and NlpCommandMapper's table — and nothing checked that the handler table
/// kept up with them. Deliberately NOT gated on IntentRepository: its rules do
/// not drive recognition, so gating there would demand handlers for intents no
/// user can type.
/// </summary>
[TestClass]
public sealed class ReachableIntentGateTests
{
    private static IEnumerable<IntentType> Reachable() =>
        LiteralCommandTable.Intents
            .Concat(CommandFront.GuardedIntents)
            .Concat(NlpCommandMapper.MappedIntents)
            .Distinct();

    [TestMethod]
    public void Every_reachable_intent_has_a_handler()
    {
        var missing = Reachable()
            .Where(i => !CommandHandlingService.IsSupported(i))
            .ToList();

        Assert.IsEmpty(missing,
            "Reachable intents with no handler would print 'not supported yet': "
            + string.Join(", ", missing));
    }

    // A source that returns an empty collection would make the gate above pass
    // vacuously, which is the one way this test can rot without failing.
    [TestMethod]
    public void Each_recognition_source_is_non_empty()
    {
        Assert.IsNotEmpty(LiteralCommandTable.Intents);
        Assert.IsNotEmpty(CommandFront.GuardedIntents);
        Assert.IsNotEmpty(NlpCommandMapper.MappedIntents);
    }

    [TestMethod]
    public void Reachable_set_contains_a_known_member_from_each_source()
    {
        CollectionAssert.Contains(LiteralCommandTable.Intents.ToList(), IntentType.Help);
        CollectionAssert.Contains(CommandFront.GuardedIntents.ToList(), IntentType.GenerateValidator);
        CollectionAssert.Contains(NlpCommandMapper.MappedIntents.ToList(), IntentType.CreateClass);
    }
}
