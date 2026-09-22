using AppScaffolding.Screens;

namespace CodeGenerator.Tests.App;

[TestClass]
public class EntryTemplateCatalogTests
{
    [TestMethod]
    public void Keys_ListEveryTemplateWithClassicFirst()
    {
        Assert.AreEqual("classic", EntryTemplateCatalog.Keys[0]);
        Assert.HasCount(Enum.GetValues<EntryTemplate>().Length, EntryTemplateCatalog.Keys);
    }

    [TestMethod]
    public void KeyFor_HyphenatesTwoColumn()
    {
        // One token, never two words: InlineCustomization scans the line right to left by
        // whitespace, the same reason "master-detail" is hyphenated.
        Assert.AreEqual("two-column", EntryTemplateCatalog.KeyFor(EntryTemplate.TwoColumn));
        Assert.DoesNotContain(" ", EntryTemplateCatalog.KeyFor(EntryTemplate.TwoColumn));
    }

    [TestMethod]
    public void TryResolve_ReadsEveryKeyCaseInsensitively()
    {
        foreach (var template in Enum.GetValues<EntryTemplate>())
        {
            Assert.IsTrue(EntryTemplateCatalog.TryResolve(
                EntryTemplateCatalog.KeyFor(template).ToUpperInvariant(), out var parsed));
            Assert.AreEqual(template, parsed);
        }
    }

    [TestMethod]
    public void TryResolve_RefusesAnythingElse()
    {
        Assert.IsFalse(EntryTemplateCatalog.TryResolve("kanban", out _));
        Assert.IsFalse(EntryTemplateCatalog.TryResolve("", out _));
        Assert.IsFalse(EntryTemplateCatalog.TryResolve("two column", out _));
    }

    [TestMethod]
    public void EveryTemplateHasADescription()
    {
        foreach (var template in Enum.GetValues<EntryTemplate>())
            Assert.IsNotEmpty(EntryTemplateCatalog.Descriptions[template]);
    }

    [TestMethod]
    public void OptionRenderings_ListTheKeysInOrder()
    {
        Assert.AreEqual("classic/grouped/tabbed/two-column/split/ribbon", EntryTemplateCatalog.PromptOptions);
        Assert.AreEqual("classic|grouped|tabbed|two-column|split|ribbon", EntryTemplateCatalog.HelpOptions);
    }

    [TestMethod]
    public void Supports_IsTrueForWpfOnlyInThisPhase()
    {
        Assert.IsTrue(EntryTemplates.Supports("wpf"));
        Assert.IsTrue(EntryTemplates.Supports("WPF"));
        Assert.IsFalse(EntryTemplates.Supports("winforms"));
        Assert.IsFalse(EntryTemplates.Supports("blazor"));
        Assert.IsFalse(EntryTemplates.Supports("console"));
    }
}
