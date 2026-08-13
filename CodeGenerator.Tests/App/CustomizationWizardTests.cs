using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>The opt-in gate and its nine prompts, driven through a TextReader like
/// BuildAppWizard is.</summary>
[TestClass]
public class CustomizationWizardTests
{
    private static AppCustomization? Run(string input, AppCustomization? start = null)
        => CustomizationWizard.Run(
            new StringReader(input), new StringWriter(), start ?? AppCustomization.Default);

    /// <summary>As <see cref="Run(string, AppCustomization?)"/>, for a named target, keeping the
    /// prompts it wrote.</summary>
    private static (AppCustomization? Result, string Output) RunFor(
        ITargetScaffolder target, string input, AppCustomization? start = null)
    {
        var writer = new StringWriter();

        var result = CustomizationWizard.Run(
            new StringReader(input), writer, start ?? AppCustomization.Default, target);

        return (result, writer.ToString());
    }

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

        // Matched with the gap between label and value left open: the prompts are padded so every
        // caret lands in one column, so the run of spaces between the two is presentation.
        StringAssert.Matches(written, LabelShowing("Company name", "Codis Ltd"));
        StringAssert.Matches(written, LabelShowing("Theme", "dark"));
        // The company here is kept by a blank answer, not typed fresh, so the narrower Fix 4(a)
        // ruling suggests no prefix -- the prompt shows "none", the same as it would with no saved
        // company at all.
        StringAssert.Matches(written, LabelShowing("Namespace prefix", "none"));
        StringAssert.Matches(written, LabelShowing("Validation layer", "yes"));
        StringAssert.Matches(written, LabelShowing("Logging", "no"));
        StringAssert.Matches(written, LabelShowing("Sample seed data", "yes"));
    }

    /// <summary>A prompt offering <paramref name="value"/> for <paramref name="label"/>.</summary>
    private static System.Text.RegularExpressions.Regex LabelShowing(string label, string value)
        => new($@"{System.Text.RegularExpressions.Regex.Escape(label)}\s+\[{System.Text.RegularExpressions.Regex.Escape(value)}\]");

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

    /// <summary>Ruling: a remembered company only earns the prefix suggestion when it is typed
    /// fresh in THIS run. A blank answer echoes back the value the wizard started with, and an
    /// existing user with a saved company and no prefix must not silently adopt one just by
    /// pressing Enter through the wizard — unlike the title-pattern courtesy two lines above,
    /// adopting a prefix moves every namespace in the app and rewrites both csprojs, App.xaml and
    /// the validator stubs on the next regeneration.</summary>
    [TestMethod]
    public void A_remembered_company_kept_by_a_blank_answer_suggests_no_prefix()
    {
        var saved = AppCustomization.Default with { Company = "Codis Ltd" };

        var result = Run("y\n\n\n\n\n\n\n\n\n\n", saved);

        Assert.IsNotNull(result);
        Assert.AreEqual(string.Empty, result.NamespacePrefix);
    }

    /// <summary>The other half of the same ruling: a company typed fresh in this run still earns
    /// the suggestion, exactly as before.</summary>
    [TestMethod]
    public void A_freshly_typed_company_still_suggests_a_prefix()
    {
        var saved = AppCustomization.Default with { Company = "Codis Ltd" };

        var result = Run("y\nAcme Corp\n\n\n\n\n\n\n\n\n", saved);

        Assert.IsNotNull(result);
        Assert.AreEqual("AcmeCorp", result.NamespacePrefix);
    }

    [TestMethod]
    public void The_layout_is_asked_first_on_a_desktop_target()
    {
        var (result, output) = RunFor(new WpfTarget(), "y\nsidebar\n" + new string('\n', 9));

        Assert.IsNotNull(result);
        Assert.AreEqual(AppLayout.Sidebar, result.Layout);

        // Structural, not decoration: it is asked before the company.
        Assert.IsLessThan(
            output.IndexOf("Company", StringComparison.Ordinal),
            output.IndexOf("Layout", StringComparison.Ordinal),
            "layout is the structural choice and is asked first");

        StringAssert.Contains(output, "tabs/sidebar/master-detail/dashboard");
    }

    [TestMethod]
    public void The_layout_prompt_is_skipped_for_a_target_with_no_window()
    {
        var (result, output) = RunFor(new ConsoleTarget(), "y\n" + new string('\n', 9));

        Assert.IsNotNull(result);
        Assert.IsNull(result.Layout);
        Assert.DoesNotContain("Layout", output);
    }

    /// <summary>No target named at all is the case every pre-existing test in this file exercises,
    /// and the reason the parameter is optional: their answers must not shift by one.</summary>
    [TestMethod]
    public void No_target_asks_nothing_about_layout()
    {
        var result = Run("y\n" + new string('\n', 9));

        Assert.IsNotNull(result);
        Assert.IsNull(result.Layout);
    }

    [TestMethod]
    public void An_unusable_layout_answer_keeps_what_was_offered()
    {
        var start = AppCustomization.Default with { Layout = AppLayout.Dashboard };

        var (result, _) = RunFor(new WpfTarget(), "y\nkanban\n" + new string('\n', 9), start);

        Assert.IsNotNull(result);
        Assert.AreEqual(AppLayout.Dashboard, result.Layout);
    }

    [TestMethod]
    public void A_blank_layout_answer_keeps_the_remembered_one()
    {
        var start = AppCustomization.Default with { Layout = AppLayout.MasterDetail };

        var (result, output) = RunFor(new WpfTarget(), "y\n" + new string('\n', 10), start);

        Assert.IsNotNull(result);
        Assert.AreEqual(AppLayout.MasterDetail, result.Layout);

        // Offered as the current value, so pressing Enter through the wizard keeps it.
        StringAssert.Contains(output, "[master-detail]");
    }

    /// <summary>A first run on a desktop target leaves the layout unnamed by pressing Enter, rather
    /// than recording `tabs` — so a later `layout dashboard` on the line still wins the merge.</summary>
    [TestMethod]
    public void A_blank_layout_answer_names_nothing_when_nothing_was_remembered()
    {
        var (result, output) = RunFor(new WpfTarget(), "y\n" + new string('\n', 10));

        Assert.IsNotNull(result);
        Assert.IsNull(result.Layout);
        StringAssert.Contains(output, "[tabs]");
    }

    [TestMethod]
    public void Closed_input_at_the_layout_prompt_returns_null()
        => Assert.IsNull(RunFor(new WpfTarget(), "y\n").Result);

    /// <summary>Every caret in one column, so the block reads as a form rather than as a ragged
    /// list — which is what it was while the accepted values sat inside the labels.</summary>
    [TestMethod]
    public void Every_prompt_puts_its_caret_in_the_same_column()
    {
        var (_, output) = RunFor(new WpfTarget(), "y\n" + new string('\n', 10),
            AppCustomization.Default with { Company = "Codis Ltd", Theme = AppTheme.Dark });

        // Ask writes without a newline -- in a terminal the user's Enter supplies it -- so the
        // prompts arrive as one run of text and the carets have to be measured by the distance
        // BETWEEN them. Reading a single column index off each line would pass on any output at all.
        var block = output[(output.IndexOf("clears an optional one.", StringComparison.Ordinal) + 1)..];

        var carets = System.Text.RegularExpressions.Regex.Matches(block, "> ")
            .Select(match => match.Index)
            .ToList();

        Assert.HasCount(10, carets, "a desktop run asks ten questions");

        var widths = carets.Zip(carets.Skip(1), (a, b) => b - a).Distinct().ToList();

        Assert.HasCount(1, widths,
            "every prompt should be the same width so the carets line up, found widths: "
            + string.Join(", ", widths));
    }

    [TestMethod]
    public void The_accepted_values_are_listed_once_rather_than_inside_every_label()
    {
        var (_, output) = RunFor(new WpfTarget(), "y\n" + new string('\n', 10));

        StringAssert.Contains(output, "Layout: tabs/sidebar/master-detail/dashboard");
        StringAssert.Contains(output, "Theme: light/dark");

        // Inline they are what made the labels differ by thirty characters.
        Assert.DoesNotContain("Theme [light/dark]", output);
        Assert.DoesNotContain("Accent colour (#rrggbb)", output);
    }
}
