using AppScaffolding;
using CodeGenerator.App.Resume;
using CodeGenerator.Web;
using CodeGenerator.Web.Workflow;
using DataIngestion;

namespace CodeGenerator.Tests.Demo;

/// <summary>
/// Serves a real `build app` workflow for tools/demo-video to record.
/// </summary>
/// <remarks>
/// Not a test: it starts the same host `build app` starts, for the session the console would
/// build from <c>build app: an order management system from csv &lt;orders.csv&gt;</c>, writes
/// the page's address to <c>url.txt</c> and serves it until the workflow ends or
/// <c>stop.txt</c> appears. It lives here rather than behind a CLI switch because the console
/// only opens the page for a person at a real console, and the recorder is not one.
/// <para>
/// Does nothing - reports inconclusive - unless <c>DEMO_VIDEO_ROOT</c> names the workspace to
/// build in, so an ordinary test run never starts a server.
/// </para>
/// </remarks>
[TestClass]
public class DemoVideoHost
{
    public const string Description = "an order management system";

    [TestMethod]
    [TestCategory("DemoVideo")]
    public void Serve_the_workflow_for_the_demo_video()
    {
        var root = Environment.GetEnvironmentVariable("DEMO_VIDEO_ROOT");
        var csv = Environment.GetEnvironmentVariable("DEMO_VIDEO_CSV");

        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(csv))
        {
            Assert.Inconclusive("Only for tools/demo-video: set DEMO_VIDEO_ROOT and DEMO_VIDEO_CSV.");
            return;
        }

        Directory.CreateDirectory(root);

        var session = new BuildSession(
            Description, root, AppCustomization.Default, new DataSourceSpec(DataSourceKind.Csv, csv));

        // Wired as BuildAppCommand wires it, so the resume the video mentions is the real one.
        session.Autosave = s => BuildSnapshotStore.Save(root, BuildSnapshot.Of(s));
        session.OnFinished = _ => BuildSnapshotStore.Clear(root);

        Assert.IsTrue(BuildAppWebHost.TryStart(session, out var host), "the page did not start");

        using (host)
        {
            File.WriteAllText(Path.Combine(root, "url.txt"), host.Url);

            // Until the recorder says it is done - NOT until the workflow ends. The app the Run
            // stage starts is a child of this test process, and the test runner takes its children
            // down with it: ending when the recorder left the workflow page killed the generated
            // app in the middle of being demonstrated.
            var stop = Path.Combine(root, "stop.txt");
            var until = DateTime.Now.AddMinutes(30);

            while (DateTime.Now < until && !File.Exists(stop))
                Thread.Sleep(500);
        }
    }
}
