using AppScaffolding;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>The opt-in gate and its eight prompts, driven through a TextReader like
/// BuildAppWizard is.</summary>
[TestClass]
public class CustomizationWizardTests
{
    private static AppCustomization? Run(string input, AppCustomization? start = null)
        => CustomizationWizard.Run(
            new StringReader(input), new StringWriter(), start ?? AppCustomization.Default);

    [TestMethod]
    public void Declining_the_gate_leaves_the_start_untouched()
        => Assert.AreEqual(AppCustomization.Default, Run("n\n"));

    [TestMethod]
    public void A_blank_answer_at_the_gate_declines()
        => Assert.AreEqual(AppCustomization.Default, Run("\n"));

    [TestMethod]
    public void Cancelling_at_the_gate_returns_null()
        => Assert.IsNull(Run("c\n"));

    [TestMethod]
    public void Closed_input_returns_null()
        => Assert.IsNull(Run(string.Empty));

    /// <summary>Declining keeps what the command line already asked for — the gate is about
    /// whether to ASK, not whether to customize.</summary>
    [TestMethod]
    public void Declining_the_gate_keeps_inline_options()
    {
        var inline = AppCustomization.Default with { Company = "Codis Ltd", Logging = true };

        Assert.AreEqual(inline, Run("n\n", inline));
    }

    [TestMethod]
    public void Accepting_and_answering_collects_every_setting()
    {
        var result = Run("y\nCodis Ltd\ndark\n#0B6E4F\n\n\n\nyes\nyes\nno\n");

        Assert.IsNotNull(result);
        Assert.AreEqual("Codis Ltd", result.Company);
        Assert.AreEqual(AppTheme.Dark, result.Theme);
        Assert.AreEqual("#0B6E4F", result.Accent);
        Assert.IsTrue(result.Validation);
        Assert.IsTrue(result.Logging);
        Assert.IsFalse(result.SampleSeedData);
    }

    /// <summary>Naming a company but leaving the title at "{app}" would hide the company the user
    /// just typed, so a named company is offered the company pattern.</summary>
    [TestMethod]
    public void Naming_a_company_offers_the_company_title_pattern()
    {
        var result = Run("y\nCodis Ltd\n\n\n\n\n\n\n\n\n");

        Assert.IsNotNull(result);
        Assert.AreEqual("Codis Ltd - Shop", result.TitleFor("Shop"));
    }

    /// <summary>Enter on every prompt accepts the pre-filled profile, which is the point of
    /// remembering it.</summary>
    [TestMethod]
    public void Blank_answers_keep_the_prefilled_values()
    {
        // TitlePattern is set explicitly: a saved company with the bare "{app}" pattern is
        // OFFERED the company pattern, so the round trip would not be an identity. Likewise
        // NamespacePrefix: a saved company with no prefix is OFFERED one derived from the company.
        var saved = AppCustomization.Default with
        {
            Company = "Codis Ltd",
            Theme = AppTheme.Dark,
            TitlePattern = AppCustomization.CompanyTitlePattern,
            NamespacePrefix = "CodisLtd",
        };

        var result = Run("y\n\n\n\n\n\n\n\n\n\n", saved);

        Assert.AreEqual(saved, result);
    }

    [TestMethod]
    public void A_malformed_accent_keeps_the_previous_one_rather_than_failing()
    {
        var result = Run("y\n\n\nnot-a-colour\n\n\n\n\n\n\n");

        Assert.IsNotNull(result);
        Assert.IsNull(result.Accent);
    }

    /// <summary>"none" is how a prompt showing a value clears it — there is no other way to type
    /// "nothing" into a prompt whose blank answer means "keep this".</summary>
    [TestMethod]
    public void None_clears_a_remembered_accent_and_icon()
    {
        var saved = AppCustomization.Default with
        {
            Accent = "#0B6E4F",
            IconPath = "C:\\brand\\logo.ico",
        };

        var result = Run("y\n\n\nnone\nnone\n\n\n\n\n\n", saved);

        Assert.IsNotNull(result);
        Assert.IsNull(result.Accent);
        Assert.IsNull(result.IconPath);
    }

    /// <summary>Stdin closing mid-way is not an answer to the rest of the questions.</summary>
    [TestMethod]
    public void Input_closing_part_way_through_returns_null()
        => Assert.IsNull(Run("y\nCodis Ltd\ndark\n"));

    [TestMethod]
    public void Every_prompt_shows_the_value_it_would_keep()
    {
        var writer = new StringWriter();

        CustomizationWizard.Run(
            new StringReader("y\n\n\n\n\n\n\n\n\n\n"),
            writer,
            AppCustomization.Default with { Company = "Codis Ltd", Theme = AppTheme.Dark });

        var written = writer.ToString();

        StringAssert.Contains(written, "Company name [Codis Ltd]");
        StringAssert.Contains(written, "Theme [light/dark] [dark]");
        StringAssert.Contains(written, "Validation layer [yes]");
        StringAssert.Contains(written, "Logging [no]");
        StringAssert.Contains(written, "Sample seed data [yes]");
    }

    [TestMethod]
    public void A_namespace_prefix_is_collected()
    {
        var result = Run("y\n\n\n\n\n\nCodisLtd\n\n\n\n");

        Assert.IsNotNull(result);
        Assert.AreEqual("CodisLtd", result.NamespacePrefix);
    }

    /// <summary>A company just named is the likeliest prefix, so it is offered — the same courtesy
    /// as a named company adopting the company title pattern. Blank accepts it.</summary>
    [TestMethod]
    public void Naming_a_company_offers_it_as_the_namespace_prefix()
    {
        var result = Run("y\nCodis Ltd\n\n\n\n\n\n\n\n\n");

        Assert.IsNotNull(result);
        Assert.AreEqual("CodisLtd", result.NamespacePrefix);
    }

    /// <summary>A company that cannot become an identifier must not be offered as one: the same
    /// run would refuse it.</summary>
    [TestMethod]
    public void A_company_that_is_not_an_identifier_offers_no_prefix()
    {
        var result = Run("y\n3M\n\n\n\n\n\n\n\n\n");

        Assert.IsNotNull(result);
        Assert.AreEqual(string.Empty, result.NamespacePrefix);
    }

    /// <summary>An explicit prefix is not overwritten by the company suggestion.</summary>
    [TestMethod]
    public void An_existing_prefix_is_kept_over_the_company_suggestion()
    {
        var saved = AppCustomization.Default with { NamespacePrefix = "Acme" };

        var result = Run("y\nCodis Ltd\n\n\n\n\n\n\n\n\n", saved);

        Assert.IsNotNull(result);
        Assert.AreEqual("Acme", result.NamespacePrefix);
    }

    [TestMethod]
    public void A_malformed_namespace_prefix_keeps_the_previous_one()
    {
        var saved = AppCustomization.Default with { NamespacePrefix = "CodisLtd" };

        var result = Run("y\n\n\n\n\n\nnot a namespace\n\n\n\n", saved);

        Assert.IsNotNull(result);
        Assert.AreEqual("CodisLtd", result.NamespacePrefix);
    }

    [TestMethod]
    public void None_clears_a_remembered_namespace_prefix()
    {
        var saved = AppCustomization.Default with { NamespacePrefix = "CodisLtd" };

        var result = Run("y\n\n\n\n\n\nnone\n\n\n\n", saved);

        Assert.IsNotNull(result);
        Assert.AreEqual(string.Empty, result.NamespacePrefix);
    }
}
