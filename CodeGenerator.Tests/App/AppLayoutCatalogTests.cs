using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class AppLayoutCatalogTests
{
    [TestMethod]
    public void Keys_ListEveryLayoutWithTabsFirst()
    {
        Assert.AreEqual("tabs", AppLayoutCatalog.Keys[0]);
        Assert.HasCount(Enum.GetValues<AppLayout>().Length, AppLayoutCatalog.Keys);
    }

    [TestMethod]
    public void KeyFor_HyphenatesMasterDetail()
    {
        // One token, never two words: InlineCustomization scans the line right to left by
        // whitespace, so "master detail" would be read as two separate tokens.
        Assert.AreEqual("master-detail", AppLayoutCatalog.KeyFor(AppLayout.MasterDetail));
        Assert.DoesNotContain(" ", AppLayoutCatalog.KeyFor(AppLayout.MasterDetail));
    }

    [TestMethod]
    public void TryResolve_ReadsEveryKeyCaseInsensitively()
    {
        foreach (var layout in Enum.GetValues<AppLayout>())
        {
            Assert.IsTrue(AppLayoutCatalog.TryResolve(AppLayoutCatalog.KeyFor(layout).ToUpperInvariant(), out var parsed));
            Assert.AreEqual(layout, parsed);
        }
    }

    [TestMethod]
    public void TryResolve_RefusesAnythingElse()
    {
        Assert.IsFalse(AppLayoutCatalog.TryResolve("kanban", out _));
        Assert.IsFalse(AppLayoutCatalog.TryResolve("", out _));
        Assert.IsFalse(AppLayoutCatalog.TryResolve("master detail", out _));
    }

    [TestMethod]
    public void EveryLayoutHasADescription()
    {
        foreach (var layout in Enum.GetValues<AppLayout>())
            Assert.IsNotEmpty(AppLayoutCatalog.Descriptions[layout]);
    }

    [TestMethod]
    public void OptionRenderings_ListTheKeysInOrder()
    {
        // The wizard prompt and the help catalogue print these verbatim, so a key added to the
        // catalogue without a rendering here would reach the user as a silently short list.
        Assert.AreEqual("tabs/sidebar/master-detail/dashboard", AppLayoutCatalog.PromptOptions);
        Assert.AreEqual("tabs|sidebar|master-detail|dashboard", AppLayoutCatalog.HelpOptions);
    }
}
