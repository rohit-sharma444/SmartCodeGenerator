using AppScaffolding;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// How the remembered profile and the typed options combine, and what the review loop says about
/// the result.
/// </summary>
[TestClass]
public class CustomizationMergeTests
{
    private static readonly AppCustomization Saved = AppCustomization.Default with
    {
        Company = "Codis Ltd",
        Theme = AppTheme.Light,
        Accent = "#0B6E4F",
        TitlePattern = AppCustomization.CompanyTitlePattern,
        IconPath = "C:\\brand\\logo.ico",
        Logging = true,
        NamespacePrefix = "CodisLtd",
    };

    [TestMethod]
    public void With_nothing_typed_the_profile_stands()
        => Assert.AreEqual(Saved, BuildAppCommand.Merge(Saved, AppCustomization.Default));

    [TestMethod]
    public void With_no_profile_the_typed_options_stand()
    {
        var inline = AppCustomization.Default with { Company = "Acme", Theme = AppTheme.Dark };

        Assert.AreEqual(inline, BuildAppCommand.Merge(AppCustomization.Default, inline));
    }

    /// <summary>What was typed just now beats what a previous run settled on, setting by setting
    /// rather than wholesale.</summary>
    [TestMethod]
    public void A_typed_option_overrides_only_itself()
    {
        var merged = BuildAppCommand.Merge(
            Saved, AppCustomization.Default with { Theme = AppTheme.Dark });

        Assert.AreEqual(AppTheme.Dark, merged.Theme);
        Assert.AreEqual("Codis Ltd", merged.Company);
        Assert.AreEqual("#0B6E4F", merged.Accent);
        Assert.IsTrue(merged.Logging);
    }

    /// <summary>The flags are the awkward half: `novalidation` and `noseed` set false, and false is
    /// not the default, so they have to survive a profile that says otherwise.</summary>
    [TestMethod]
    public void A_typed_flag_overrides_the_profile()
    {
        var merged = BuildAppCommand.Merge(
            Saved,
            AppCustomization.Default with { Validation = false, SampleSeedData = false });

        Assert.IsFalse(merged.Validation);
        Assert.IsFalse(merged.SampleSeedData);
        Assert.IsTrue(merged.Logging, "the profile's own logging choice should survive");
    }

    [TestMethod]
    public void The_default_customization_says_nothing_in_the_review_loop()
        => Assert.IsEmpty(CustomizationSummary.Lines(AppCustomization.Default, target: null));

    [TestMethod]
    public void Every_changed_setting_is_named_in_the_review_loop()
    {
        var lines = CustomizationSummary.Lines(Saved with
        {
            Validation = false,
            SampleSeedData = false,
        }, target: null);

        var text = string.Join("\n", lines);

        StringAssert.Contains(text, "Company: Codis Ltd");
        StringAssert.Contains(text, "Theme: light");
        StringAssert.Contains(text, "Accent: #0B6E4F");
        StringAssert.Contains(text, "Icon: C:\\brand\\logo.ico");
        StringAssert.Contains(text, "Title: {company} - {app}");
        StringAssert.Contains(text, "no validation layer");
        StringAssert.Contains(text, "logging");
        StringAssert.Contains(text, "no sample data");
    }

    /// <summary>Only what changed: a run that turned logging on has one thing to report, not
    /// eight.</summary>
    [TestMethod]
    public void Only_the_changed_settings_are_named()
    {
        var lines = CustomizationSummary.Lines(
            AppCustomization.Default with { Logging = true }, target: null);

        Assert.HasCount(1, lines);
        StringAssert.Contains(lines[0], "logging");
    }

    [TestMethod]
    public void A_typed_namespace_prefix_overrides_the_profile()
    {
        var merged = BuildAppCommand.Merge(
            Saved, AppCustomization.Default with { NamespacePrefix = "Acme" });

        Assert.AreEqual("Acme", merged.NamespacePrefix);
        Assert.AreEqual("Codis Ltd", merged.Company);
    }

    [TestMethod]
    public void The_namespace_prefix_is_named_in_the_review_loop()
    {
        var lines = CustomizationSummary.Lines(
            AppCustomization.Default with { NamespacePrefix = "CodisLtd" }, target: null);

        Assert.HasCount(1, lines);
        StringAssert.Contains(lines[0], "Namespace prefix: CodisLtd");
    }

    [TestMethod]
    public void An_inline_layout_beats_the_remembered_one()
    {
        var saved = AppCustomization.Default with { Layout = AppLayout.Dashboard };
        var inline = AppCustomization.Default with { Layout = AppLayout.Sidebar };

        Assert.AreEqual(AppLayout.Sidebar, BuildAppCommand.Merge(saved, inline).Layout);
    }

    /// <summary>The defect this test exists to prevent: `layout tabs` equals the DEFAULT layout, so
    /// the differs-from-Default rule every other option uses would read it as "not mentioned" and
    /// hand back the profile's dashboard — silently ignoring what the user just typed.</summary>
    [TestMethod]
    public void An_inline_tabs_beats_a_remembered_dashboard()
    {
        var saved = AppCustomization.Default with { Layout = AppLayout.Dashboard };
        var inline = AppCustomization.Default with { Layout = AppLayout.Tabs };

        Assert.AreEqual(AppLayout.Tabs, BuildAppCommand.Merge(saved, inline).Layout);
    }

    [TestMethod]
    public void A_remembered_layout_survives_a_line_that_names_none()
    {
        var saved = AppCustomization.Default with { Layout = AppLayout.Sidebar };

        Assert.AreEqual(
            AppLayout.Sidebar, BuildAppCommand.Merge(saved, AppCustomization.Default).Layout);
    }

    [TestMethod]
    public void The_layout_is_named_in_the_review_loop()
    {
        var lines = CustomizationSummary.Lines(
            AppCustomization.Default with { Layout = AppLayout.Dashboard }, target: null);

        Assert.Contains("  Layout: dashboard", lines);
    }

    /// <summary>`layout tabs` against a remembered dashboard is a deliberate override, and the
    /// review loop is where the user checks it took effect — so it is listed even though tabs is
    /// what an uncustomized app already gets.</summary>
    [TestMethod]
    public void An_explicit_tabs_layout_is_still_named_in_the_review_loop()
    {
        var lines = CustomizationSummary.Lines(
            AppCustomization.Default with { Layout = AppLayout.Tabs }, target: null);

        Assert.Contains("  Layout: tabs", lines);
    }

    /// <summary>
    /// Every setting typed on the line beats the profile, not just the ones someone remembered to
    /// list in Merge.
    /// </summary>
    /// <remarks>
    /// Login, transactions, masters and entry-template used to be missing from it, so on the web
    /// and interactive-console paths the profile quietly won: `build app: shop nologin` generated a
    /// login screen whenever the profile had one. The guard below fails when a new setting is added
    /// to AppCustomization without being given a typed value here, which is how those four were
    /// missed in the first place.
    /// </remarks>
    [TestMethod]
    public void Every_typed_setting_overrides_the_profile()
    {
        var saved = AppCustomization.Default with
        {
            Company = "Codis Ltd",
            Theme = AppTheme.Light,
            Accent = "#0B6E4F",
            TitlePattern = AppCustomization.CompanyTitlePattern,
            IconPath = @"C:\brand\logo.ico",
            Validation = true,
            Logging = false,
            SampleSeedData = true,
            Login = true,
            Transactions = "Order",
            Masters = "Customer",
            NamespacePrefix = "CodisLtd",
            Layout = AppLayout.Sidebar,
            EntryTemplate = AppScaffolding.Screens.EntryTemplate.Grouped,
        };

        var typed = AppCustomization.Default with
        {
            Company = "Acme",
            Theme = AppTheme.Dark,
            Accent = "#123456",
            TitlePattern = "{app} ({company})",
            IconPath = @"D:\acme.ico",
            Validation = false,
            Logging = true,
            SampleSeedData = false,
            Login = false,
            Transactions = "Invoice",
            Masters = "Supplier",
            NamespacePrefix = "Acme",
            Layout = AppLayout.Dashboard,
            EntryTemplate = AppScaffolding.Screens.EntryTemplate.Classic,
        };

        foreach (var property in typeof(AppCustomization).GetProperties()
                     .Where(p => p.CanWrite && p.GetMethod!.IsPublic))
            Assert.AreNotEqual(
                property.GetValue(saved), property.GetValue(typed),
                $"{property.Name} needs a different typed value here, or this test cannot see it");

        Assert.AreEqual(typed, BuildAppCommand.Merge(saved, typed));
    }
}
