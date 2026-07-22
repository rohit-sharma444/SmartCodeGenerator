using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
[DoNotParallelize]
public class BusinessValidationRoutingTests
{
    [TestMethod]
    public void Add_business_validation_resolves_intent_class_and_text()
    {
        var cmd = CommandFront.Parse("add business validation EndDate must be after StartDate in Booking", "");
        Assert.AreEqual(IntentType.AddBusinessValidation, cmd.Intent);
        Assert.AreEqual("Booking", cmd.ClassName);
        Assert.AreEqual("EndDate must be after StartDate", cmd.ValidationText);
    }

    [TestMethod]
    public void Add_business_validation_does_not_trip_the_data_validation_guard()
    {
        var cmd = CommandFront.Parse("add business validation X must be after Y in Z", "");
        Assert.AreNotEqual(IntentType.AddValidation, cmd.Intent);
    }

    [TestMethod]
    public void Show_business_validation_resolves_intent_and_class()
    {
        var cmd = CommandFront.Parse("show business validation Booking", "");
        Assert.AreEqual(IntentType.ShowBusinessValidation, cmd.Intent);
        Assert.AreEqual("Booking", cmd.ClassName);
    }

    [TestMethod]
    public void Data_add_validation_still_routes_to_AddValidation()
    {
        var cmd = CommandFront.Parse("add validation Email should not be blank on Email in Customer", "");
        Assert.AreEqual(IntentType.AddValidation, cmd.Intent);
    }
}
