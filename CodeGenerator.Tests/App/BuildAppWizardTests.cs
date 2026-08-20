using AppScaffolding;
using CodeGenerator.App;
using DataIngestion;
using DataIngestion.Schema.Readers;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The wizard's flow. Every scripted input here ends with one more line than the questions the
/// wizard used to ask: the customization step is last, and its gate reads a line before the
/// wizard can return. "n" declines it, which is what these tests are about not being asked.
/// </summary>
[TestClass]
public class BuildAppWizardTests
{
    private static InlineBuildRequest Bare() => new(string.Empty, null, false);

    private static string TempCsv()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        File.WriteAllText(path, "Id,Name\n1,Ada\n");
        return path;
    }

    /// <summary>
    /// The reported bug. Bare `build app` used to open the data interview and then, on "no",
    /// print a usage hint about a description it had never asked for.
    /// </summary>
    [TestMethod]
    public void BareBuildApp_AsksForTheDescriptionBeforeAskingAboutData()
    {
        var writer = new StringWriter();

        var outcome = BuildAppWizard.Run(
            Bare(), new StringReader("a library lending system\nn\nn\n"), writer, () => 0);

        var written = writer.ToString();
        var description = written.IndexOf("Describe the application", StringComparison.Ordinal);
        var data = written.IndexOf("Do you have existing data?", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, description, "the description was never asked for");
        Assert.IsGreaterThanOrEqualTo(0, data, "the data question was never asked");
        Assert.IsGreaterThan(description, data,
            $"the data question came first (description at {description}, data at {data})");

        Assert.AreEqual(WizardDecision.Proceed, outcome.Decision);
        Assert.AreEqual("a library lending system", outcome.Description);
    }

    [TestMethod]
    public void AnInlineDescription_IsAcceptedWithoutBeingAskedAgain()
    {
        var writer = new StringWriter();

        var outcome = BuildAppWizard.Run(
            new InlineBuildRequest("a bike shop", null, false),
            new StringReader("n\nn\n"), writer, () => 0);

        Assert.AreEqual("a bike shop", outcome.Description);
        Assert.IsFalse(
            writer.ToString().Contains("Describe the application", StringComparison.Ordinal),
            "typing a description should stay a shortcut, not something to retype");
    }

    [TestMethod]
    public void TheChosenApplicationType_BecomesTheTarget()
    {
        var outcome = BuildAppWizard.Run(
            Bare(), new StringReader("a shop\nn\nn\n"), new StringWriter(), () => 1);

        // Index 1 is "Windows Desktop (WinForms)".
        Assert.AreEqual("winforms", outcome.Target!.Key);
    }

    [TestMethod]
    public void ABlankDescription_IsAllowedSoDataCanSpeakForItself()
    {
        var path = TempCsv();
        try
        {
            var outcome = BuildAppWizard.Run(
                Bare(), new StringReader($"\ny\ncsv\n{path}\nn\n"), new StringWriter(), () => 3);

            Assert.AreEqual(WizardDecision.Proceed, outcome.Decision);
            Assert.AreEqual(string.Empty, outcome.Description);
            Assert.AreEqual(DataSourceKind.Csv, outcome.Source!.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AnInlineSource_SkipsTheDataQuestion()
    {
        var path = TempCsv();
        var writer = new StringWriter();
        try
        {
            var outcome = BuildAppWizard.Run(
                new InlineBuildRequest("orders", new DataSourceSpec(DataSourceKind.Csv, path), true),
                new StringReader("n\n"), writer, () => 0);

            Assert.AreEqual(WizardDecision.Proceed, outcome.Decision);
            Assert.AreEqual(path, outcome.Source!.Location);
            Assert.IsTrue(outcome.Normalize);
            Assert.IsFalse(
                writer.ToString().Contains("Do you have existing data?", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void CancellingAtTheDescription_Cancels()
    {
        var outcome = BuildAppWizard.Run(
            Bare(), new StringReader("c\n"), new StringWriter(), () => 0);

        Assert.AreEqual(WizardDecision.Cancelled, outcome.Decision);
    }

    [TestMethod]
    public void CancellingAtTheApplicationType_Cancels()
    {
        var outcome = BuildAppWizard.Run(
            Bare(), new StringReader("a shop\n"), new StringWriter(), () => null);

        Assert.AreEqual(WizardDecision.Cancelled, outcome.Decision);
    }

    [TestMethod]
    public void CancellingAtTheDataQuestion_Cancels()
    {
        var outcome = BuildAppWizard.Run(
            Bare(), new StringReader("a shop\ny\nc\n"), new StringWriter(), () => 0);

        // Not NoData: someone who backed out must not be handed a Wikipedia build they never
        // asked for.
        Assert.AreEqual(WizardDecision.Cancelled, outcome.Decision);
    }

    [TestMethod]
    public void EndOfInputAtTheDescription_Cancels()
    {
        var outcome = BuildAppWizard.Run(
            Bare(), new StringReader(""), new StringWriter(), () => 0);

        Assert.AreEqual(WizardDecision.Cancelled, outcome.Decision);
    }

    /// <summary>
    /// The brief's original version of this test recorded only the string "type" and asserted
    /// against a one-element list — that proves the menu delegate fired exactly once, and says
    /// nothing about WHERE in the flow it fired. This version captures what the writer contains
    /// at the moment the delegate is invoked, so the assertion can actually observe position:
    /// the description prompt must already be written, and the data question must not be yet.
    /// </summary>
    [TestMethod]
    public void TheApplicationType_IsAskedAfterTheDescriptionAndBeforeData()
    {
        var writer = new StringWriter();
        string? writtenWhenAsked = null;

        var outcome = BuildAppWizard.Run(
            Bare(),
            new StringReader("a shop\nn\nn\n"),
            writer,
            () => { writtenWhenAsked = writer.ToString(); return 0; });

        Assert.AreEqual(WizardDecision.Proceed, outcome.Decision);
        Assert.IsNotNull(writtenWhenAsked, "the application type was never asked for");

        // The menu is a delegate, so its position in the flow is only observable by looking at
        // what had been written when it fired.
        StringAssert.Contains(writtenWhenAsked!, "Describe the application");
        Assert.IsFalse(
            writtenWhenAsked!.Contains("Do you have existing data?", StringComparison.Ordinal),
            "the data question was asked before the application type");
    }

    /// <summary>The customization step is last: a user who wants to answer it has already settled
    /// the description, the application type and the data source.</summary>
    [TestMethod]
    public void TheCustomizationStep_IsAskedLast()
    {
        var writer = new StringWriter();

        // The blank after the gate answers the layout prompt, which application type 0 — WPF — is
        // asked before the company. Keeping the offered layout leaves this walkthrough about what
        // it was about: that the customization step comes last.
        var outcome = BuildAppWizard.Run(
            Bare(), new StringReader("a shop\nn\ny\n\nCodis Ltd\n\n\n\n\n\n\n\n\n\n"), writer, () => 0);

        var written = writer.ToString();

        Assert.IsGreaterThan(
            written.IndexOf("Do you have existing data?", StringComparison.Ordinal),
            written.IndexOf("Customize appearance and features?", StringComparison.Ordinal));

        Assert.AreEqual(WizardDecision.Proceed, outcome.Decision);
        Assert.AreEqual("Codis Ltd", outcome.Customization.Company);
    }

    /// <summary>Declining it is not cancelling it: the build goes ahead with what the command line
    /// already asked for.</summary>
    [TestMethod]
    public void DecliningTheCustomizationStep_KeepsTheStartingCustomization()
    {
        var start = AppCustomization.Default with { Company = "Codis Ltd" };

        var outcome = BuildAppWizard.Run(
            Bare(), new StringReader("a shop\nn\nn\n"), new StringWriter(), () => 0, start);

        Assert.AreEqual(WizardDecision.Proceed, outcome.Decision);
        Assert.AreEqual(start, outcome.Customization);
    }

    [TestMethod]
    public void CancellingAtTheCustomizationStep_Cancels()
    {
        var outcome = BuildAppWizard.Run(
            Bare(), new StringReader("a shop\nn\nc\n"), new StringWriter(), () => 0);

        Assert.AreEqual(WizardDecision.Cancelled, outcome.Decision);
    }
}
