using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

[TestClass]
public class TargetScaffoldersTests
{
    [TestMethod]
    public void Keys_AreEveryTargetInOrder()
    {
        // winforms and blazor are appended rather than inserted, so Default stays wpf and no
        // existing key changes position. The wizard's menu supplies its own display order.
        CollectionAssert.AreEqual(
            new[] { "wpf", "console", "webapi", "classlib", "worker", "winforms", "blazor" },
            TargetScaffolders.Keys.ToList());
    }

    [TestMethod]
    public void Default_IsWpf()
    {
        Assert.AreEqual("wpf", TargetScaffolders.Default.Key);
    }

    [TestMethod]
    public void TryResolve_IsCaseInsensitive()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("WebApi", out var t));
        Assert.AreEqual("webapi", t.Key);
    }

    [TestMethod]
    public void TryResolve_UnknownKey_ReturnsFalse()
    {
        Assert.IsFalse(TargetScaffolders.TryResolve("nonsense", out _));
    }

    [TestMethod]
    public void ByKey_HasAnEntryForEveryKey()
    {
        foreach (var key in TargetScaffolders.Keys)
            Assert.IsTrue(TargetScaffolders.ByKey.ContainsKey(key), $"missing {key}");
    }
}
