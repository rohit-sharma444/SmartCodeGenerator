using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Which front end an interactive `build app` gets. The scope boundary of the whole feature:
/// redirected stdin must never reach the browser, because that is what every piped script and
/// E2E test drives.
/// </summary>
[TestClass]
public class BuildAppWebRoutingTests
{
    [TestMethod]
    public void Redirected_input_never_opens_a_browser()
        => Assert.IsFalse(BuildAppCommand.UseWebWorkflow(inputRedirected: true, noWeb: false));

    [TestMethod]
    public void An_interactive_run_opens_the_browser()
        => Assert.IsTrue(BuildAppCommand.UseWebWorkflow(inputRedirected: false, noWeb: false));

    // The escape hatch for a headless or SSH session, where there is no browser to open.
    [TestMethod]
    public void CG_NO_WEB_keeps_the_console_wizard()
        => Assert.IsFalse(BuildAppCommand.UseWebWorkflow(inputRedirected: false, noWeb: true));
}
