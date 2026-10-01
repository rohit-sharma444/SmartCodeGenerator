using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

// Mutates the process-wide ProjectManager.CurrentProject / ContextManager.Current /
// SessionContext singletons and the ProjectManager.dat registry file next to the
// test exe; method-level parallelization (see MSTestSettings.cs) would otherwise
// race these tests against each other and against ParsingStateSnapshotTests.
[TestClass]
[DoNotParallelize]
public class WorkspaceSnapshotTests
{
    private readonly List<string> _tempDirectories = [];

    private string NewWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "snap_" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        _tempDirectories.Add(root);
        return root;
    }

    [TestCleanup]
    public void RemoveTempDirectories()
    {
        // Initialize() points the process-wide store at a temp workspace we are
        // about to delete; leave it detached from any file.
        LearnedIntentStore.UseInMemory();

        foreach (var directory in _tempDirectories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory must never fail an otherwise green test.
            }
        }

        _tempDirectories.Clear();
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
    public void Restore_DiscardsLearningRecordedDuringTheBatch()
    {
        var root = NewWorkspace();
        LearnedIntentStore.Initialize(root);
        LearnedIntentStore.Save();

        using var snapshot = WorkspaceSnapshot.Capture(root);

        // What an executed handler does, via LearningCoordinator.RecordOutcome.
        LearnedIntentStore.Current.Reinforce("create a Customer class", IntentType.CreateClass);
        LearnedIntentStore.Save();

        snapshot.Restore();

        // Restoring learned.json alone is not enough: the next Save() would write
        // the un-reverted in-memory store straight back over the restored file.
        Assert.IsEmpty(LearnedIntentStore.Current.Entries);

        // Prove it survives the write that used to clobber the restored file.
        LearnedIntentStore.Save();
        LearnedIntentStore.Initialize(root);
        Assert.IsEmpty(LearnedIntentStore.Current.Entries);
    }

    // The registry beside the test exe is shared by every test; these put it back as they found it.
    private static void WithRegistry(string? contents, Action test)
    {
        var path = ProjectManager.RegistryPath;
        var saved = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var current = ProjectManager.CurrentProject;
        try
        {
            if (contents is null)
                File.Delete(path);
            else
                File.WriteAllText(path, contents);
            test();
        }
        finally
        {
            if (saved is null)
                File.Delete(path);
            else
                File.WriteAllBytes(path, saved);
            ProjectManager.LoadProject();
            ProjectManager.CurrentProject = current;
        }
    }

    private static string Registered(params string[] csprojs) =>
        System.Text.Json.JsonSerializer.Serialize(csprojs.Select(c => new ProjectMetaData
        {
            Success = true,
            ProjectName = Path.GetFileNameWithoutExtension(c),
            ProjectPath = Path.GetDirectoryName(c)!,
            CsProjPath = c,
        }).ToList());

    private static string NewProject(string root, string name)
    {
        var csproj = Path.Combine(root, name, name + ".csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(csproj)!);
        File.WriteAllText(csproj, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        return csproj;
    }

    // S4: the rollback reloaded the whole registry, so `list projects` then offered projects from
    // every other workspace the tool had ever been used in.
    [TestMethod]
    public void Restore_ShowsOnlyThisWorkspacesProjects()
    {
        var root = NewWorkspace();
        var other = NewWorkspace();
        var shop = NewProject(root, "Shop");
        var elsewhere = NewProject(other, "Elsewhere");

        WithRegistry(Registered(shop, elsewhere), () =>
        {
            using var snapshot = WorkspaceSnapshot.Capture(root);
            snapshot.Restore();

            CollectionAssert.AreEquivalent(new[] { "Shop" }, ProjectManager.Projects.Select(p => p.ProjectName).ToArray());
        });
    }

    // S4 / U2: with no registry file, startup registers the projects it finds on disk without
    // saving them; reading only the file afterwards (or keeping a stale list) loses them.
    [TestMethod]
    public void Restore_WithNoRegistryFile_ShowsTheProjectsStartupWouldFind()
    {
        var root = NewWorkspace();
        NewProject(root, "Shop");

        WithRegistry(null, () =>
        {
            using var snapshot = WorkspaceSnapshot.Capture(root);
            File.WriteAllText(ProjectManager.RegistryPath, Registered(NewProject(root, "Tool")));
            ProjectManager.LoadProject();
            snapshot.Restore();

            CollectionAssert.AreEquivalent(new[] { "Shop" }, ProjectManager.Projects.Select(p => p.ProjectName).ToArray());
        });
    }

    [TestMethod]
    public void ReloadWithin_WithNoRegistryFile_KeepsTheProjectsOnDisk()   // U2
    {
        var root = NewWorkspace();
        NewProject(root, "Shop");

        WithRegistry(null, () =>
        {
            ProjectManager.ReloadWithin(root);

            CollectionAssert.AreEquivalent(new[] { "Shop" }, ProjectManager.Projects.Select(p => p.ProjectName).ToArray());
        });
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

        // Dispose keeps this one on purpose, so the test owns deleting it.
        _tempDirectories.Add(path);
    }
}
