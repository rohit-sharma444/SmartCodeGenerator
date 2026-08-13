using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class AppLayoutSettingTests
{
    [TestMethod]
    public void Default_NamesNoLayout()
    {
        // Null, not Tabs. `layout tabs` is the first inline option whose value equals the
        // default, and BuildAppCommand.Merge decides a setting was typed by comparing against
        // Default — so "not mentioned" has to be a distinct value or a typed `layout tabs`
        // would lose to a remembered profile.
        Assert.IsNull(AppCustomization.Default.Layout);
    }

    [TestMethod]
    public void EffectiveLayout_IsTabsWhenNoneWasNamed()
    {
        Assert.AreEqual(AppLayout.Tabs, AppCustomization.Default.EffectiveLayout);
    }

    [TestMethod]
    public void EffectiveLayout_IsWhateverWasNamed()
    {
        var customization = AppCustomization.Default with { Layout = AppLayout.Dashboard };

        Assert.AreEqual(AppLayout.Dashboard, customization.EffectiveLayout);
    }

    [TestMethod]
    public void EffectiveLayout_IsTabsWhenTabsWasNamedExplicitly()
    {
        var customization = AppCustomization.Default with { Layout = AppLayout.Tabs };

        Assert.AreEqual(AppLayout.Tabs, customization.EffectiveLayout);
        Assert.IsNotNull(customization.Layout);
    }
}
