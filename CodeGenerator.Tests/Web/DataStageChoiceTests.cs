using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerator.Web.Components.Stages;
using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The data stage's refusal belongs to the choice it was about.
/// </summary>
/// <remarks>
/// "Nothing to build from" was said about "No data". After "I have a file" was picked it stayed on
/// screen under the file controls, telling the user off for a choice they had already changed.
/// </remarks>
[TestClass]
public class DataStageChoiceTests
{
    [TestMethod]
    public void The_nothing_to_build_from_error_goes_when_a_file_is_chosen()
    {
        using var context = new BunitContext();
        var navigation = new StageNavigation();

        var session = new BuildSession(string.Empty, Path.GetTempPath(), AppCustomization.Default);
        session.SetTarget(new WpfTarget());
        session.Advance();

        var rendered = context.Render<DataStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, navigation)
            .Add(c => c.OnAdvance, () => { }));

        rendered.InvokeAsync(navigation.ApplyAsync).Wait();
        rendered.Render();

        StringAssert.Contains(rendered.Find(".failure").TextContent, "nothing to build from");

        rendered.Find("input[type=radio][value=file]").Change(true);

        Assert.IsEmpty(rendered.FindAll(".failure"));
    }
}
