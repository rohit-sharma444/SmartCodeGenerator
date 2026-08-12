using AppScaffolding;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>Inline options are lifted off the line BEFORE InlineDataSource.Parse sees it,
/// because that treats everything after `from &lt;format&gt;` as the location to end of line.</summary>
[TestClass]
public class InlineCustomizationTests
{
    private static (string Remaining, AppCustomization Customization) Split(string tail)
        => InlineCustomization.Split(tail, AppCustomization.Default);

    [TestMethod]
    public void A_line_with_no_options_is_returned_unchanged()
    {
        var (remaining, customization) = Split("a shop that sells books");

        Assert.AreEqual("a shop that sells books", remaining);
        Assert.AreEqual(AppCustomization.Default, customization);
    }

    [TestMethod]
    public void A_quoted_company_is_taken_whole()
    {
        var (remaining, customization) = Split("a shop company \"Codis Ltd\"");

        Assert.AreEqual("a shop", remaining);
        Assert.AreEqual("Codis Ltd", customization.Company);
    }

    /// <summary>Naming a company adopts the company title pattern: a user who says who they are
    /// means the app to say so, and asking again would be a question with one sensible answer.</summary>
    [TestMethod]
    public void A_company_adopts_the_company_title_pattern()
    {
        var (_, customization) = Split("a shop company Codis");

        Assert.AreEqual(AppCustomization.CompanyTitlePattern, customization.TitlePattern);
        Assert.AreEqual("Codis - Shop", customization.TitleFor("Shop"));
    }

    [TestMethod]
    public void An_explicit_title_wins_over_the_company_pattern()
    {
        var (_, customization) = Split("a shop company Codis title \"{app} at {company}\"");

        Assert.AreEqual("Shop at Codis", customization.TitleFor("Shop"));
    }

    [TestMethod]
    public void Theme_and_accent_are_read()
    {
        var (remaining, customization) = Split("a shop theme dark accent #0B6E4F");

        Assert.AreEqual("a shop", remaining);
        Assert.AreEqual(AppTheme.Dark, customization.Theme);
        Assert.AreEqual("#0B6E4F", customization.Accent);
    }

    [TestMethod]
    public void The_three_flags_are_read()
    {
        var (remaining, customization) = Split("a shop novalidation logging noseed");

        Assert.AreEqual("a shop", remaining);
        Assert.IsFalse(customization.Validation);
        Assert.IsTrue(customization.Logging);
        Assert.IsFalse(customization.SampleSeedData);
    }

    /// <summary>Options sit after the data source, and the location must survive intact —
    /// a Windows path contains spaces and is taken as the rest of the line downstream.</summary>
    [TestMethod]
    public void Options_are_stripped_from_after_a_data_source()
    {
        var (remaining, customization) = Split(
            "orders from csv D:\\my data\\orders.csv company \"Codis Ltd\" noseed");

        Assert.AreEqual("orders from csv D:\\my data\\orders.csv", remaining);
        Assert.AreEqual("Codis Ltd", customization.Company);
        Assert.IsFalse(customization.SampleSeedData);
    }

    /// <summary>`normalize` belongs to InlineDataSource and must be left where it is.</summary>
    [TestMethod]
    public void Normalize_is_left_on_the_line()
    {
        var (remaining, _) = Split("orders from csv orders.csv normalize");

        Assert.AreEqual("orders from csv orders.csv normalize", remaining);
    }

    [TestMethod]
    public void Scanning_stops_at_the_first_unrecognized_token()
    {
        var (remaining, customization) = Split("a shop theme dark and a warehouse");

        Assert.AreEqual("a shop theme dark and a warehouse", remaining);
        Assert.AreEqual(AppTheme.Default, customization.Theme);
    }

    /// <summary>Documented limitation, asserted rather than pretended away: a description whose
    /// final words spell a valid option is read as one. Quoting or reordering avoids it.</summary>
    [TestMethod]
    public void A_description_ending_in_an_option_is_misread()
    {
        var (remaining, customization) = Split("track theme dark");

        Assert.AreEqual("track", remaining);
        Assert.AreEqual(AppTheme.Dark, customization.Theme);
    }

    [TestMethod]
    public void An_unknown_theme_value_leaves_the_theme_alone_and_stops_the_scan()
    {
        var (remaining, customization) = Split("a shop theme mauve");

        Assert.AreEqual("a shop theme mauve", remaining);
        Assert.AreEqual(AppTheme.Default, customization.Theme);
    }

    [TestMethod]
    public void An_icon_path_is_read()
    {
        var (remaining, customization) = Split("a shop icon \"C:\\brand\\logo.ico\"");

        Assert.AreEqual("a shop", remaining);
        Assert.AreEqual("C:\\brand\\logo.ico", customization.IconPath);
    }
}
