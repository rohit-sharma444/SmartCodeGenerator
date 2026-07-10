using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

// Mutates the process-wide ProjectManager.CurrentProject / ContextManager.Current /
// SessionContext singletons and the ProjectManager.dat registry file next to the
// test exe; method-level parallelization (see MSTestSettings.cs) would otherwise
// race these tests against each other and against ParsingStateSnapshotTests.
[TestClass]
[DoNotParallelize]
public class WorkspaceSnapshotTests
{
    private static string NewWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "snap_" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        return root;
    }

    [TestMethod]
    public void Restore_NoChanges_IsANoOp()
    {
        var root = NewWorkspace();
        File.WriteAllText(Path.Combine(root, "A.cs"), "original");

        using var snapshot = WorkspaceSnapshot.Capture(root);
        snapshot.Restore();

        Assert.AreEqual("original", File.ReadAllText(Path.Combine(root, "A.cs")));
    }

    [TestMethod]
    public void Restore_FileCreatedAfterCapture_IsDeleted()
    {
        var root = NewWorkspace();

        using var snapshot = WorkspaceSnapshot.Capture(root);
        File.WriteAllText(Path.Combine(root, "New.cs"), "added");
        snapshot.Restore();

        Assert.IsFalse(File.Exists(Path.Combine(root, "New.cs")));
    }

    [TestMethod]
    public void Restore_FileModifiedAfterCapture_HasOriginalContents()
    {
        var root = NewWorkspace();
        var file = Path.Combine(root, "A.cs");
        File.WriteAllText(file, "original");

        using var snapshot = WorkspaceSnapshot.Capture(root);
        File.WriteAllText(file, "clobbered");
        snapshot.Restore();

        Assert.AreEqual("original", File.ReadAllText(file));
    }

    [TestMethod]
    public void Restore_FileDeletedAfterCapture_IsRecreated()
    {
        var root = NewWorkspace();
        var file = Path.Combine(root, "A.cs");
        File.WriteAllText(file, "original");

        using var snapshot = WorkspaceSnapshot.Capture(root);
        File.Delete(file);
        snapshot.Restore();

        Assert.AreEqual("original", File.ReadAllText(file));
    }

    [TestMethod]
    public void Restore_DirectoryCreatedAfterCapture_IsRemoved()
    {
        var root = NewWorkspace();

        using var snapshot = WorkspaceSnapshot.Capture(root);
        var projectDir = Path.Combine(root, "Foo");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "Foo.csproj"), "<Project/>");
        snapshot.Restore();

        // This is what removes a `dotnet new` project tree.
        Assert.IsFalse(Directory.Exists(projectDir));
    }

    [TestMethod]
    public void Capture_ExcludesBuildOutputAndGit()
    {
        var root = NewWorkspace();
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        Directory.CreateDirectory(Path.Combine(root, "obj"));
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        File.WriteAllText(Path.Combine(root, "bin", "app.dll"), "binary");
        File.WriteAllText(Path.Combine(root, "obj", "tmp.cache"), "cache");
        File.WriteAllText(Path.Combine(root, ".git", "HEAD"), "ref");
        File.WriteAllText(Path.Combine(root, "A.cs"), "source");

        using var snapshot = WorkspaceSnapshot.Capture(root);
        File.WriteAllText(Path.Combine(root, "bin", "app.dll"), "rebuilt");
        snapshot.Restore();

        // Excluded directories are neither captured nor disturbed by restore:
        // the rebuilt dll survives, and bin/ is not deleted as "not in snapshot".
        Assert.AreEqual("rebuilt", File.ReadAllText(Path.Combine(root, "bin", "app.dll")));
        Assert.IsTrue(File.Exists(Path.Combine(root, "obj", "tmp.cache")));
        Assert.IsTrue(File.Exists(Path.Combine(root, ".git", "HEAD")));
    }

    [TestMethod]
    public void Measure_CountsOnlyIncludedFiles()
    {
        var root = NewWorkspace();
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        File.WriteAllText(Path.Combine(root, "bin", "app.dll"), "binary");
        File.WriteAllText(Path.Combine(root, "A.cs"), "12345");
        File.WriteAllText(Path.Combine(root, "B.cs"), "12345");

        var sizing = WorkspaceSnapshot.Measure(root);

        Assert.AreEqual(2, sizing.FileCount);
        Assert.AreEqual(10, sizing.Bytes);
    }

    [TestMethod]
    public void Restore_RestoresBothParsingSingletons()
    {
        var root = NewWorkspace();
        ContextManager.Current.LastEntity = "BeforeEntity";
        SessionContext.CurrentClass = "BeforeClass";

        using var snapshot = WorkspaceSnapshot.Capture(root);
        ContextManager.Current.LastEntity = "AfterEntity";
        SessionContext.CurrentClass = "AfterClass";
        snapshot.Restore();

        Assert.AreEqual("BeforeEntity", ContextManager.Current.LastEntity);
        Assert.AreEqual("BeforeClass", SessionContext.CurrentClass);
    }

    [TestMethod]
    public void Dispose_AfterSuccessfulRestore_DeletesSnapshot()
    {
        var root = NewWorkspace();
        var snapshot = WorkspaceSnapshot.Capture(root);
        var path = snapshot.SnapshotPath;

        snapshot.Restore();
        snapshot.Dispose();

        Assert.IsFalse(Directory.Exists(path));
    }

    [TestMethod]
    public void Dispose_AfterFailedRestore_KeepsSnapshot()
    {
        var root = NewWorkspace();
        var snapshot = WorkspaceSnapshot.Capture(root);
        var path = snapshot.SnapshotPath;

        // Force Restore to throw by destroying the captured tree.
        Directory.Delete(Path.Combine(path, "workspace"), recursive: true);
        Assert.ThrowsExactly<DirectoryNotFoundException>(() => snapshot.Restore());

        snapshot.Dispose();

        // Losing the only copy of the user's code on a failed restore is the
        // worst thing this feature could do.
        Assert.IsTrue(Directory.Exists(path));
    }
}
