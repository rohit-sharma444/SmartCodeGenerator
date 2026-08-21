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

    /// <summary>Two pages each: the list, and the editor the list opens.</summary>
    [TestMethod]
    public void ThereIsOnePageForEveryClass()
    {
        foreach (var name in Names)
        {
            var route = name.ToLowerInvariant();

            Assert.Contains($"@page \"/{route}\"", Source($"{name}Page.razor"));

            var editor = Source($"{name}EditorPage.razor");

            Assert.Contains($"@page \"/{route}/{{Id:int}}\"", editor);
            Assert.Contains($"@page \"/{route}/new\"", editor);
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

    /// <summary>
    /// The same actions as the desktop targets, in the same two places: the list finds and the
    /// editor changes.
    /// </summary>
    /// <remarks>
    /// They live in the shared components now rather than in each page, which is the whole point of
    /// the page being three lines long.
    /// </remarks>
    [TestMethod]
    public void EachPageOffersTheSameActionsAsTheDesktopTargets()
    {
        var list = Source("MasterScreen.razor");

        foreach (var action in new[] { "New", "Open", "Delete" })
            Assert.Contains($">{action}</button>", list);

        var editor = Source("RecordEditor.razor");

        Assert.Contains(">Save</button>", editor);
        Assert.Contains(">Cancel</button>", editor);
    }

    /// <summary>
    /// Delete writes through immediately, rather than marking a row and waiting for a Save.
    /// </summary>
    /// <remarks>
    /// The old editable table needed a pending-deletions list, because Save wrote back everything
    /// still present and a removed record reappeared on the next load. A read-only list has no Save
    /// of its own, so there is nothing to defer to.
    /// </remarks>
    [TestMethod]
    public void DeletingARecordWritesThroughToTheStore()
    {
        var list = Source("MasterScreen.razor");

        Assert.Contains("all.RemoveAll(", list);
        Assert.Contains("_store.SaveAll(all)", list);
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

        // The screens are components, not pages: they have no route and are rendered BY a page.
        var screens = new[] { "MasterScreen.razor", "TransactionScreen.razor", "RecordEditor.razor" };

        foreach (var screen in screens)
            Assert.AreEqual("Components/Screens", files.Single(f => f.Name == screen).RelativeFolder);

        // ScreenFields is C#, and sits with the roles it reflects over rather than among the markup.
        Assert.AreEqual("Screens", files.Single(f => f.Name == "ScreenFields.cs").RelativeFolder);

        var shell = new[] { "NavMenu.razor", "Routes.razor" };

        foreach (var page in files.Where(
                     f => f.Name.EndsWith(".razor")
                          && !shell.Contains(f.Name)
                          && !screens.Contains(f.Name)))
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

            // With nothing to seed the list page has no code at all: it is a route and one element.
            Assert.DoesNotContain("@code", page);
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
