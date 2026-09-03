using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AppScaffolding.Screens;
using CodeGenerationEngine.Core;

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

    // ---- Totals ----

    [TestMethod]
    public void An_editor_totals_each_money_column_of_its_lines()
    {
        var source = RecordEditorTemplate.Source("Acme", validation: false);

        StringAssert.Contains(source, "public sealed record LineTotal(string Column, decimal Value, bool IsGrand)");
        StringAssert.Contains(source, "public IReadOnlyList<LineTotal> Totals");
    }

    /// <summary>The same exclusion the transaction list makes: an Id is not money.</summary>
    [TestMethod]
    public void The_totals_exclude_the_id_and_the_foreign_key()
    {
        var source = RecordEditorTemplate.Source("Acme", validation: false);

        StringAssert.Contains(source, "!p.Name.EndsWith(\"Id\", StringComparison.Ordinal)");
    }

    /// <summary>A line whose Id and foreign key are large, nonzero numbers, so a leaked exclusion
    /// would visibly change the sum -- a substring match on the exclusion predicate cannot show
    /// that it is actually applied. Two money columns, so IsGrand must be false for both, and one
    /// of them a double, since ToDecimal converts it and nothing else would notice if that arm
    /// broke.</summary>
    [TestMethod]
    public void Totals_sums_each_money_column_by_value_and_excludes_id_and_foreign_key()
    {
        var assembly = EmitWithTotals();

        SeedLine(assembly, "OrderLine", id: 900, bookId: 900, ("UnitPrice", 10.50m), ("Weight", 2.5d));
        SeedLine(assembly, "OrderLine", id: 901, bookId: 900, ("UnitPrice", 5.25m), ("Weight", 1.5d));

        var set = LineSetOf(assembly, "OrderLine", "BookId");
        set.GetType().GetMethod("Load")!.Invoke(set, [900]);

        var editor = EditorOverBookWith(assembly, set);
        var totals = Totals(editor);

        var unitPrice = totals.Single(t => ColumnOf(t) == "UnitPrice");
        var weight = totals.Single(t => ColumnOf(t) == "Weight");

        Assert.AreEqual(15.75m, ValueOf(unitPrice), "the Id and foreign key, both 900, must not leak in");
        Assert.AreEqual(4.0m, ValueOf(weight), "a double column is summed too");
        Assert.IsFalse(IsGrandOf(unitPrice), "two money columns means neither is the grand total");
        Assert.IsFalse(IsGrandOf(weight));
    }

    /// <summary>IsGrand is only true when a set has exactly one money column -- the reverse of the
    /// two-column case above, because a rule proved in one direction is only half proved.</summary>
    [TestMethod]
    public void Totals_marks_a_sets_only_money_column_as_the_grand_total()
    {
        var assembly = EmitWithTotals();

        SeedLine(assembly, "InvoiceLine", id: 900, bookId: 900, ("Amount", 40m));
        SeedLine(assembly, "InvoiceLine", id: 901, bookId: 900, ("Amount", 60m));

        var set = LineSetOf(assembly, "InvoiceLine", "BookId");
        set.GetType().GetMethod("Load")!.Invoke(set, [900]);

        var editor = EditorOverBookWith(assembly, set);
        var total = Totals(editor).Single();

        Assert.AreEqual("Amount", ColumnOf(total));
        Assert.AreEqual(100m, ValueOf(total));
        Assert.IsTrue(IsGrandOf(total), "one money column is the only case where a grand total is not a guess");
    }

    /// <summary>Two line types, one with a single money column and one with two, so IsGrand can be
    /// proved both ways without a second compiled fixture.</summary>
    private const string TotalsLineStubs = """
public class OrderLine
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public decimal UnitPrice { get; set; }
    public double Weight { get; set; }
}

public class InvoiceLine
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public decimal Amount { get; set; }
}
""";

    private static Assembly EmitWithTotals()
    {
        var source = EmittedSource.Flatten(
            RecordEditorTests.StoreAndModelStub,
            TotalsLineStubs,
            RecordEditorTests.ObservableAndCommand,
            RecordEditorTests.Editor(validation: false));

        EmittedSource.AssertCompiles(source);

        return Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
    }

    private static object LineSetOf(Assembly assembly, string lineTypeName, string foreignKey)
        => Activator.CreateInstance(
            assembly.GetType("LineSet`1")!.MakeGenericType(assembly.GetType(lineTypeName)!),
            [lineTypeName, foreignKey])!;

    private static IList RowsOf(Assembly assembly, string lineTypeName)
        => (IList)assembly.GetType("MemoryStore`1")!
            .MakeGenericType(assembly.GetType(lineTypeName)!)
            .GetField("Rows")!.GetValue(null)!;

    private static void SeedLine(
        Assembly assembly, string lineTypeName, int id, int bookId, params (string Property, object Value)[] fields)
    {
        var lineType = assembly.GetType(lineTypeName)!;
        var line = Activator.CreateInstance(lineType)!;

        lineType.GetProperty("Id")!.SetValue(line, id);
        lineType.GetProperty("BookId")!.SetValue(line, bookId);

        foreach (var (property, value) in fields)
            lineType.GetProperty(property)!.SetValue(line, value);

        RowsOf(assembly, lineTypeName).Add(line);
    }

    /// <summary>
    /// Book 900, open in an editor over whichever line sets a test supplies.
    /// </summary>
    /// <remarks>
    /// The editor's own constructor reloads every set it is given, from the record's real id --
    /// so the book's id has to match the parent id the lines were seeded under, or the set a
    /// caller loaded beforehand is silently replaced with an empty one.
    /// </remarks>
    private static object EditorOverBookWith(Assembly assembly, params object[] sets)
    {
        var setsArray = Array.CreateInstance(assembly.GetType("ILineSet")!, sets.Length);
        for (var i = 0; i < sets.Length; i++)
            setsArray.SetValue(sets[i], i);

        var bookType = assembly.GetType("Book")!;
        var book = Activator.CreateInstance(bookType)!;
        bookType.GetProperty("Id")!.SetValue(book, 900);
        bookType.GetProperty("Title")!.SetValue(book, "Original");

        var rows = RecordEditorTests.Rows(assembly);
        rows.Clear();
        rows.Add(book);

        var editorType = assembly.GetType("RecordEditorViewModel`1")!.MakeGenericType(bookType);

        return Activator.CreateInstance(editorType, [book, false, setsArray])!;
    }

    private static List<object> Totals(object editor)
        => ((IEnumerable)editor.GetType().GetProperty("Totals")!.GetValue(editor)!).Cast<object>().ToList();

    private static string ColumnOf(object total) => (string)total.GetType().GetProperty("Column")!.GetValue(total)!;

    private static decimal ValueOf(object total) => (decimal)total.GetType().GetProperty("Value")!.GetValue(total)!;

    private static bool IsGrandOf(object total) => (bool)total.GetType().GetProperty("IsGrand")!.GetValue(total)!;

    // ---- HasAdditional ----

    /// <summary>A second model, with the long-text field Book has none of, so the Additional
    /// Information tab can be asserted both ways round in one assembly.</summary>
    private const string MemoStub = """
public class Memo
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Remarks { get; set; } = string.Empty;
}
""";

    /// <summary>A model with no long-text field must not grow an empty Additional Information
    /// tab, and one with a long-text field must show it -- asserted both ways round, over two
    /// models in one assembly, the same as the master screen's equivalent property.</summary>
    [TestMethod]
    public void Only_a_model_with_long_text_has_an_additional_tab()
    {
        var assembly = RecordEditorTests.Emit(validation: false, extra: [MemoStub]);

        Assert.IsFalse(HasAdditional(assembly, "Book"), "Book has no long-text field");
        Assert.IsTrue(HasAdditional(assembly, "Memo"), "Memo.Remarks is a long-text field");
    }

    private static bool HasAdditional(Assembly assembly, string model)
    {
        var recordType = assembly.GetType(model)!;
        var record = Activator.CreateInstance(recordType)!;

        var editorType = assembly.GetType("RecordEditorViewModel`1")!.MakeGenericType(recordType);
        var editor = Activator.CreateInstance(editorType, [record, false, null])!;

        return (bool)editorType.GetProperty("HasAdditional")!.GetValue(editor)!;
    }
}
