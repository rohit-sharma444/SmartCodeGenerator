using CodeGenerator.App;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class BuildAppAddCommandTests
{
    private static string[] Parts(params string[] parts) => parts;

    [TestMethod]
    public void AddLine_RejoinsAMultiWordName()
    {
        // `line.Split(' ', 3, ...)` hands the loop three parts for `add Cash register`. Passing
        // only parts[1] silently created an entity called "Cash".
        var model = new CandidateDomainModel();

        BuildAppCommand.AddLine(model, Parts("add", "Cash", "register"));

        Assert.AreEqual("CashRegister", model.Entities.Single().Name);
    }

    [TestMethod]
    public void AddLine_ReportsTheNameThatWasCreated_NotTheOneThatWasTyped()
    {
        var model = new CandidateDomainModel();

        var line = BuildAppCommand.AddLine(model, Parts("add", "cash", "register"));

        Assert.AreEqual($"Added '{model.Entities.Single().Name}'.", line);
    }

    [TestMethod]
    public void AddLine_ReportsTheSuggestionsOwnName_WhenItPromotesOne()
    {
        var model = new CandidateDomainModel();
        model.Suggestions.Add(new CandidateEntity
        {
            Name = "LibraryMakerspace",
            Provenance = Provenance.Wiki
        });

        var line = BuildAppCommand.AddLine(model, Parts("add", "librarymakerspace"));

        Assert.AreEqual("Added 'LibraryMakerspace'.", line);
    }

    [TestMethod]
    public void AddLine_SaysSoWhenNothingWasAdded()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Book" });

        var line = BuildAppCommand.AddLine(model, Parts("add", "book"));

        StringAssert.Contains(line, "Could not add");
    }
}
