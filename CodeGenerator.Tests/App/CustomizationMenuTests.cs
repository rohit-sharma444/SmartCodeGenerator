using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Targets;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The customization step as drop-downs: every question a list, with the value the user already
/// has pre-selected.
/// </summary>
/// <remarks>
/// The chooser is a delegate for the same reason the application-type menu is one — it is the step
/// that needs raw key input, which a test cannot supply. What the fake records is the whole
/// contract: the title asked, the rows offered, and which row was pre-selected.
/// </remarks>
[TestClass]
public class CustomizationMenuTests
{
    /// <summary>Answer a question by taking the row it pre-selected — Enter on a real list.</summary>
    private const int Keep = -1;

    /// <summary>A chooser that answers from a script and remembers what it was asked.</summary>
    private sealed class FakeChooser(params int?[] answers)
    {
        private readonly Queue<int?> _answers = new(answers);

        public List<(string Title, IReadOnlyList<string> Labels, int Default)> Asked { get; } = [];

        public int? Choose(string title, IReadOnlyList<string> labels, int defaultIndex)
        {
            Asked.Add((title, labels, defaultIndex));

            // Running out means the flow asked something this test did not expect, which is the
            // failure worth reporting — not an exception from an empty queue.
            Assert.IsGreaterThan(0, _answers.Count, $"unscripted question: {title}");

            var answer = _answers.Dequeue();

            return answer == Keep ? defaultIndex : answer;
        }

        /// <summary>The rows offered for <paramref name="title"/>, and the one pre-selected.</summary>
        public (IReadOnlyList<string> Labels, int Default) Question(string title)
        {
            var match = Asked.FirstOrDefault(a => a.Title == title);

            Assert.IsNotNull(
                match.Labels,
                $"never asked '{title}'; asked: {string.Join(", ", Asked.Select(a => a.Title))}");

            return (match.Labels, match.Default);
        }
    }

    private const string Gate = "Customize appearance and features?";

    /// <summary>The questions asked after the gate, in order, for a target with no window.</summary>
    private static readonly string[] Questions =
    [
        "Company name", "Theme", "Accent colour", "Icon", "Window title", "Namespace prefix",
        "Validation layer", "Logging", "Sample seed data",
    ];

    /// <summary>Accept the gate, then take the pre-selected row at every question.</summary>
    private static int?[] KeepEverything()
        => [0, .. Enumerable.Repeat<int?>(Keep, Questions.Length)];

    /// <summary>As <see cref="KeepEverything"/>, with one question answered differently.</summary>
    private static int?[] Answering(string question, int? row)
    {
        var answers = KeepEverything();

        answers[Array.IndexOf(Questions, question) + 1] = row;

        return answers;
    }

    /// <summary>As <see cref="KeepEverything"/>, for a target that is also asked its layout.</summary>
    private static int?[] KeepEverythingWithLayout()
        => [0, Keep, .. Enumerable.Repeat<int?>(Keep, Questions.Length)];

    private static (AppCustomization? Result, FakeChooser Chooser) Run(
        AppCustomization start, int?[] answers, ITargetScaffolder? target = null,
        string typed = "")
    {
        var chooser = new FakeChooser(answers);

        var result = CustomizationWizard.Run(
            new StringReader(typed), new StringWriter(), start, target, chooser.Choose);

        return (result, chooser);
    }

    [TestMethod]
    public void The_gate_is_a_menu_defaulting_to_no()
    {
        var (result, chooser) = Run(AppCustomization.Default, [1]);

        var (labels, byDefault) = chooser.Question(Gate);

        CollectionAssert.AreEqual(new[] { "Yes", "No" }, labels.ToList());

        // Declining is what a run gets by pressing Enter, exactly as the typed prompt's blank line
        // did: this step is optional decoration on a build already asked for.
        Assert.AreEqual(1, byDefault);
        Assert.AreEqual(AppCustomization.Default, result);
    }

    [TestMethod]
    public void Declining_the_gate_keeps_what_the_command_line_asked_for()
    {
        var inline = AppCustomization.Default with { Company = "Codis Ltd", Logging = true };

        // The gate decides whether to ASK, not whether to customize.
        var (result, chooser) = Run(inline, [1]);

        Assert.AreEqual(inline, result);
        Assert.HasCount(1, chooser.Asked);
    }

    [TestMethod]
    public void Escape_at_the_gate_cancels()
        => Assert.IsNull(Run(AppCustomization.Default, [null]).Result);

    [TestMethod]
    public void Escape_at_a_later_question_cancels_the_whole_step()
        => Assert.IsNull(Run(AppCustomization.Default, Answering("Theme", null)).Result);

    [TestMethod]
    public void Taking_every_default_keeps_the_profile_it_started_with()
    {
        var saved = AppCustomization.Default with
        {
            Company = "Codis Ltd",
            Theme = AppTheme.Dark,
            Accent = "#0B6E4F",
            TitlePattern = AppCustomization.CompanyTitlePattern,
            NamespacePrefix = "CodisLtd",
            Logging = true,
        };

        var (result, _) = Run(saved, KeepEverything());

        // The point of remembering a profile: arrowing through it changes nothing.
        Assert.AreEqual(saved, result);
    }

    [TestMethod]
    public void Every_question_is_asked_as_a_menu_in_the_order_the_typed_prompts_used()
    {
        var (_, chooser) = Run(AppCustomization.Default, KeepEverything());

        string[] expected = [Gate, .. Questions];

        CollectionAssert.AreEqual(expected, chooser.Asked.Select(a => a.Title).ToList());
    }

    [TestMethod]
    public void The_theme_menu_preselects_the_theme_in_hand()
    {
        var (_, chooser) = Run(
            AppCustomization.Default with { Theme = AppTheme.Dark }, KeepEverything());

        var (labels, byDefault) = chooser.Question("Theme");

        CollectionAssert.AreEqual(new[] { "None", "Light", "Dark" }, labels.ToList());
        Assert.AreEqual(2, byDefault);
    }

    [TestMethod]
    public void Choosing_a_theme_row_sets_it()
    {
        var (result, _) = Run(AppCustomization.Default, Answering("Theme", 1));

        Assert.IsNotNull(result);
        Assert.AreEqual(AppTheme.Light, result.Theme);
    }

    [TestMethod]
    public void A_yes_no_question_is_a_menu_preselecting_the_current_answer()
    {
        var (_, chooser) = Run(
            AppCustomization.Default with { Logging = true }, KeepEverything());

        var (labels, byDefault) = chooser.Question("Logging");

        CollectionAssert.AreEqual(new[] { "Yes", "No" }, labels.ToList());
        Assert.AreEqual(0, byDefault);

        // Validation is on by default and sample data with it, so their rows are the same answer.
        Assert.AreEqual(0, chooser.Question("Validation layer").Default);
        Assert.AreEqual(0, chooser.Question("Sample seed data").Default);
    }

    [TestMethod]
    public void Turning_a_yes_no_question_off()
    {
        var (result, _) = Run(AppCustomization.Default, Answering("Validation layer", 1));

        Assert.IsNotNull(result);
        Assert.IsFalse(result.Validation);

        // Only the question that was answered changed.
        Assert.IsTrue(result.SampleSeedData);
    }

    [TestMethod]
    public void A_value_question_offers_keep_none_and_type()
    {
        var (_, chooser) = Run(
            AppCustomization.Default with { Accent = "#0B6E4F" }, KeepEverything());

        var (labels, byDefault) = chooser.Question("Accent colour");

        // Keeping is the pre-selected row, so arrowing past a question never loses its value.
        Assert.AreEqual(0, byDefault);
        StringAssert.Contains(labels[0], "#0B6E4F");
        CollectionAssert.Contains(labels.ToList(), "None");
        Assert.IsTrue(labels[^1].StartsWith("Type", StringComparison.Ordinal), labels[^1]);
    }

    [TestMethod]
    public void A_value_question_with_nothing_to_keep_offers_only_none_and_type()
    {
        var (_, chooser) = Run(AppCustomization.Default, KeepEverything());

        var (labels, byDefault) = chooser.Question("Accent colour");

        CollectionAssert.AreEqual(new[] { "None", "Type a colour" }, labels.ToList());
        Assert.AreEqual(0, byDefault);
    }

    [TestMethod]
    public void The_none_row_clears_an_optional_value()
    {
        var saved = AppCustomization.Default with { Accent = "#0B6E4F" };

        // Accent's rows are Keep / None / Type, so None is row 1.
        var (result, _) = Run(saved, Answering("Accent colour", 1));

        Assert.IsNotNull(result);
        Assert.IsNull(result.Accent);
    }

    [TestMethod]
    public void The_type_row_asks_for_the_value_at_the_prompt()
    {
        // With nothing to keep, Company's rows are None / Type a name — so typing is row 1.
        var (result, _) = Run(
            AppCustomization.Default, Answering("Company name", 1), typed: "Codis Ltd\n");

        Assert.IsNotNull(result);
        Assert.AreEqual("Codis Ltd", result.Company);
    }

    [TestMethod]
    public void A_typed_value_that_cannot_be_used_keeps_the_one_in_hand()
    {
        var saved = AppCustomization.Default with { Accent = "#0B6E4F" };

        // Row 2 is "Type a colour" when there is one to keep. "blue" is not #rrggbb.
        var (result, _) = Run(saved, Answering("Accent colour", 2), typed: "blue\n");

        Assert.IsNotNull(result);
        Assert.AreEqual("#0B6E4F", result.Accent);
    }

    [TestMethod]
    public void Closing_input_at_a_typed_value_cancels()
    {
        var (result, _) = Run(
            AppCustomization.Default, Answering("Company name", 1), typed: string.Empty);

        // EOF is the absence of an answer, not an empty one.
        Assert.IsNull(result);
    }

    [TestMethod]
    public void The_layout_question_is_asked_only_for_a_target_with_a_window()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("console", out var console));
        Assert.IsTrue(TargetScaffolders.TryResolve("wpf", out var wpf));

        var (_, withoutWindow) = Run(AppCustomization.Default, KeepEverything(), console);
        Assert.IsFalse(withoutWindow.Asked.Any(a => a.Title == "Layout"));

        var (_, withWindow) = Run(AppCustomization.Default, KeepEverythingWithLayout(), wpf);

        var (labels, _) = withWindow.Question("Layout");

        Assert.HasCount(AppLayoutCatalog.Keys.Count, labels);
        StringAssert.Contains(labels[0], "tabs");
    }

    [TestMethod]
    public void The_layout_menu_preselects_the_remembered_layout()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("wpf", out var wpf));

        var saved = AppCustomization.Default with { Layout = AppLayout.Dashboard };

        var (result, chooser) = Run(saved, KeepEverythingWithLayout(), wpf);

        Assert.AreEqual(
            AppLayoutCatalog.Keys.ToList().IndexOf("dashboard"),
            chooser.Question("Layout").Default);

        Assert.IsNotNull(result);
        Assert.AreEqual(AppLayout.Dashboard, result.Layout);
    }

    /// <summary>
    /// Enter on a run with no remembered layout must leave it UNNAMED rather than recording
    /// `tabs` — the review summary then stays quiet for a user who customized nothing, and a
    /// `layout tabs` typed against a remembered dashboard still reads as the override it is.
    /// </summary>
    [TestMethod]
    public void Taking_the_default_layout_row_names_no_layout_at_all()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("wpf", out var wpf));

        var (result, _) = Run(AppCustomization.Default, KeepEverythingWithLayout(), wpf);

        Assert.IsNotNull(result);
        Assert.IsNull(result.Layout);
    }

    [TestMethod]
    public void The_title_menu_offers_both_patterns_and_preselects_the_company_one_for_a_fresh_company()
    {
        // Naming a company but leaving the title at "{app}" would hide the company just typed.
        var (result, chooser) = Run(
            AppCustomization.Default, Answering("Company name", 1), typed: "Codis Ltd\n");

        var (labels, byDefault) = chooser.Question("Window title");

        Assert.IsTrue(labels.Any(
            l => l.Contains(AppCustomization.AppPlaceholder, StringComparison.Ordinal)));

        Assert.IsTrue(labels[byDefault].Contains(
            AppCustomization.CompanyPlaceholder, StringComparison.Ordinal));

        Assert.IsNotNull(result);
        Assert.AreEqual("Codis Ltd - Shop", result.TitleFor("Shop"));
    }

    [TestMethod]
    public void The_prefix_menu_offers_the_name_derived_from_a_fresh_company()
    {
        var (result, chooser) = Run(
            AppCustomization.Default, Answering("Company name", 1), typed: "Codis Ltd\n");

        var (labels, byDefault) = chooser.Question("Namespace prefix");

        StringAssert.Contains(labels[byDefault], "CodisLtd");

        Assert.IsNotNull(result);
        Assert.AreEqual("CodisLtd", result.NamespacePrefix);
    }

    /// <summary>
    /// A saved company with no prefix must not silently adopt one: taking the default row is not
    /// asking for a prefix, and adopting one moves every namespace in the app.
    /// </summary>
    [TestMethod]
    public void A_remembered_company_is_not_offered_a_prefix()
    {
        var saved = AppCustomization.Default with { Company = "Codis Ltd" };

        var (result, chooser) = Run(saved, KeepEverything());

        var (labels, byDefault) = chooser.Question("Namespace prefix");

        Assert.AreEqual("None", labels[byDefault]);

        Assert.IsNotNull(result);
        Assert.IsEmpty(result.NamespacePrefix);
    }
}
