using System.Linq;
using CodeGenerator.App;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class BuildAppWhyCommandTests
{
    private static string[] Parts(params string[] parts) => parts;

    private static CandidateDomainModel Model()
    {
        var model = new CandidateDomainModel();
        model.Record("Cash register", DecisionStage.Enrichment, DecisionOutcome.Suggested,
            DecisionReasons.LeadDoesNotMentionIt, "lead of \"Point of sale\"");
        return model;
    }

    [TestMethod]
    public void BareWhy_ReportsEverything()
    {
        var text = string.Join("\n", BuildAppCommand.WhyLines(Model(), Parts("why")));

        StringAssert.Contains(text, "Decisions (1):");
    }

    [TestMethod]
    public void WhyWithAName_ReportsThatNamesTrail()
    {
        var text = string.Join("\n", BuildAppCommand.WhyLines(Model(), Parts("why", "CashRegister")));

        StringAssert.Contains(text, DecisionReasons.LeadDoesNotMentionIt);
    }

    [TestMethod]
    public void WhyWithAMultiWordName_RejoinsWhatTheSplitBrokeApart()
    {
        // `line.Split(' ', 3, ...)` hands the loop three parts for `why Cash register`.
        var text = string.Join("\n",
            BuildAppCommand.WhyLines(Model(), Parts("why", "Cash", "register")));

        StringAssert.Contains(text, DecisionReasons.LeadDoesNotMentionIt);
    }

    [TestMethod]
    public void WhyWithAnUnknownName_SaysSoRatherThanReportingEverything()
    {
        var text = string.Join("\n", BuildAppCommand.WhyLines(Model(), Parts("why", "Nonesuch")));

        StringAssert.Contains(text, "No decision recorded for 'Nonesuch'");
    }
}
