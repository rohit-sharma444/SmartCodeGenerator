using AppScaffolding;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The emitted TestSupport.Fill builds records the app's OWN generated validators accept, run
/// against the real compiled TestSupport rather than read from its source.
/// </summary>
[TestClass]
public class GeneratedTestSupportTests
{
    /// <summary>
    /// A filled date carries no time of day.
    /// </summary>
    /// <remarks>
    /// A SQL <c>date</c> column generates the rule "must be a date with no time of day", and Fill
    /// set every DateTime to <c>DateTime.UtcNow</c> - so an app built <c>from sql</c> failed its own
    /// validator test and its own round trip on every first run, on all seven targets, while the
    /// generator's thousands of tests passed. Found by tools/app-check, which runs each generated
    /// app's tests. A midnight date satisfies that rule and every other date rule the generator
    /// emits (a floor such as smalldatetime's 1900 is far below today).
    /// </remarks>
    [TestMethod]
    public void A_filled_date_has_no_time_of_day()
    {
        using var app = EmittedDataLayer.Compile(
            EmittedLogin.None,
            validation: true,
            ("Tests/TestSupport.cs", TestProjectTemplates.TestSupport("Acme", login: false, sessionAuth: false)));

        var item = Activator.CreateInstance(app.Model)!;

        app.Assembly.GetType("Acme.Tests.TestSupport")!
            .GetMethod("Fill")!
            .MakeGenericMethod(app.Model)
            .Invoke(null, [item]);

        var filled = (DateTime)app.Model.GetProperty("CreatedOn")!.GetValue(item)!;

        Assert.AreNotEqual(default, filled, "Fill must set the date at all");
        Assert.AreEqual(TimeSpan.Zero, filled.TimeOfDay);
    }

    /// <summary>
    /// An app that seeds has its stores seeded before any generated test writes to one.
    /// </summary>
    /// <remarks>
    /// Seeding is per store and only into an EMPTY store, and the tests share the app's real
    /// store files. When the Line round trip ran first, Fill created a "Test" Invoice for the
    /// line's foreign key and left it there; SeedData then skipped the no-longer-empty Invoice
    /// store but seeded Lines pointing at Invoices 1-5, and the Invoice round trip's own new
    /// record was refused its delete - "3 Line record(s) refer to it". xUnit's test order is
    /// per project, so which targets failed varied (an invoices-from-json app's Worker and
    /// Console, a shop-from-sql app's WinForms); a real app never hits it, because it seeds at
    /// startup before anything else touches a store. The tests now do the same: TestSupport's
    /// static constructor seeds, and every round-trip and validator test touches TestSupport
    /// before any store. A static constructor rather than a module initializer, which CA2255
    /// warns about in every generated test build.
    /// </remarks>
    [TestMethod]
    public void TestSupport_seeds_the_stores_before_any_test_uses_them_when_the_app_seeds()
    {
        var seeding = TestProjectTemplates.TestSupport("Acme", login: false, sessionAuth: false, seed: true);
        StringAssert.Contains(seeding, "static TestSupport() => global::Acme.Data.SeedData.EnsureSeeded();");

        // No SeedData class exists in an app built with noseed, so naming it would not compile.
        var notSeeding = TestProjectTemplates.TestSupport("Acme", login: false, sessionAuth: false, seed: false);
        Assert.DoesNotContain("SeedData", notSeeding);
    }

    /// <summary>The wiring: the scaffolder hands TestSupport the app's own seed setting.</summary>
    [TestMethod]
    public void A_scaffolded_apps_TestSupport_seeds_exactly_when_the_app_does()
    {
        Assert.IsTrue(AppScaffolding.Targets.TargetScaffolders.TryResolve("console", out var target));

        foreach (var seed in new[] { true, false })
        {
            var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
            Directory.CreateDirectory(workspace);

            try
            {
                var request = new ScaffoldRequest(
                    TestModels.OneEntity(), "Shop", workspace, target, PersistenceKind.Json,
                    ImportedData: null, AppCustomization.Default with { SampleSeedData = seed }, DateTime.Now.Year);

                var result = new AppScaffolder().Scaffold(request);
                Assert.IsTrue(result.Success, result.Message);
                Assert.IsNotNull(result.TestProjectPath);

                var support = File.ReadAllText(
                    Path.Combine(Path.GetDirectoryName(result.TestProjectPath)!, "TestSupport.cs"));

                Assert.AreEqual(seed, support.Contains("SeedData.EnsureSeeded()", StringComparison.Ordinal), $"seed: {seed}");
            }
            finally
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }
}
