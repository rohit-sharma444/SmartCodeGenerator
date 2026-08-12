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

    /// <summary>Regression for a crash found by the review probe: a lone quote character is not a
    /// closing partner for anything, so it must be treated as an ordinary token rather than sent
    /// through LastIndexOf with a negative start index.</summary>
    [TestMethod]
    public void A_lone_quote_does_not_crash_the_scan()
    {
        var (remaining, customization) = Split("\" theme dark");

        Assert.AreEqual("\"", remaining);
        Assert.AreEqual(AppTheme.Dark, customization.Theme);
    }

    /// <summary>Regression for a review finding: an unmatched opening quote must not leave a
    /// literal quote character in the value, since that value flows into generated titles and
    /// project metadata.</summary>
    [TestMethod]
    public void An_unmatched_opening_quote_is_stripped_from_the_value()
    {
        var (remaining, customization) = Split("a shop company \"Codis");

        Assert.AreEqual("a shop", remaining);
        Assert.AreEqual("Codis", customization.Company);
    }

    /// <summary>A quoted location is already immune to option-stripping: the trailing quote makes
    /// the whole path one atomic token, so a flag word inside it is never seen as a separate,
    /// trailing token.</summary>
    [TestMethod]
    public void A_quoted_location_survives_intact()
    {
        var (remaining, customization) = Split(
            "orders from csv \"C:\\Data\\audit logging\" company \"Codis Ltd\" noseed");

        Assert.AreEqual("orders from csv \"C:\\Data\\audit logging\"", remaining);
        Assert.AreEqual("Codis Ltd", customization.Company);
        Assert.IsFalse(customization.SampleSeedData);
    }

    /// <summary>Documented limitation, ruled to stay as-is rather than grow a path-detection
    /// heuristic: an UNQUOTED location whose final word matches a flag is truncated, because
    /// nothing marks where the location ends and the option scan begins. Quoting the location, as
    /// <see cref="A_quoted_location_survives_intact"/> does, is the fix.</summary>
    [TestMethod]
    public void An_unquoted_location_ending_in_a_flag_word_is_truncated()
    {
        var (remaining, customization) = Split(@"orders from csv C:\Data\audit logging");

        Assert.AreEqual(@"orders from csv C:\Data\audit", remaining);
        Assert.IsTrue(customization.Logging);
    }

    [TestMethod]
    public void A_namespace_prefix_is_read()
    {
        var (remaining, customization) = Split("a shop namespace CodisLtd");

        Assert.AreEqual("a shop", remaining);
        Assert.AreEqual("CodisLtd", customization.NamespacePrefix);
        Assert.AreEqual("CodisLtd.Shop", customization.RootNamespaceFor("Shop"));
    }

    [TestMethod]
    public void A_dotted_namespace_prefix_is_read()
    {
        var (_, customization) = Split("a shop namespace \"Codis.Internal\"");

        Assert.AreEqual("Codis.Internal", customization.NamespacePrefix);
    }

    /// <summary>Unlike a malformed accent, which is ignored, a malformed namespace cannot be
    /// accepted at all — it would not compile. The words stay part of the description, which is
    /// what `theme mauve` already does.</summary>
    [TestMethod]
    public void A_malformed_namespace_prefix_is_left_on_the_line()
    {
        var (remaining, customization) = Split("a shop namespace 3M");

        Assert.AreEqual("a shop namespace 3M", remaining);
        Assert.AreEqual(string.Empty, customization.NamespacePrefix);
    }

    /// <summary>Ruling: "none" is the vocabulary the wizard already teaches for clearing a value,
    /// and inline was otherwise the only surface with no way at all to turn off a remembered
    /// prefix from a piped run. `IsValid("none")` is true, so without this it would be accepted as
    /// a literal (and broken) namespace segment instead.</summary>
    [TestMethod]
    public void Namespace_none_clears_a_remembered_prefix()
    {
        var (remaining, customization) = InlineCustomization.Split(
            "a shop namespace none", AppCustomization.Default with { NamespacePrefix = "CodisLtd" });

        Assert.AreEqual("a shop", remaining);
        Assert.AreEqual(string.Empty, customization.NamespacePrefix);
    }
}
