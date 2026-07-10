using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
public class NlpCommandMapperTests
{
    [TestMethod]
    public void First_action_wins_over_trailing_called_verb()
    {
        // "create a class called Customer" tags both "create" and "called" as
        // Action. The first must decide the intent.
        var analysis = NlpActionProvider.Analyze("create a class called Customer");
        Assert.AreEqual(IntentType.CreateClass, NlpCommandMapper.ResolveIntent(analysis));
    }

    [TestMethod]
    public void Determiners_are_stripped_from_the_target()
    {
        var withDeterminer = NlpCommandMapper.ResolveIntent(NlpActionProvider.Analyze("create a class"));
        var without = NlpCommandMapper.ResolveIntent(NlpActionProvider.Analyze("create class"));
        Assert.AreEqual(IntentType.CreateClass, withDeterminer);
        Assert.AreEqual(without, withDeterminer);
    }

    [TestMethod]
    public void Null_analysis_resolves_to_Unknown()
    {
        Assert.AreEqual(IntentType.Unknown, NlpCommandMapper.ResolveIntent(null));
    }

    [TestMethod]
    public void Unrecognized_verb_target_pair_resolves_to_Unknown()
    {
        // "juggle the flamingo" has no rule; must not guess.
        var analysis = NlpActionProvider.Analyze("juggle the flamingo");
        Assert.AreEqual(IntentType.Unknown, NlpCommandMapper.ResolveIntent(analysis));
    }
}
