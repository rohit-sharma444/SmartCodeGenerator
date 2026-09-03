using AppScaffolding.Screens;

namespace CodeGenerator.Tests.App;

/// <summary>The view-model half of the entry screens.</summary>
[TestClass]
public class EntryScreenViewModelTests
{
    [TestMethod]
    public void The_editor_interface_extends_a_dirty_screen()
    {
        var source = RecordEditorTemplate.Source("Acme", validation: false);

        StringAssert.Contains(source, "public interface IDirtyScreen");
        StringAssert.Contains(source, "public interface IRecordEditor : IDirtyScreen");
    }

    /// <summary>
    /// The shell must not test IRecordEditor for dirtiness.
    /// </summary>
    /// <remarks>
    /// A master screen holds its editor inline and is deliberately not an IRecordEditor, so a
    /// check against that interface would let a dirty master be navigated away from without the
    /// discard prompt firing — in all three places the shell asks.
    /// </remarks>
    [TestMethod]
    public void The_shell_asks_a_dirty_screen_not_a_record_editor()
    {
        var source = ShellTemplate.Shell("Acme");

        Assert.DoesNotContain("_current is IRecordEditor { IsDirty: true }", source);
        Assert.AreEqual(
            2,
            source.Split("_current is IDirtyScreen { IsDirty: true }").Length - 1,
            "TryShow and SignOut both ask");
    }

    /// <summary>Leaving an editor still drops the history, and that test is on IRecordEditor —
    /// a master screen is not an editor and Back must not be disabled over it.</summary>
    [TestMethod]
    public void Leaving_an_editor_still_drops_the_history()
        => StringAssert.Contains(
            ShellTemplate.Shell("Acme"), "_previous = _current is IRecordEditor ? null : _current;");

    /// <summary>
    /// A string match on the declaration proves the text exists; it does not prove the emitted
    /// code actually compiles with that hierarchy, or that the shell's pattern match names a type
    /// that really exists. Only compiling the emitted editor and reflecting over the loaded type
    /// closes that gap.
    /// </summary>
    [TestMethod]
    public void The_compiled_editor_is_assignable_to_the_dirty_screen_interface()
    {
        var assembly = RecordEditorTests.Emit();

        var editorType = assembly.GetType("RecordEditorViewModel`1")!
            .MakeGenericType(assembly.GetType("Book")!);
        var dirtyScreenType = assembly.GetType("IDirtyScreen")!;

        Assert.IsTrue(
            dirtyScreenType.IsAssignableFrom(editorType),
            "the emitted RecordEditorViewModel<T> must implement IDirtyScreen");
    }
}
