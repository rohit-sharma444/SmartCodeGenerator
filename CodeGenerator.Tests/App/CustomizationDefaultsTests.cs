using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Screens;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>The remembered profile. A broken one must leave the CLI working exactly as it does
/// without one — the precedent AiConfig.Load sets.</summary>
[TestClass]
public class CustomizationDefaultsTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateWorkspace()
    {
        _root = Path.Combine(Path.GetTempPath(), "cust-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void RemoveWorkspace()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [TestMethod]
    public void No_profile_yields_the_defaults()
        => Assert.AreEqual(AppCustomization.Default, CustomizationDefaults.Load(_root));

    [TestMethod]
    public void A_saved_profile_round_trips()
    {
        var saved = AppCustomization.Default with
        {
            Company = "Codis Ltd",
            Theme = AppTheme.Dark,
            Accent = "#0B6E4F",
            TitlePattern = AppCustomization.CompanyTitlePattern,
            Logging = true,
            SampleSeedData = false,
            NamespacePrefix = "CodisLtd",
        };

        CustomizationDefaults.Save(_root, saved);

        Assert.AreEqual(saved, CustomizationDefaults.Load(_root));
    }

    [TestMethod]
    public void The_profile_is_written_under_dot_codegen()
    {
        CustomizationDefaults.Save(_root, AppCustomization.Default with { Company = "Codis" });

        Assert.IsTrue(File.Exists(Path.Combine(_root, ".codegen", CustomizationDefaults.FileName)));
    }

    [TestMethod]
    public void A_corrupt_profile_yields_the_defaults()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".codegen"));
        File.WriteAllText(
            Path.Combine(_root, ".codegen", CustomizationDefaults.FileName), "{ not json");

        Assert.AreEqual(AppCustomization.Default, CustomizationDefaults.Load(_root));
    }

    /// <summary>A profile written by an older build, missing the fields a newer one added,
    /// must fill them from the defaults rather than with nulls.</summary>
    [TestMethod]
    public void A_partial_profile_fills_the_rest_from_the_defaults()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".codegen"));
        File.WriteAllText(
            Path.Combine(_root, ".codegen", CustomizationDefaults.FileName),
            "{ \"company\": \"Codis Ltd\" }");

        var loaded = CustomizationDefaults.Load(_root);

        Assert.AreEqual("Codis Ltd", loaded.Company);
        Assert.IsTrue(loaded.Validation);
        Assert.AreEqual(AppTheme.Default, loaded.Theme);
    }

    [TestMethod]
    public void Saving_into_an_unwritable_root_does_not_throw()
        => CustomizationDefaults.Save(
            Path.Combine(_root, "no", "such", "\0bad"), AppCustomization.Default);

    /// <summary>Every other surface validates a namespace prefix — <c>InlineCustomization</c>
    /// refuses it and the wizard's <c>ParsePrefix</c> keeps the previous value. A hand-edited
    /// profile is not exempt: an invalid stored prefix must not flow into a namespace declaration
    /// that will not compile.</summary>
    [TestMethod]
    public void An_invalid_namespace_prefix_loads_as_the_default()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".codegen"));
        File.WriteAllText(
            Path.Combine(_root, ".codegen", CustomizationDefaults.FileName),
            "{ \"namespacePrefix\": \"Codis Ltd\" }");

        var loaded = CustomizationDefaults.Load(_root);

        // Null, not string.Empty: the fallback IS AppCustomization.Default now that NamespacePrefix
        // is nullable, and null there means "not mentioned" rather than "explicitly cleared".
        Assert.IsNull(loaded.NamespacePrefix);
    }

    [TestMethod]
    public void A_layout_round_trips_through_the_profile()
    {
        CustomizationDefaults.Save(
            _root, AppCustomization.Default with { Layout = AppLayout.MasterDetail });

        Assert.AreEqual(AppLayout.MasterDetail, CustomizationDefaults.Load(_root).Layout);
    }

    /// <summary>A hand-edited profile bypasses the inline parser and the wizard, so Load
    /// re-validates — the rule the namespace prefix already follows.</summary>
    [TestMethod]
    public void An_unrecognized_stored_layout_falls_back_to_none()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".codegen"));
        File.WriteAllText(
            Path.Combine(_root, ".codegen", CustomizationDefaults.FileName),
            "{ \"layout\": \"kanban\" }");

        Assert.IsNull(CustomizationDefaults.Load(_root).Layout);
    }

    /// <summary>A profile that names no layout must not gain one, or a later `layout tabs` would
    /// stop reading as typed.</summary>
    [TestMethod]
    public void A_profile_with_no_layout_stays_without_one()
    {
        CustomizationDefaults.Save(_root, AppCustomization.Default with { Company = "Codis Ltd" });

        Assert.IsNull(CustomizationDefaults.Load(_root).Layout);
        Assert.DoesNotContain(
            "\"layout\"",
            File.ReadAllText(Path.Combine(_root, ".codegen", CustomizationDefaults.FileName)),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A remembered layout is announced, because it overrules the default silently.
    /// </summary>
    /// <remarks>
    /// `EffectiveLayout` is `Layout ?? Business`, so a profile written before the business shell
    /// became the default keeps its owner on the old shape for good — and the only workspace the
    /// flip reaches is one with no profile, which is what every test is. Reported from real use:
    /// three runs of a `dashboard` profile, each looking as though the shell had never shipped.
    /// </remarks>
    [TestMethod]
    public void A_remembered_layout_is_announced()
    {
        var notice = CustomizationDefaults.RememberedLayoutNotice(
            _root, AppCustomization.Default with { Layout = AppLayout.Dashboard }, null);

        Assert.IsNotNull(notice);
        StringAssert.Contains(notice, "dashboard");

        // Named so it can be edited or deleted: the file is the whole reason the layout is stuck.
        StringAssert.Contains(notice, CustomizationDefaults.FileName);

        // And what they are missing, or the notice is a fact with nothing to do about it.
        StringAssert.Contains(notice, "business");

        // The whole sentence once, because every part of it is load-bearing: which layout is in
        // force, where that came from, what the default is now, and the two ways to change it.
        Assert.AreEqual(
            $"  Using the dashboard layout, remembered from "
            + $"{Path.Combine(_root, ".codegen", CustomizationDefaults.FileName)}. "
            + "The default is now business — a dashboard home, a rail grouped into masters and "
            + "transactions, and an editor for every record."
            + " Choose business at the Layout step to switch, or delete that file to forget it.",
            notice);
    }

    [TestMethod]
    public void A_remembered_business_layout_is_not_announced()
        => Assert.IsNull(CustomizationDefaults.RememberedLayoutNotice(
            _root, AppCustomization.Default with { Layout = AppLayout.Business }, null));

    /// <summary>The overwhelmingly common case: nothing to say, and a line here would appear on
    /// every interactive run forever.</summary>
    [TestMethod]
    public void A_profile_naming_no_layout_is_not_announced()
        => Assert.IsNull(
            CustomizationDefaults.RememberedLayoutNotice(_root, AppCustomization.Default, null));

    /// <summary>Every older layout is worth announcing, not just the one that was reported.</summary>
    [TestMethod]
    public void Every_older_layout_is_announced()
    {
        foreach (var layout in new[]
                 { AppLayout.Tabs, AppLayout.Sidebar, AppLayout.MasterDetail, AppLayout.Dashboard })
        {
            var notice = CustomizationDefaults.RememberedLayoutNotice(
                _root, AppCustomization.Default with { Layout = layout }, null);

            Assert.IsNotNull(notice, $"a remembered {layout} layout says nothing");
            StringAssert.Contains(notice, AppLayoutCatalog.KeyFor(layout));
        }
    }

    /// <summary>
    /// A layout typed on the command line is not a remembered one.
    /// </summary>
    /// <remarks>
    /// The notice is computed from the SAVED profile, not from the merged customization the run
    /// goes on to use — `layout dashboard` on the line would otherwise be reported as something
    /// the profile did, to the one user who cannot be confused about it.
    /// </remarks>
    [TestMethod]
    public void A_layout_named_on_the_line_is_not_credited_to_the_profile()
        => Assert.IsNull(CustomizationDefaults.RememberedLayoutNotice(
            _root,
            AppCustomization.Default with { Layout = AppLayout.Dashboard },
            AppLayout.Sidebar));

    [TestMethod]
    public void An_entry_template_round_trips_through_the_profile()
    {
        CustomizationDefaults.Save(
            _root, AppCustomization.Default with { EntryTemplate = EntryTemplate.TwoColumn });

        Assert.AreEqual(EntryTemplate.TwoColumn, CustomizationDefaults.Load(_root).EntryTemplate);
    }

    /// <summary>Every other surface for this feature — the inline CLI token, the wizard prompt,
    /// the menu row, the help text — uses the hyphenated <see cref="EntryTemplateCatalog"/>
    /// spelling ("two-column"), and this file's own doc-comments say the profile can be
    /// hand-edited. A profile must persist and re-read that exact spelling, not the bare enum
    /// name ("TwoColumn") a case-sensitive round trip would previously have hidden behind.</summary>
    [TestMethod]
    public void An_entry_template_is_persisted_using_the_hyphenated_catalog_key()
    {
        CustomizationDefaults.Save(
            _root, AppCustomization.Default with { EntryTemplate = EntryTemplate.TwoColumn });

        var written = File.ReadAllText(
            Path.Combine(_root, ".codegen", CustomizationDefaults.FileName));

        StringAssert.Contains(written, "two-column");
        Assert.DoesNotContain("TwoColumn", written);
    }

    /// <summary>A user who hand-edits the profile has only ever been shown the hyphenated
    /// spelling — this proves it is actually understood, not silently ignored back to the
    /// default.</summary>
    [TestMethod]
    public void A_hand_typed_hyphenated_entry_template_loads_correctly()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".codegen"));
        File.WriteAllText(
            Path.Combine(_root, ".codegen", CustomizationDefaults.FileName),
            "{ \"entryTemplate\": \"two-column\" }");

        Assert.AreEqual(EntryTemplate.TwoColumn, CustomizationDefaults.Load(_root).EntryTemplate);
    }

    /// <summary>A hand-edited profile bypasses the inline parser and the wizard, so Load
    /// re-validates — the rule the layout already follows.</summary>
    [TestMethod]
    public void An_unrecognized_stored_entry_template_falls_back_to_none()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".codegen"));
        File.WriteAllText(
            Path.Combine(_root, ".codegen", CustomizationDefaults.FileName),
            "{ \"entryTemplate\": \"carousel\" }");

        Assert.IsNull(CustomizationDefaults.Load(_root).EntryTemplate);
    }
}
