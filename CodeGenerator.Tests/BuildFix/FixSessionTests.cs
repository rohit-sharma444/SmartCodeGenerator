using CodeGenerator.BuildFix;

namespace CodeGenerator.Tests.BuildFix;

[TestClass]
public class FixSessionTests
{
    [TestMethod]
    public void Write_ChangesTheFileAndLogsTheEdit()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "class A {}");
        var session = new FixSession(root);
        session.BeginRound(1);

        session.Write(file, "partial class A {}", "test");

        Assert.AreEqual("partial class A {}", File.ReadAllText(file));
        Assert.HasCount(1, session.Edits);
        Assert.AreEqual("test", session.Edits[0].By);
    }

    [TestMethod]
    public void Write_RefusesAPathOutsideTheProject()
    {
        var root = FixTestSupport.TempProject();
        var outside = FixTestSupport.Write(FixTestSupport.TempProject(), "B.cs", "class B {}");
        var session = new FixSession(root);
        session.BeginRound(1);

        Assert.Throws<InvalidOperationException>(() => session.Write(outside, "x", "test"));
        Assert.AreEqual("class B {}", File.ReadAllText(outside));
    }

    [TestMethod]
    public void Write_RefusesASiblingFolderThatSharesThePrefix()
    {
        var root = FixTestSupport.TempProject();
        var sibling = FixTestSupport.Write(root + "-other", "C.cs", "class C {}");
        var session = new FixSession(root);
        session.BeginRound(1);

        Assert.Throws<InvalidOperationException>(() => session.Write(sibling, "x", "test"));
    }

    [TestMethod]
    public void RevertRound_RestoresTheExactBytes()
    {
        var root = FixTestSupport.TempProject();
        var file = Path.Combine(root, "A.cs");
        var original = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a', 13, 10, (byte)'b' };
        File.WriteAllBytes(file, original);
        var session = new FixSession(root);
        session.BeginRound(1);
        session.Write(file, "changed", "test");

        session.RevertRound(1);

        CollectionAssert.AreEqual(original, File.ReadAllBytes(file));
#pragma warning disable MSTEST0037
        Assert.IsTrue(session.Edits[0].Reverted);
#pragma warning restore MSTEST0037
    }

    [TestMethod]
    public void Write_KeepsTheBomAndTheLineEndings()
    {
        var root = FixTestSupport.TempProject();
        var file = Path.Combine(root, "A.cs");
        File.WriteAllBytes(file, [0xEF, 0xBB, 0xBF, (byte)'x']);
        var session = new FixSession(root);
        session.BeginRound(1);

        session.Write(file, "a\r\nb", "test");

        var bytes = File.ReadAllBytes(file);
#pragma warning disable MSTEST0037
        Assert.AreEqual(0xEF, bytes[0]);
        Assert.IsTrue(System.Text.Encoding.UTF8.GetString(bytes).Contains("a\r\nb"));
#pragma warning restore MSTEST0037
    }

    [TestMethod]
    public void RevertRound_LeavesAnEarlierRoundsEditAlone()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "v0");
        var session = new FixSession(root);
        session.BeginRound(1);
        session.Write(file, "v1", "rule");
        session.BeginRound(2);
        session.Write(file, "v2", "ai");

        session.RevertRound(2);

        Assert.AreEqual("v1", File.ReadAllText(file));
    }
}
