using System.Linq;
using AppScaffolding;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using DataIngestion;
using DataIngestion.Schema;
using DomainDiscovery.Model;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The full path an accepted Cross-Field proposal travels: DataModelBuilder finds it,
/// DomainModelMapper carries it once accepted, ValidatorTemplate emits it, and the compiled
/// validator actually refuses a record that breaks it.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AcceptedEntityProposalReachesValidatorTests
{
    [TestInitialize]
    public void Setup() => ValidationRegistry.Clear();

    [TestCleanup]
    public void Cleanup() => ValidationRegistry.Clear();

    [TestMethod]
    public void An_accepted_cross_field_proposal_refuses_a_record_that_breaks_it()
    {
        var table = new TabularTable { Name = "Booking" };
        table.Columns.Add(new TabularColumn { Name = "StartDate" });
        table.Columns.Add(new TabularColumn { Name = "EndDate" });
        table.Rows.Add(["2026-01-01", "2026-01-10"]);
        table.Rows.Add(["2026-02-01", "2026-02-20"]);

        // Infer column types from the rows
        foreach (var column in table.Columns)
            column.Type = ColumnTypeInference.Infer(table.ColumnValues(column.Name), column.Name);

        var model = DataModelBuilder.Build([table], []);
        var booking = model.Entities.Single(e => e.Name == "Booking");

        var proposal = booking.Proposals.Single(p => p.Kind == EntityRuleProposalKind.CrossField);
        proposal.Accepted = true;

        var classes = DomainModelMapper.ToClassDefinitions(model);
        var definition = classes.Single(c => c.Name == "Booking");

        Assert.HasCount(1, definition.BusinessRules);

        var source = ValidatorTemplate.Generated(
            "Acme", definition, [], referencesRequired: false, businessRules: definition.BusinessRules);

        StringAssert.Contains(source, "private static void Business(");
        StringAssert.Contains(source, "        Business,");
    }
}
