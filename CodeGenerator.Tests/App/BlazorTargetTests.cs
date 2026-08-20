using AppScaffolding;
using CodeGenerator.App;
using AppScaffolding.Targets;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The Blazor Server target: a page per class, a nav menu that reaches them, and a home page.
/// </summary>
/// <remarks>
/// Razor is not C#, so <c>EmittedSource</c> cannot compile these — the assertions are over the
/// emitted markup. The end-to-end proof is a generated app that builds and serves, done by hand.
/// </remarks>
[TestClass]
public class BlazorTargetTests
{
    private static readonly string[] Names = ["Order", "Customer"];

    private static IReadOnlyList<GeneratedFile> Files(AppCustomization? customization = null)
        => new BlazorTarget().FrontEndFiles(
            "Shop", Names, customization ?? AppCustomization.Default);

    private static string Source(string name)
        => Files().Single(f => f.Name == name).Source;

    [TestMethod]
    public void ItIsADistinctTargetFromTheWebApi()
    {
        var blazor = new BlazorTarget();

        Assert.AreEqual("blazor", blazor.Key);
        Assert.AreEqual(DotNetProjectType.BlazorServer, blazor.ProjectType);

        // A separate folder suffix, so both can be generated from the same model side by side.
        Assert.AreNotEqual(new WebApiTarget().FolderSuffix, blazor.FolderSuffix);
    }

    [TestMethod]
    public void ItIsReachableByKeyAndOnTheMenu()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("blazor", out var resolved));
        Assert.AreEqual("blazor", resolved.Key);

        // A target nobody can pick is a target nobody has.
        CollectionAssert.Contains(ApplicationTypeMenu.Labels.ToList(), "Web Application (Blazor Server)");
    }

    [TestMethod]
    public void ThereIsOnePageForEveryClass()
    {
        foreach (var name in Names)
        {
            var page = Source($"{name}Page.razor");

            Assert.Contains($"@page \"/{name.ToLowerInvariant()}\"", page);
            Assert.Contains($"{name}Repository", page);
        }
    }

    [TestMethod]
    public void EveryPageDeclaresInteractiveRendering()
    {
        // Without this the buttons render and do nothing: the .NET 10 blazor template serves pages
        // statically unless a component asks for interactivity. The single most breakable line here.
        foreach (var name in Names)
            Assert.Contains("@rendermode InteractiveServer", Source($"{name}Page.razor"));
    }

    [TestMethod]
    public void EachPageOffersTheSameActionsAsTheDesktopTargets()
    {
        var page = Source("OrderPage.razor");

        Assert.Contains("Add", page);
        Assert.Contains("Save", page);
        Assert.Contains("Remove", page);
    }

    [TestMethod]
    public void RemovingASavedRecordDeletesItOnSave()
    {
        // The first cut only dropped the row from the list, so Save wrote back everything still
        // present and the removed record reappeared on the next load.
        var page = Source("OrderPage.razor");

        Assert.Contains("_repository.Delete(", page);
        Assert.Contains("_removed", page);
    }

    [TestMethod]
    public void PagesDeclareTheGeneratedNamespaces()
    {
        var page = Source("OrderPage.razor");

        Assert.Contains("@using Shop.Data", page);
        Assert.Contains("@using Shop.Models", page);
    }

    [TestMethod]
    public void PagesFollowANamespacePrefix()
    {
        // The prefix feature sets the project's root namespace; a page importing the unprefixed one
        // would not compile.
        var prefixed = new BlazorTarget().FrontEndFiles(
            "Shop", Names, AppCustomization.Default with { NamespacePrefix = "CodisLtd" });

        var page = prefixed.Single(f => f.Name == "OrderPage.razor").Source;

        Assert.Contains("@using CodisLtd.Shop.Data", page);
        Assert.DoesNotContain("@using Shop.Data", page);
    }

    [TestMethod]
    public void TheNavMenuLinksToEveryPageAndNotToTheTemplateSamples()
    {
        var nav = Source("NavMenu.razor");

        foreach (var name in Names)
            Assert.Contains($"href=\"{name.ToLowerInvariant()}\"", nav);

        // The template's own nav points at Counter and Weather, which this target deletes.
        Assert.DoesNotContain("counter", nav.ToLowerInvariant());
        Assert.DoesNotContain("weather", nav.ToLowerInvariant());
    }

    [TestMethod]
    public void TheHomePageListsTheClassesToo()
    {
        // The nav is a narrow column that collapses on a small screen; a first visitor should not
        // have to find it to see what the application holds.
        var home = Source("Home.razor");

        Assert.Contains("@page \"/\"", home);

        foreach (var name in Names)
            Assert.Contains(name, home);
    }

    [TestMethod]
    public void EverythingLandsWhereTheBlazorTemplateKeepsIt()
    {
        var files = Files();

        Assert.AreEqual("Components/Layout", files.Single(f => f.Name == "NavMenu.razor").RelativeFolder);

        // AppSeed is C#, not markup, and belongs beside the SeedData it calls.
        Assert.AreEqual("Data", files.Single(f => f.Name == "AppSeed.cs").RelativeFolder);

        // The router sits beside App.razor rather than among the pages, which is where the
        // template keeps it -- it is not a page and has no route of its own.
        Assert.AreEqual("Components", files.Single(f => f.Name == "Routes.razor").RelativeFolder);

        var shell = new[] { "NavMenu.razor", "Routes.razor" };

        foreach (var page in files.Where(
                     f => f.Name.EndsWith(".razor") && !shell.Contains(f.Name)))
            Assert.AreEqual("Components/Pages", page.RelativeFolder);
    }

    [TestMethod]
    public void APageShowsSampleDataOnAFirstVisit()
    {
        // Not a nicety: every other target seeds from its entry point, and this one emits no
        // Program.cs to seed from, so without the gate a Blazor app opened straight after generation
        // shows empty tables where the desktop app of the same model shows rows.
        var files = Files();

        Assert.Contains("SeedData.EnsureSeeded();", files.Single(f => f.Name == "AppSeed.cs").Source);

        foreach (var name in Names)
            Assert.Contains("AppSeed.Once();", files.Single(f => f.Name == $"{name}Page.razor").Source);
    }

    [TestMethod]
    public void SeedingIsGatedSoOnlyTheFirstPageViewPaysForIt()
    {
        // One page per class, each seeding on initialize: EnsureSeeded is idempotent but reads every
        // store to discover that, so the gate is what stops a nav click costing a full sweep.
        var gate = Files().Single(f => f.Name == "AppSeed.cs").Source;

        Assert.Contains("_done", gate);

        // Blazor Server serves circuits concurrently, so the check cannot be a bare bool test.
        Assert.Contains("lock (", gate);
    }

    [TestMethod]
    public void TurningSampleDataOffLeavesNoSeedingBehind()
    {
        // SeedData.cs is not generated at all in that case, so a leftover call would not compile.
        var bare = new BlazorTarget().FrontEndFiles(
            "Shop", Names, AppCustomization.Default with { SampleSeedData = false });

        Assert.IsFalse(bare.Any(f => f.Name == "AppSeed.cs"));

        foreach (var name in Names)
        {
            var page = bare.Single(f => f.Name == $"{name}Page.razor").Source;

            Assert.DoesNotContain("AppSeed", page);
            Assert.Contains("_items = _repository.GetAll();", page);
        }
    }

    [TestMethod]
    public void TheTitleCustomizationReachesTheShell()
    {
        // Company AND a pattern that uses it: TitleFor only substitutes {company} into whatever
        // TitlePattern says, and the default pattern is the app name alone. Setting a company by
        // itself deliberately does not rename anything — the wizard offers the combined pattern as
        // a suggestion, it is not a default.
        var branded = new BlazorTarget().FrontEndFiles(
            "Shop",
            Names,
            AppCustomization.Default with
            {
                Company = "Codis Ltd",
                TitlePattern = "{company} - {app}",
            });

        var nav = branded.Single(f => f.Name == "NavMenu.razor").Source;

        Assert.Contains("Codis Ltd - Shop", nav);
    }

    [TestMethod]
    public void AModelWithNoClassesStillProducesAUsableShell()
    {
        // A zero-entity app is a known gap elsewhere, but the shell must not emit broken markup.
        var empty = new BlazorTarget().FrontEndFiles("Shop", [], AppCustomization.Default);

        Assert.IsNotEmpty(empty);
        Assert.Contains("@page \"/\"", empty.Single(f => f.Name == "Home.razor").Source);
    }
}
