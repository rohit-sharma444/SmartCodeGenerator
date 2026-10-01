using CodeGenerator.App.Scripts;

namespace CodeGenerator.Tests.App.Scripts;

[TestClass]
public sealed class ScriptPathTests
{
    private const string Root = @"C:\Work\Library";

    [TestMethod]
    [DataRow("library", @"C:\Work\Library\library.cgs")]
    [DataRow("library.cgs", @"C:\Work\Library\library.cgs")]
    [DataRow("library.txt", @"C:\Work\Library\library.txt")]
    [DataRow("\"my lib\"", @"C:\Work\Library\my lib.cgs")]          // Review Focus 4
    [DataRow(@"scripts\setup", @"C:\Work\Library\scripts\setup.cgs")]
    [DataRow(@"D:\Recipes\audit.cgs", @"D:\Recipes\audit.cgs")]
    [DataRow("acme.v2", @"C:\Work\Library\acme.v2.cgs")]          // M7: a dot is not an extension
    [DataRow("LIBRARY.CGS", @"C:\Work\Library\LIBRARY.CGS")]
    public void Names_resolve_against_the_workspace(string argument, string expected) =>
        Assert.AreEqual(expected, ScriptPath.Resolve(argument, Root));

    [TestMethod]
    public void A_path_inside_the_workspace_is_shown_relative_and_one_outside_in_full()
    {
        Assert.AreEqual(@"scripts\setup.cgs", ScriptPath.Shown(@"C:\Work\Library\scripts\setup.cgs", Root));
        Assert.AreEqual(@"D:\Recipes\audit.cgs", ScriptPath.Shown(@"D:\Recipes\audit.cgs", Root));
    }
}
