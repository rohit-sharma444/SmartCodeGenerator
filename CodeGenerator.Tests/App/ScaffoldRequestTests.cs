using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>The request object and the customization-aware target overload. A target that does
/// not care about customization must keep working untouched, which is what the default
/// interface method buys.</summary>
[TestClass]
public class ScaffoldRequestTests
{
    [TestMethod]
    public void A_request_defaults_to_neutral_customization()
    {
        var request = ScaffoldRequest.For(
            new DomainDiscovery.Model.CandidateDomainModel(), "Shop", "C:\\ws", new ConsoleTarget());

        Assert.AreEqual(AppCustomization.Default, request.Customization);
        Assert.AreEqual(PersistenceKind.Json, request.Persistence);
    }

    /// <summary>ClassLibraryTarget does not override the three-argument overload; the default
    /// interface method must forward it to the two-argument one.</summary>
    [TestMethod]
    public void An_unaware_target_forwards_to_its_two_argument_overload()
    {
        ITargetScaffolder target = new ClassLibraryTarget();

        var plain = target.FrontEndFiles("Shop", ["Order"]);
        var customized = target.FrontEndFiles("Shop", ["Order"], AppCustomization.Default);

        Assert.HasCount(plain.Count, customized);
    }
}
