using CodeGenerator.App;
using DataIngestion;
using DataIngestion.Schema.Readers;

namespace CodeGenerator.Tests.App;

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
            Bare(), new StringReader("a library lending system\nn\n"), writer, () => 0);

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
            new StringReader("n\n"), writer, () => 0);

        Assert.AreEqual("a bike shop", outcome.Description);
        Assert.IsFalse(
            writer.ToString().Contains("Describe the application", StringComparison.Ordinal),
            "typing a description should stay a shortcut, not something to retype");
    }

    [TestMethod]
    public void TheChosenApplicationType_BecomesTheTarget()
    {
        var outcome = BuildAppWizard.Run(
            Bare(), new StringReader("a shop\nn\n"), new StringWriter(), () => 1);

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
                Bare(), new StringReader($"\ny\ncsv\n{path}\n"), new StringWriter(), () => 3);

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
                new StringReader(""), writer, () => 0);

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
            new StringReader("a shop\nn\n"),
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
}
