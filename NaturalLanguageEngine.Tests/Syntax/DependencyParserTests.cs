using NaturalLanguageEngine.Models.Analysis;
using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Models.Lexical;
using NaturalLanguageEngine.Models.Syntax;
using NaturalLanguageEngine.Syntax;
using Xunit;

namespace NaturalLanguageEngine.Tests.Syntax;

public class DependencyParserTests
{
    private readonly DependencyParser _parser = new();

    private static TokenAnalysis MakeToken(string text, TokenType type = TokenType.Word)
        => new() { Token = new Token { Text = text, Type = type } };

    private static Phrase NounPhrase(TokenAnalysis head, params TokenAnalysis[] leadingModifiers)
    {
        var phrase = new Phrase { Type = PhraseType.NounPhrase, Head = head };
        foreach (var modifier in leadingModifiers)
            phrase.Tokens.Add(modifier);
        phrase.Tokens.Add(head);
        return phrase;
    }

    private static Phrase VerbPhrase(TokenAnalysis head, params TokenAnalysis[] leadingAuxiliaries)
    {
        var phrase = new Phrase { Type = PhraseType.VerbPhrase, Head = head };
        foreach (var aux in leadingAuxiliaries)
            phrase.Tokens.Add(aux);
        phrase.Tokens.Add(head);
        return phrase;
    }

    [Fact]
    public void Parse_EmptyPhraseList_ReturnsNoEdges()
    {
        Assert.Empty(_parser.Parse(Array.Empty<Phrase>()));
    }

    [Fact]
    public void Parse_SimpleSvo_ProducesRootSubjectAndDirectObject()
    {
        var sarah = MakeToken("Sarah");
        var booked = MakeToken("booked");
        var flight = MakeToken("flight");

        var phrases = new[]
        {
            NounPhrase(sarah),
            VerbPhrase(booked),
            NounPhrase(flight)
        };

        var deps = _parser.Parse(phrases);

        Assert.Contains(deps, d => d.Type == DependencyType.Root && d.Governor == booked && d.Dependent == booked);
        Assert.Contains(deps, d => d.Type == DependencyType.Subject && d.Governor == booked && d.Dependent == sarah);
        Assert.Contains(deps, d => d.Type == DependencyType.DirectObject && d.Governor == booked && d.Dependent == flight);
    }

    [Fact]
    public void Parse_TwoNounPhrasesAfterVerb_SecondIsIndirectObject()
    {
        var send = MakeToken("send");
        var report = MakeToken("report");
        var john = MakeToken("John");

        var phrases = new[]
        {
            VerbPhrase(send),
            NounPhrase(report),
            NounPhrase(john)
        };

        var deps = _parser.Parse(phrases);

        Assert.Contains(deps, d => d.Type == DependencyType.DirectObject && d.Dependent == report);
        Assert.Contains(deps, d => d.Type == DependencyType.IndirectObject && d.Dependent == john);
    }

    [Fact]
    public void Parse_DateOrTimeHeadedNounPhraseAfterVerb_IsModifierNotObject()
    {
        var send = MakeToken("send");
        var date = MakeToken("2025-06-30", TokenType.Date);

        var phrases = new[]
        {
            VerbPhrase(send),
            NounPhrase(date)
        };

        var deps = _parser.Parse(phrases);

        var edge = Assert.Single(deps, d => d.Governor == send && d.Dependent == date);
        Assert.Equal(DependencyType.Modifier, edge.Type);
    }

    [Fact]
    public void Parse_VerbPhraseWithNoPrecedingNounPhrase_HasNoSubjectEdge()
    {
        var send = MakeToken("send");

        var phrases = new[] { VerbPhrase(send) };

        var deps = _parser.Parse(phrases);

        Assert.DoesNotContain(deps, d => d.Type == DependencyType.Subject);
        Assert.Contains(deps, d => d.Type == DependencyType.Root);
    }

    [Fact]
    public void Parse_NounPhraseWithNoGoverningVerb_ProducesNoInterPhraseEdge()
    {
        var car = MakeToken("car");

        var phrases = new[] { NounPhrase(car) };

        var deps = _parser.Parse(phrases);

        Assert.DoesNotContain(deps, d => d.Governor == car && d.Dependent != car);
    }

    [Fact]
    public void Parse_DeterminerAndAdjectiveInNounPhrase_AttachAsModifiersToHead()
    {
        var the = MakeToken("the");
        var big = MakeToken("big");
        var car = MakeToken("car");

        var phrase = NounPhrase(car, the, big);

        var deps = _parser.Parse(new[] { phrase });

        Assert.Contains(deps, d => d.Type == DependencyType.Modifier && d.Governor == car && d.Dependent == the);
        Assert.Contains(deps, d => d.Type == DependencyType.Modifier && d.Governor == car && d.Dependent == big);
    }

    [Fact]
    public void Parse_AuxiliaryInVerbPhrase_AttachesAsPredicateToHead()
    {
        var will = MakeToken("will");
        var book = MakeToken("book");

        var phrase = VerbPhrase(book, will);

        var deps = _parser.Parse(new[] { phrase });

        Assert.Contains(deps, d => d.Type == DependencyType.Predicate && d.Governor == book && d.Dependent == will);
    }

    [Fact]
    public void Parse_TwoClauses_EachVerbGetsItsOwnRootAndObjects()
    {
        var sarah = MakeToken("Sarah");
        var booked = MakeToken("booked");
        var flight = MakeToken("flight");
        var send = MakeToken("send");
        var report = MakeToken("report");

        var phrases = new[]
        {
            NounPhrase(sarah),
            VerbPhrase(booked),
            NounPhrase(flight),
            VerbPhrase(send),
            NounPhrase(report)
        };

        var deps = _parser.Parse(phrases);

        Assert.Contains(deps, d => d.Type == DependencyType.Root && d.Governor == booked);
        Assert.Contains(deps, d => d.Type == DependencyType.Root && d.Governor == send);
        Assert.Contains(deps, d => d.Type == DependencyType.DirectObject && d.Governor == booked && d.Dependent == flight);
        Assert.Contains(deps, d => d.Type == DependencyType.DirectObject && d.Governor == send && d.Dependent == report);
        // "flight" (the previous clause's object) must not also become
        // send's subject just because it immediately precedes it.
        Assert.DoesNotContain(deps, d => d.Type == DependencyType.Subject && d.Governor == send);
    }

    [Fact]
    public void Parse_PhraseWithNullHead_IsSkippedWithoutThrowing()
    {
        var headless = new Phrase { Type = PhraseType.NounPhrase };
        var booked = MakeToken("booked");

        var deps = _parser.Parse(new[] { headless, VerbPhrase(booked) });

        Assert.Contains(deps, d => d.Type == DependencyType.Root && d.Governor == booked);
    }
}
