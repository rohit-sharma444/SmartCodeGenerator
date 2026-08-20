using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Finding the executable MSBuild just produced. Launching it needs a real process and is
/// verified by hand; deciding <i>what</i> to launch is pure and lives here.
/// </summary>
[TestClass]
public class AppLauncherTests
{
    /// <summary>The line the default console logger writes for each project it builds.</summary>
    private const string ShopLine =
        @"  Shop -> D:\Work\Shop\bin\Debug\net10.0-windows\Shop.dll";

    [TestMethod]
    public void AssemblyPathFrom_TakesTheDllAndReturnsTheExeBesideIt()
    {
        // The apphost, not the dll: `Shop.dll` is not directly executable, and starting it
        // through `dotnet` is what puts a console window behind a WPF app.
        Assert.AreEqual(
            @"D:\Work\Shop\bin\Debug\net10.0-windows\Shop.exe",
            AppLauncher.AssemblyPathFrom(ShopLine));
    }

    [TestMethod]
    public void AssemblyPathFrom_IgnoresEverythingAroundIt()
    {
        var output = "  Determining projects to restore...\n"
                     + "  All projects are up-to-date for restore.\n"
                     + ShopLine + "\n"
                     + "\nBuild succeeded.\n    0 Warning(s)\n    0 Error(s)\n";

        Assert.AreEqual(
            @"D:\Work\Shop\bin\Debug\net10.0-windows\Shop.exe",
            AppLauncher.AssemblyPathFrom(output));
    }

    [TestMethod]
    public void AssemblyPathFrom_TakesTheLastProjectBuilt()
    {
        // Dependencies are built and reported first; the app the user asked for is last.
        var output = @"  Shop.Core -> D:\Work\Shop\Core\bin\Debug\net10.0\Shop.Core.dll"
                     + "\n" + ShopLine;

        Assert.AreEqual(
            @"D:\Work\Shop\bin\Debug\net10.0-windows\Shop.exe",
            AppLauncher.AssemblyPathFrom(output));
    }

    [TestMethod]
    public void AssemblyPathFrom_IsNullWhenTheOutputSaysNothingUsable()
    {
        // The caller falls back to `dotnet run` rather than guessing a path that may not exist.
        Assert.IsNull(AppLauncher.AssemblyPathFrom(""));
        Assert.IsNull(AppLauncher.AssemblyPathFrom("Build succeeded."));
        Assert.IsNull(AppLauncher.AssemblyPathFrom("  Shop -> not a dll"));
        Assert.IsNull(AppLauncher.AssemblyPathFrom("an arrow -> like this one"));
    }

    /// <summary>
    /// The run stage starts a real process and had no way to stop it: Launch disposed the Process
    /// and returned only a file name, so the id was gone the moment it existed. A running app holds
    /// its own bin folder open, which is what makes the next build of that project fail — so "stop
    /// it" is the difference between carrying on and going to Task Manager.
    /// </summary>
    [TestMethod]
    public void A_started_process_can_be_stopped_by_the_id_it_reported()
    {
        // Waits on stdin forever, which this test never gives it.
        using var child = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c pause")
            {
                RedirectStandardInput = true,
                UseShellExecute = false
            });

        Assert.IsNotNull(child);
        Assert.IsFalse(child.HasExited);

        Assert.IsTrue(AppLauncher.Stop(child.Id));

        child.WaitForExit(TimeSpan.FromSeconds(10));
        Assert.IsTrue(child.HasExited);
    }

    [TestMethod]
    public void Stopping_something_that_has_already_gone_is_not_an_error()
    {
        using var child = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c exit")
            {
                UseShellExecute = false
            });

        Assert.IsNotNull(child);
        child.WaitForExit(TimeSpan.FromSeconds(10));

        // A user who stops an app that closed itself gets a plain "no", not an exception out of a
        // component handler.
        Assert.IsFalse(AppLauncher.Stop(child.Id));
    }
}
