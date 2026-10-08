using CodeGenerator.BuildFix;
using System.Diagnostics;

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
        Assert.IsFalse(Directory.Exists(Path.Combine(root, ".codegen")));
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
        Assert.IsTrue(session.Edits[0].Reverted);
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
        Assert.AreEqual((byte)0xEF, bytes[0]);
        Assert.AreEqual((byte)0xBB, bytes[1]);
        Assert.AreEqual((byte)0xBF, bytes[2]);
        Assert.AreEqual("a\r\nb", File.ReadAllText(file));
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

    [TestMethod]
    public void Write_RefusesAJunctionPointingOutsideTheProject()
    {
        var root = FixTestSupport.TempProject();
        var outside = FixTestSupport.TempProject();
        var outsideFile = FixTestSupport.Write(outside, "target.cs", "class T {}");
        var link = Path.Combine(root, "link");

        var mklinkResult = RunMklink(link, outside);
        if (!mklinkResult)
        {
            Assert.Inconclusive("mklink not available on this machine");
            return;
        }

        var session = new FixSession(root);
        session.BeginRound(1);
        var linkedFile = Path.Combine(link, "target.cs");

        Assert.Throws<InvalidOperationException>(() => session.Write(linkedFile, "modified", "test"));
        Assert.Throws<InvalidOperationException>(() => session.Read(linkedFile));
        Assert.AreEqual("class T {}", File.ReadAllText(outsideFile));
    }

    [TestMethod]
    public void Write_ThrowsIfCalledBeforeBeginRound()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "class A {}");
        var session = new FixSession(root);

        var ex = Assert.Throws<InvalidOperationException>(() => session.Write(file, "x", "test"));
        Assert.AreEqual("Call BeginRound first", ex.Message);
    }

    [TestMethod]
    public void Read_ThrowsIfCalledBeforeBeginRound()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "class A {}");
        var session = new FixSession(root);

        var ex = Assert.Throws<InvalidOperationException>(() => session.Read(file));
        Assert.AreEqual("Call BeginRound first", ex.Message);
    }

    private static bool RunMklink(string link, string target)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    [TestMethod]
    public void RevertRound_RestoresEveryFile_EvenWhenOneCannotBeWritten_AndDoesNotThrow()
    {
        var root = FixTestSupport.TempProject();
        var a = FixTestSupport.Write(root, "A.cs", "aaa");
        var b = FixTestSupport.Write(root, "B.cs", "bbb");
        var c = FixTestSupport.Write(root, "C.cs", "ccc");
        var session = new FixSession(root);
        session.BeginRound(1);
        session.Write(a, "A2", "t");
        session.Write(b, "B2", "t");
        session.Write(c, "C2", "t");

        using (new FileStream(b, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            session.RevertRound(1);

        Assert.AreEqual("aaa", File.ReadAllText(a));
        Assert.AreEqual("B2", File.ReadAllText(b));
        Assert.AreEqual("ccc", File.ReadAllText(c));
        Assert.IsTrue(session.Edits.Single(e => e.File == a).Reverted);
        Assert.IsFalse(session.Edits.Single(e => e.File == b).Reverted);
        Assert.IsTrue(session.Edits.Single(e => e.File == c).Reverted);
    }

    [TestMethod]
    public void Write_KeepsAMapOfEveryBackup_RelativePathsOnly()
    {
        var root = FixTestSupport.TempProject();
        var a = FixTestSupport.Write(root, "A.cs", "a0");
        var b = FixTestSupport.Write(root, Path.Combine("Models", "B.cs"), "b0");
        var session = new FixSession(root);
        session.BeginRound(1);
        session.Write(a, "a1", "t");
        session.Write(b, "b1", "t");
        session.Write(a, "a1b", "t");
        session.BeginRound(2);
        session.Write(a, "a2", "t");

        var folder = Directory.GetDirectories(Path.Combine(root, ".codegen", "fix-backups")).Single();
        var map = Path.Combine(folder, BuildFixReport.BackupMapName);

        CollectionAssert.AreEqual(
            new[] { "r1\\0.bak = A.cs", "r1\\1.bak = Models\\B.cs", "r2\\0.bak = A.cs" },
            File.ReadAllLines(map));
        Assert.AreEqual("a0", File.ReadAllText(Path.Combine(folder, "r1", "0.bak")));
        Assert.AreEqual("a1b", File.ReadAllText(Path.Combine(folder, "r2", "0.bak")));
        Assert.DoesNotContain(root, File.ReadAllText(map));
    }

    [TestMethod]
    public void RevertRound_MarksAnEditWhoseFileCannotBeRestored()
    {
        var root = FixTestSupport.TempProject();
        var a = FixTestSupport.Write(root, "A.cs", "aaa");
        var b = FixTestSupport.Write(root, "B.cs", "bbb");
        var session = new FixSession(root);
        session.BeginRound(1);
        session.Write(a, "A2", "t");
        session.Write(b, "B2", "t");

        using (new FileStream(b, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            session.RevertRound(1);

        Assert.IsFalse(session.Edits.Single(e => e.File == a).RestoreFailed);
        Assert.IsTrue(session.Edits.Single(e => e.File == b).RestoreFailed);
        Assert.IsFalse(session.Edits.Single(e => e.File == b).Reverted);
    }

    [TestMethod]
    public void RestoreFailed_AndReverted_CanNeverBeTrueTogether()
    {
        var edit = new FixEdit("f", "by", 1);

        edit.Reverted = true;
        edit.RestoreFailed = true;
        Assert.IsFalse(edit.Reverted);

        edit.Reverted = true;
        Assert.IsFalse(edit.RestoreFailed);
    }

    [TestMethod]
    public void RevertRound_DoesNotMarkAnEditRestoreFailed_WhenUndoSinceAlreadyRestoredIt()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "a");
        var session = new FixSession(root);
        session.BeginRound(1);
        session.Write(file, "b", "AI");
        Assert.HasCount(0, session.UndoSince(0));

        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            session.RevertRound(1);

        Assert.IsTrue(session.Edits[0].Reverted);
        Assert.IsFalse(session.Edits[0].RestoreFailed);
        Assert.AreEqual("a", File.ReadAllText(file));
    }

    [TestMethod]
    public void RevertRound_StillFlagsAnEditNotUndone_AndAnUndoneOneStaysReverted()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "a");
        var session = new FixSession(root);
        session.BeginRound(1);
        session.Write(file, "b", "first");
        var mark = session.Mark();
        session.Write(file, "c", "second");
        session.UndoSince(mark);

        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            session.RevertRound(1);

        Assert.IsTrue(session.Edits[0].RestoreFailed);
        Assert.IsFalse(session.Edits[0].Reverted);
        Assert.IsTrue(session.Edits[1].Reverted);
        Assert.IsFalse(session.Edits[1].RestoreFailed);
    }

    [TestMethod]
    public void Write_StillEditsAndKeepsTheOriginal_WhenTheBackupMapCannotBeWritten()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "original");
        var session = new FixSession(root);
        session.BeginRound(1);
        var field = typeof(FixSession).GetField(
            "_backupRoot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var map = Path.Combine((string)field.GetValue(session)!, BuildFixReport.BackupMapName);
        Directory.CreateDirectory(map);

        session.Write(file, "changed", "test");

        Assert.AreEqual("changed", File.ReadAllText(file));
        Assert.HasCount(1, session.Edits);
        Assert.HasCount(0, session.RevertRound(1));
        Assert.AreEqual("original", File.ReadAllText(file));
    }
}
