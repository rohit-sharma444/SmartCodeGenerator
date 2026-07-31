using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class BuildAppPersistenceParsingTests
{
    [TestMethod]
    public void ResolvePersistence_DefaultsToJsonWithoutAToken()
    {
        Assert.AreEqual(PersistenceKind.Json,
            BuildAppCommand.ResolvePersistence(new[] { "approve", "console" }));
    }

    [TestMethod]
    public void ResolvePersistence_ReadsSqliteAndJsonCaseInsensitively()
    {
        Assert.AreEqual(PersistenceKind.Sqlite,
            BuildAppCommand.ResolvePersistence(new[] { "approve", "console", "SQLite" }));
        Assert.AreEqual(PersistenceKind.Json,
            BuildAppCommand.ResolvePersistence(new[] { "approve", "console", "json" }));
    }

    [TestMethod]
    public void ResolvePersistence_ReturnsNullForAnUnknownToken()
    {
        Assert.IsNull(BuildAppCommand.ResolvePersistence(new[] { "approve", "console", "postgres" }));
    }

    [TestMethod]
    public void IsPersistenceAllowed_RejectsSqliteForBothDesktopTargets()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("wpf", out var wpf));
        Assert.IsTrue(TargetScaffolders.TryResolve("winforms", out var winforms));
        Assert.IsTrue(TargetScaffolders.TryResolve("console", out var console));

        // Both desktop targets share EntityViewModel, which reads JsonStore directly rather than
        // going through the store seam. Allowing sqlite here would generate an app that ignores it.
        Assert.IsFalse(BuildAppCommand.IsPersistenceAllowed(wpf, PersistenceKind.Sqlite));
        Assert.IsFalse(BuildAppCommand.IsPersistenceAllowed(winforms, PersistenceKind.Sqlite));

        Assert.IsTrue(BuildAppCommand.IsPersistenceAllowed(wpf, PersistenceKind.Json));
        Assert.IsTrue(BuildAppCommand.IsPersistenceAllowed(winforms, PersistenceKind.Json));
        Assert.IsTrue(BuildAppCommand.IsPersistenceAllowed(console, PersistenceKind.Sqlite));
    }
}
