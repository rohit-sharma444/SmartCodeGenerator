namespace CodeGenerator.Tests.BuildFix;

[TestClass]
public class FixTempCleanupTests
{
    /// <summary>Runs once when the whole test run is over: no cgfix- folder is left in %TEMP%.</summary>
    [AssemblyCleanup]
    public static void RemoveTheTempProjects() => FixTestSupport.CleanUpTempProjects();

    [TestMethod]
    public void DeleteTree_RemovesTheFolder_ButNeverFollowsAJunctionOutOfIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "cgfix-t-" + Guid.NewGuid().ToString("N")[..8]);
        var outside = Path.Combine(Path.GetTempPath(), "cgfix-t-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(root, "A.cs"), "class A {}");
        File.WriteAllText(Path.Combine(outside, "keep.cs"), "class K {}");

        try
        {
            if (!FixSessionTests.RunMklink(Path.Combine(root, "link"), outside))
            {
                Assert.Inconclusive("mklink not available on this machine");
                return;
            }

            FixTestSupport.DeleteTree(root);

            Assert.IsFalse(Directory.Exists(root));
            Assert.IsTrue(File.Exists(Path.Combine(outside, "keep.cs")), "the junction's target must be left alone");
        }
        finally
        {
            FixTestSupport.DeleteTree(root);
            FixTestSupport.DeleteTree(outside);
        }
    }
}
