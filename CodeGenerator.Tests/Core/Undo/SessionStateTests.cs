using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Undo;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Undo;

[TestClass]
[DoNotParallelize]
public sealed class SessionStateTests
{
    private sealed class RequiredStub : IValidationRule
    {
        public ValidationCategory Category => ValidationCategory.Data;
        public IReadOnlyList<string> Properties => ["Title"];
        public string Describe() => "Title is required";
        public void Emit(ValidationEmitContext ctx) { }
    }

    [TestInitialize]
    [TestCleanup]
    public void Reset()
    {
        ValidationRegistry.Clear();
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
    }

    [TestMethod]
    public void Restore_brings_back_the_rules_and_the_current_class()
    {
        SessionContext.CurrentClass = "Book";
        var before = SessionState.Capture();

        ValidationRegistry.Add("Book", new RequiredStub());
        SessionContext.CurrentClass = "Member";
        Assert.IsFalse(before.SameAs(SessionState.Capture()));

        before.Restore();

        Assert.IsEmpty(ValidationRegistry.Get("Book"));
        Assert.AreEqual("Book", SessionContext.CurrentClass);
        Assert.IsTrue(before.SameAs(SessionState.Capture()));
    }

    [TestMethod]
    public void The_current_project_is_part_of_the_state()
    {
        var before = SessionState.Capture();
        ProjectManager.CurrentProject = new ProjectMetaData { ProjectName = "Shop" };

        Assert.IsFalse(before.SameAs(SessionState.Capture()));
        before.Restore();
        Assert.IsNull(ProjectManager.CurrentProject);
    }

    [TestMethod]
    public void The_session_journal_tracks_the_real_registry_and_state()
    {
        var root = Path.Combine(Path.GetTempPath(), "cg_undo_fs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var journal = UndoJournal.ForSession(root);

            using (journal.Begin("add validation required on Title in Book"))
                ValidationRegistry.Add("Book", new RequiredStub());

            Assert.AreEqual("Undid: add validation required on Title in Book", journal.Undo().Message);
            Assert.IsEmpty(ValidationRegistry.Get("Book"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
