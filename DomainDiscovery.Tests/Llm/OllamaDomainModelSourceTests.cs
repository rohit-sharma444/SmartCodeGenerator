using DomainDiscovery;
using DomainDiscovery.Llm;
using Xunit;

namespace DomainDiscovery.Tests.Llm;

/// <summary>
/// Answers each of the three asks with whatever the test set, so the source's orchestration is
/// testable with no Ollama.
/// </summary>
internal sealed class FakeOllamaClient : IOllamaClient
{
    public bool Available { get; set; } = true;
    public string? LastDescription { get; private set; }

    // The one-shot ask.
    public LlmDomainModel? Answer { get; set; }
    public string? Error { get; set; }
    public string? Warning { get; set; }

    // Phase one.
    public LlmDomainModel? Roster { get; set; }
    public string? RosterError { get; set; }

    // Phase two.
    public LlmDomainModel? Detail { get; set; }
    public string? DetailError { get; set; }
    public string? DetailWarning { get; set; }

    /// <summary>The roster phase two was actually given, so a test can prove it was handed on.</summary>
    public LlmDomainModel? RosterPassedToDetail { get; private set; }

    /// <summary>What each ask reports through its progress callback before answering.</summary>
    public IReadOnlyList<LlmClass> RosterProgress { get; set; } = [];
    public IReadOnlyList<LlmClass> DetailProgress { get; set; } = [];

    public bool IsAvailable() => Available;

    public OllamaAnswer Ask(string description, Action<IReadOnlyList<LlmClass>>? onClasses = null)
    {
        LastDescription = description;

        Report(Answer?.Classes ?? [], onClasses);

        return Compose(Answer, Error, Warning);
    }

    public OllamaAnswer AskForClasses(
        string description, Action<IReadOnlyList<LlmClass>>? onClasses = null)
    {
        LastDescription = description;

        Report(RosterProgress, onClasses);

        return Compose(Roster, RosterError, null);
    }

    public OllamaAnswer AskForDetail(
        string description, LlmDomainModel roster, Action<IReadOnlyList<LlmClass>>? onClasses = null)
    {
        RosterPassedToDetail = roster;

        Report(DetailProgress, onClasses);

        return Compose(Detail, DetailError, DetailWarning);
    }

    /// <summary>One report per class, the way a real stream produces them.</summary>
    private static void Report(
        IReadOnlyList<LlmClass> classes, Action<IReadOnlyList<LlmClass>>? onClasses)
    {
        for (var i = 1; i <= classes.Count; i++)
            onClasses?.Invoke(classes.Take(i).ToList());
    }

    private static OllamaAnswer Compose(LlmDomainModel? model, string? error, string? warning)
    {
        if (model is null)
            return OllamaAnswer.Failure(error ?? "no answer");

        return warning is null ? OllamaAnswer.Success(model) : OllamaAnswer.Partial(model, warning);
    }
}

public class OllamaDomainModelSourceTests
{
    private static LlmClass Class(string name, string role = "Party", params string[] properties)
    {
        var c = new LlmClass { Name = name, Role = role, Purpose = $"the {name}" };

        foreach (var p in properties)
            c.Properties.Add(new LlmProperty { Name = p, Type = "string" });

        return c;
    }

    private static LlmDomainModel Model(params LlmClass[] classes)
    {
        var m = new LlmDomainModel();
        m.Classes.AddRange(classes);
        return m;
    }

    private static LlmDomainModel OneClass() => Model(Class("Customer", "Party", "Name"));

    /// <summary>The one-phase route, which <c>discovery.json</c> can still select.</summary>
    private static OllamaDomainModelSource OnePhase(FakeOllamaClient client)
        => new(client, "qwen2.5:3b", phases: 1);

    private static OllamaDomainModelSource TwoPhase(FakeOllamaClient client)
        => new(client, "qwen2.5:3b", phases: 2);

    [Fact]
    public void SucceedsAndLabelsTheModelThatAnswered()
    {
        var client = new FakeOllamaClient { Answer = OneClass() };

        var outcome = OnePhase(client).Discover("a shop");

        Assert.True(outcome.Succeeded);
        Assert.Equal("qwen2.5:3b (Ollama)", outcome.SourceLabel);
        Assert.Contains(outcome.Model.Entities, e => e.Name == "Customer");
    }

    [Fact]
    public void FailsWithAMessageWhenTheModelAnswersNothing()
    {
        var outcome = OnePhase(new FakeOllamaClient { Answer = null }).Discover("a shop");

        Assert.False(outcome.Succeeded);
        Assert.NotNull(outcome.Message);
        Assert.Empty(outcome.Model.Entities);
    }

    [Fact]
    public void ThePublishedMessageCarriesTheClientsActualReason()
    {
        // Before OllamaAnswer, every one of these collapsed into the same unhelpful sentence -
        // this is what stops that: the specific reason the client determined must survive all the
        // way to the string the user reads.
        var outcome = OnePhase(new FakeOllamaClient
            {
                Answer = null,
                Error = "the server returned 500 Internal Server Error: {\"error\":\"boom\"}"
            })
            .Discover("a shop");

        Assert.False(outcome.Succeeded);
        Assert.Contains("the server returned 500", outcome.Message);
        Assert.Contains("boom", outcome.Message);
    }

    [Fact]
    public void FailsWhenTheModelReturnsNoClassesAtAll()
    {
        var outcome = OnePhase(new FakeOllamaClient { Answer = new LlmDomainModel() })
            .Discover("a shop");

        Assert.False(outcome.Succeeded);
    }

    [Fact]
    public void PassesTheDescriptionStraightThrough()
    {
        var client = new FakeOllamaClient { Answer = OneClass() };

        OnePhase(client).Discover("a shop that sells bicycles");

        Assert.Equal("a shop that sells bicycles", client.LastDescription);
    }

    // ---- the two-phase route -------------------------------------------------------------

    [Fact]
    public void AsksForTheClassesFirstAndThenTheirDetail()
    {
        var client = new FakeOllamaClient
        {
            Roster = Model(Class("Product", "Item"), Class("Sale", "Transaction")),
            Detail = Model(Class("Product", "Item", "Name"), Class("Sale", "Transaction", "Total"))
        };

        var outcome = TwoPhase(client).Discover("a shop");

        Assert.True(outcome.Succeeded);
        Assert.Null(outcome.Message);
        Assert.Equal(["Product", "Sale"], outcome.Model.Entities.Select(e => e.Name));

        // The roster really was handed to the second pass, which is what keeps it from inventing
        // its own class list.
        Assert.Equal(["Product", "Sale"], client.RosterPassedToDetail!.Classes.Select(c => c.Name));
    }

    [Fact]
    public void NeverTicksAClassTheFirstPassHasOnlyNamed()
    {
        // Found by watching the real page: the roster pass used to report its classes as DONE, so
        // eight ticks appeared and then, the moment the detail pass began, seven of them turned
        // back into open circles. Ticks running backwards reads as something going wrong. Naming a
        // class is not finishing it.
        var client = new FakeOllamaClient
        {
            Roster = Model(Class("Product", "Item"), Class("Sale", "Transaction")),
            Detail = Model(Class("Product", "Item", "Name"), Class("Sale", "Transaction", "Total")),
            RosterProgress = [Class("Product", "Item"), Class("Sale", "Transaction")]
        };

        var seen = new List<DiscoveryProgress>();

        TwoPhase(client).Discover("a shop", seen.Add);

        // The count of ticked classes only ever grows.
        var ticked = seen.Select(p => p.Done.Count).ToList();

        Assert.Equal(ticked.OrderBy(n => n), ticked);

        // And during the first pass specifically, nothing is ticked at all.
        Assert.All(
            seen.TakeWhile(p => p.Step == "Working out which classes there are"),
            p => Assert.Empty(p.Done));
    }

    [Fact]
    public void TicksEachClassAsItArrivesOnTheOneShotRoute()
    {
        // The mirror of the rule above: a class from the single-call ask DOES arrive finished,
        // properties and all, so ticking it is the truth.
        var client = new FakeOllamaClient { Answer = Model(Class("Product", "Item", "Name")) };

        var seen = new List<DiscoveryProgress>();

        OnePhase(client).Discover("a shop", seen.Add);

        Assert.Equal(["Product [Item]"], seen[^1].Done);
        Assert.Empty(seen[^1].Pending);
    }

    [Fact]
    public void ShowsTheWholeClassListAsPendingBeforeAskingForAnyDetail()
    {
        // The point of the split. Between the two calls the panel already knows every class, and
        // none of them is finished.
        var client = new FakeOllamaClient
        {
            Roster = Model(Class("Product", "Item"), Class("Sale", "Transaction")),
            Detail = Model(Class("Product", "Item", "Name"), Class("Sale", "Transaction", "Total"))
        };

        var seen = new List<DiscoveryProgress>();

        TwoPhase(client).Discover("a shop", seen.Add);

        var handover = seen.First(p => p.Pending.Count == 2);

        Assert.Empty(handover.Done);
        Assert.Equal(["Product [Item]", "Sale [Transaction]"], handover.Pending);
        Assert.Equal("Filling in the properties", handover.Step);
    }

    [Fact]
    public void MovesEachClassFromPendingToDoneAsItsDetailArrives()
    {
        var client = new FakeOllamaClient
        {
            Roster = Model(Class("Product", "Item"), Class("Sale", "Transaction")),
            Detail = Model(Class("Product", "Item", "Name"), Class("Sale", "Transaction", "Total")),
            DetailProgress =
            [
                Class("Product", "Item", "Name"),
                Class("Sale", "Transaction", "Total")
            ]
        };

        var seen = new List<DiscoveryProgress>();

        TwoPhase(client).Discover("a shop", seen.Add);

        var half = seen.First(p => p.Done.Count == 1 && p.Pending.Count == 1);

        Assert.Equal("Product [Item] — 1 property", half.Done[0]);
        Assert.Equal("Sale [Transaction]", half.Pending[0]);
        Assert.Equal("Filling in the properties — 1 of 2", half.Step);

        Assert.Equal(["Product [Item] — 1 property", "Sale [Transaction] — 1 property"], seen[^1].Done);
    }

    [Fact]
    public void KeepsEveryClassWhenTheDetailPassIsCutShort()
    {
        // The failure the split is meant to survive: the roster finished, the long pass did not.
        var client = new FakeOllamaClient
        {
            Roster = Model(Class("Product", "Item"), Class("Sale", "Transaction"), Class("Store", "Place")),
            Detail = Model(Class("Product", "Item", "Name"))
        };

        var outcome = TwoPhase(client).Discover("a shop");

        Assert.True(outcome.Succeeded);
        Assert.Equal(["Product", "Sale", "Store"], outcome.Model.Entities.Select(e => e.Name));
        Assert.Contains("2 of 3", outcome.Message);
        Assert.Contains("no properties yet", outcome.Message);
    }

    [Fact]
    public void KeepsTheClassListWhenTheDetailPassFailsCompletely()
    {
        var client = new FakeOllamaClient
        {
            Roster = Model(Class("Product", "Item"), Class("Sale", "Transaction")),
            Detail = null,
            DetailError = "it stalled"
        };

        var outcome = TwoPhase(client).Discover("a shop");

        // A named, classified class list is a real starting point, and it is what the user has
        // already been watching. Discarding it would be the old bug in a new place.
        Assert.True(outcome.Succeeded);
        Assert.Equal(["Product", "Sale"], outcome.Model.Entities.Select(e => e.Name));
        Assert.Contains("it stalled", outcome.Message);
    }

    [Fact]
    public void FailsWhenEvenTheClassListCouldNotBeProduced()
    {
        var outcome = TwoPhase(new FakeOllamaClient { Roster = null, RosterError = "it stalled" })
            .Discover("a shop");

        Assert.False(outcome.Succeeded);
        Assert.Contains("it stalled", outcome.Message);
    }

    [Fact]
    public void FailsWhenTheClassListIsEmpty()
    {
        var outcome = TwoPhase(new FakeOllamaClient { Roster = new LlmDomainModel() })
            .Discover("a shop");

        Assert.False(outcome.Succeeded);
        Assert.NotNull(outcome.Message);
    }

    [Fact]
    public void NeverAsksForDetailWhenThereAreNoClasses()
    {
        var client = new FakeOllamaClient { Roster = null, RosterError = "it stalled" };

        TwoPhase(client).Discover("a shop");

        Assert.Null(client.RosterPassedToDetail);
    }

    [Fact]
    public void DefaultsToTwoPhases()
    {
        // The constructor default and DiscoveryConfig's must agree; a mismatch would make the
        // documented behaviour depend on which overload a caller happened to use.
        var client = new FakeOllamaClient
        {
            Roster = Model(Class("Product", "Item")),
            Detail = Model(Class("Product", "Item", "Name")),
            Answer = null
        };

        var outcome = new OllamaDomainModelSource(client, "qwen2.5:3b").Discover("a shop");

        Assert.True(outcome.Succeeded);
        Assert.Equal(DiscoveryConfig.DefaultPhases, 2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void TreatsAnUnrecognisedPhaseCountAsTheDefault(int phases)
    {
        var client = new FakeOllamaClient
        {
            Roster = Model(Class("Product", "Item")),
            Detail = Model(Class("Product", "Item", "Name"))
        };

        var outcome = new OllamaDomainModelSource(client, "qwen2.5:3b", phases).Discover("a shop");

        Assert.True(outcome.Succeeded);
        Assert.NotNull(client.RosterPassedToDetail);
    }
}
