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

    [TestMethod]
    public void Embedded_class_name_before_target_still_resolves_CreateClass()
    {
        // NLE tags the embedded class name as a Person entity that lands ahead
        // of the "class" target: [Action create][Person "a Customer"][Object
        // class][Person Name][Action string]. The mapper must skip that leading
        // Person run to find the real target and still resolve CreateClass.
        var analysis = NlpActionProvider.Analyze("create a Customer class with Name string");
        Assert.AreEqual(IntentType.CreateClass, NlpCommandMapper.ResolveIntent(analysis));
    }

    [DataTestMethod]
    [DataRow("create class Book with properties Title string", IntentType.CreateClass)]
    [DataRow("create class Lend with properties Name string", IntentType.CreateClass)]
    [DataRow("create class Schedule with properties Name string", IntentType.CreateClass)]
    [DataRow("create a Book class with Title string", IntentType.CreateClass)]
    [DataRow("add property Total decimal to Invoice", IntentType.CreateProperty)]
    [DataRow("add property Net int to Invoice", IntentType.CreateProperty)]
    public void A_capitalised_name_after_the_verb_is_not_part_of_the_target(string sentence, IntentType expected)
    {
        // NLE tags Book/Lend/Schedule as Action wherever they appear, so the name used to join
        // the target span ("class book properties") and the command came back Unknown.
        // "Total decimal" is merged into one Object the same way ("property total decimal").
        var analysis = NlpActionProvider.Analyze(sentence);
        Assert.AreEqual(expected, NlpCommandMapper.ResolveIntent(analysis));
    }

    [TestMethod]
    public void Leading_person_skip_does_not_reach_a_dead_intent()
    {
        // "create a repository for Customer" has no target entity after the
        // verb ([Action create][Object "a repository"][Person Customer]), so
        // the span is a non-target Object that isn't in the table. The
        // leading-Person skip must not over-trigger and manufacture a match;
        // CreateRepository has no handler and must stay unreachable.
        var analysis = NlpActionProvider.Analyze("create a repository for Customer");
        Assert.AreEqual(IntentType.Unknown, NlpCommandMapper.ResolveIntent(analysis));
    }
}
