using System.Linq;
using AppScaffolding;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>
/// A rule registered via ValidationRegistry.Add (what `add business validation` already does)
/// reaches ClassDefinition.BusinessRules once DomainModelMapper builds the class, with no
/// scan and no accepted proposal involved.
/// </summary>
[TestClass]
[DoNotParallelize] // ValidationRegistry is a process-wide static.
public class BusinessRulesReachAppTests
{
    [TestInitialize]
    public void Setup() => ValidationRegistry.Clear();

    [TestCleanup]
    public void Cleanup() => ValidationRegistry.Clear();

    [TestMethod]
    public void A_hand_registered_rule_lands_on_the_matching_class()
    {
        var rule = new ComparisonRule("StartDate", "EndDate", "> 0", "StartDate must not be after EndDate");
        ValidationRegistry.Add("Booking", rule);

        var model = new CandidateDomainModel();
        var booking = new CandidateEntity { Name = "Booking" };
        booking.Fields.Add(new CandidateField { Name = "StartDate", Type = "DateTime" });
        booking.Fields.Add(new CandidateField { Name = "EndDate", Type = "DateTime" });
        model.Entities.Add(booking);

        var classes = DomainModelMapper.ToClassDefinitions(model);
        var definition = classes.Single(c => c.Name == "Booking");

        Assert.HasCount(1, definition.BusinessRules);
        Assert.AreSame(rule, definition.BusinessRules[0]);
    }

    [TestMethod]
    public void A_class_with_no_registered_rule_gets_an_empty_list()
    {
        var model = new CandidateDomainModel();
        var order = new CandidateEntity { Name = "Order" };
        order.Fields.Add(new CandidateField { Name = "Total", Type = "decimal" });
        model.Entities.Add(order);

        var classes = DomainModelMapper.ToClassDefinitions(model);

        Assert.IsEmpty(classes.Single(c => c.Name == "Order").BusinessRules);
    }
}
