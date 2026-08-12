using AppScaffolding;

namespace CodeGenerator.Tests.App;

/// <summary>The customization value types. Default must be neutral: it is what every existing
/// caller passes, so anything it changes changes today's generated output.</summary>
[TestClass]
public class AppCustomizationTests
{
    [TestMethod]
    public void The_default_renders_the_app_name_alone()
        => Assert.AreEqual("Shop", AppCustomization.Default.TitleFor("Shop"));

    [TestMethod]
    public void The_default_themes_nothing()
        => Assert.IsNull(ThemePalette.Resolve(AppTheme.Default, null));

    [TestMethod]
    public void The_default_has_no_branding()
        => Assert.IsFalse(AppCustomization.Default.HasBranding);

    [TestMethod]
    public void A_company_and_pattern_are_substituted()
    {
        var customization = AppCustomization.Default with
        {
            Company = "Codis Ltd",
            TitlePattern = "{company} - {app}",
        };

        Assert.AreEqual("Codis Ltd - Shop", customization.TitleFor("Shop"));
    }

    /// <summary>A pattern naming a company that was never set must not leave a dangling
    /// separator: "{company} - {app}" with no company reads " - Shop" without this.</summary>
    [TestMethod]
    public void A_pattern_with_no_company_collapses_its_separator()
    {
        var customization = AppCustomization.Default with { TitlePattern = "{company} - {app}" };

        Assert.AreEqual("Shop", customization.TitleFor("Shop"));
    }

    [TestMethod]
    public void An_accent_alone_themes_over_the_light_base()
    {
        var palette = ThemePalette.Resolve(AppTheme.Default, "#B91C1C");

        Assert.IsNotNull(palette);
        Assert.AreEqual("#B91C1C", palette.Accent);
        Assert.AreEqual("#FFFFFF", palette.Background);
    }

    [TestMethod]
    public void The_dark_palette_differs_from_the_light_one()
    {
        var dark = ThemePalette.Resolve(AppTheme.Dark, null);
        var light = ThemePalette.Resolve(AppTheme.Light, null);

        Assert.IsNotNull(dark);
        Assert.IsNotNull(light);
        Assert.AreNotEqual(light.Background, dark.Background);
    }

    [TestMethod]
    public void A_malformed_colour_is_rejected()
    {
        Assert.IsTrue(ThemePalette.IsValidHexColour("#0B6E4F"));
        Assert.IsFalse(ThemePalette.IsValidHexColour("0B6E4F"));
        Assert.IsFalse(ThemePalette.IsValidHexColour("#0B6E4"));
        Assert.IsFalse(ThemePalette.IsValidHexColour("#GGGGGG"));
    }

    [TestMethod]
    public void No_company_means_no_file_header()
        => Assert.AreEqual(string.Empty, FileHeader.For(string.Empty, 2026));

    [TestMethod]
    public void A_company_produces_a_one_line_banner()
        => Assert.AreEqual("// Copyright (c) 2026 Codis Ltd.\n", FileHeader.For("Codis Ltd", 2026));
}
