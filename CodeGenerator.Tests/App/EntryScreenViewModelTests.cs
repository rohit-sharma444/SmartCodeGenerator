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

    /// <summary>
    /// A set with two money columns, one of them a double, so the sums (not merely a declaration)
    /// are proved -- and IsGrand must be false for both, since neither is alone with its column.
    /// </summary>
    /// <remarks>
    /// OrderLine's Id and BookId are plain <c>int</c>, so the exclusion predicate's TYPE clause
    /// alone already keeps them out of the band regardless of their value -- proving the NAME
    /// clause (an Id typed as money) is
    /// <c>Totals_excludes_a_money_typed_foreign_key_by_name_and_marks_the_remaining_column_grand</c>'s
    /// job, not this test's.
    /// </remarks>
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

        Assert.AreEqual(15.75m, ValueOf(unitPrice), "the two lines' UnitPrice values, summed");
        Assert.AreEqual(4.0m, ValueOf(weight), "a double column is summed too");
        Assert.IsFalse(IsGrandOf(unitPrice), "two money columns means neither is the grand total");
        Assert.IsFalse(IsGrandOf(weight));
    }

    /// <summary>
    /// IsGrand is only true when a set has exactly one money column -- the reverse of the
    /// two-column case above, because a rule proved in one direction is only half proved.
    /// </summary>
    /// <remarks>
    /// InvoiceLine also carries SupplierId: money-TYPED (decimal) but foreign-key-SHAPED (ends
    /// with "Id"). OrderLine's Id/BookId are int, so the type filter alone already keeps them out
    /// of the band -- seeding them large proves nothing about the name predicate. SupplierId is
    /// the case that actually exercises it: seeded 900 on both lines, if the name predicate were
    /// deleted it would join the band as an 1800 row and Amount's only-column count would rise to
    /// two, flipping IsGrand false.
    /// </remarks>
    [TestMethod]
    public void Totals_excludes_a_money_typed_foreign_key_by_name_and_marks_the_remaining_column_grand()
    {
        var assembly = EmitWithTotals();

        SeedLine(assembly, "InvoiceLine", id: 900, bookId: 900, ("Amount", 40m), ("SupplierId", 900m));
        SeedLine(assembly, "InvoiceLine", id: 901, bookId: 900, ("Amount", 60m), ("SupplierId", 900m));

        var set = LineSetOf(assembly, "InvoiceLine", "BookId");
        set.GetType().GetMethod("Load")!.Invoke(set, [900]);

        var editor = EditorOverBookWith(assembly, set);
        var totals = Totals(editor);

        Assert.HasCount(1, totals, "SupplierId is money-typed but named like a foreign key and must not join the band");

        var total = totals.Single();

        Assert.AreEqual("Amount", ColumnOf(total));
        Assert.AreEqual(100m, ValueOf(total), "SupplierId, seeded 900 on each line, must not leak into the sum");
        Assert.IsTrue(IsGrandOf(total), "one money column is the only case where a grand total is not a guess");
    }

    /// <summary>
    /// IsGrand is decided per SET, not across the whole band.
    /// </summary>
    /// <remarks>
    /// Two sets, each with exactly one money column of its own, so a mutation that scopes IsGrand
    /// globally (comparing against the total money-column count across every set) or that only
    /// walks the first set (<c>Lines.Take(1)</c>) both surface here: the first shows up as
    /// IsGrand:false on a set that is genuinely alone with its column, and the second shows up as
    /// a missing row for the second set entirely.
    /// </remarks>
    [TestMethod]
    public void Totals_marks_the_grand_total_of_each_set_independently()
    {
        var assembly = EmitWithTotals();

        SeedLine(assembly, "InvoiceLine", id: 900, bookId: 900, ("Amount", 40m), ("SupplierId", 900m));
        SeedLine(assembly, "PaymentLine", id: 900, bookId: 900, ("Received", 25m));

        var invoiceSet = LineSetOf(assembly, "InvoiceLine", "BookId");
        invoiceSet.GetType().GetMethod("Load")!.Invoke(invoiceSet, [900]);

        var paymentSet = LineSetOf(assembly, "PaymentLine", "BookId");
        paymentSet.GetType().GetMethod("Load")!.Invoke(paymentSet, [900]);

        var editor = EditorOverBookWith(assembly, invoiceSet, paymentSet);
        var totals = Totals(editor);

        Assert.HasCount(2, totals, "each set contributes its own row -- neither is dropped");

        var amount = totals.Single(t => ColumnOf(t) == "Amount");
        var received = totals.Single(t => ColumnOf(t) == "Received");

        Assert.IsTrue(IsGrandOf(amount), "Invoice's only money column is its own grand total");
        Assert.IsTrue(IsGrandOf(received), "Payment's only money column is its own grand total, independent of Invoice's");
    }

    // ---- the band repaints when the lines change ----

    /// <summary>
    /// A line added to the set announces the totals.
    /// </summary>
    /// <remarks>
    /// <c>Totals</c> is a computed property, and a computed property announces nothing by itself:
    /// a band bound to it paints once and then goes stale — the mockup's most visible number,
    /// silently wrong. The lines are the thing that changes and the editor is the thing bound, so
    /// the set publishes the change on the list event both desktop grids already listen to, and
    /// the editor translates it into the one property that derives from it.
    /// </remarks>
    [TestMethod]
    public void Adding_a_line_announces_the_totals()
    {
        var assembly = EmitWithTotals();
        var set = LineSetOf(assembly, "PaymentLine", "BookId");
        var editor = EditorOverBookWith(assembly, set);
        var announced = Announcements(editor);

        set.GetType().GetMethod("Add")!.Invoke(set, null);

        Assert.Contains("Totals", announced);
    }

    /// <summary>Removing and clearing go the same way: every collection change the set can see
    /// reaches the band.</summary>
    [TestMethod]
    public void Removing_and_clearing_lines_announce_the_totals()
    {
        var assembly = EmitWithTotals();

        SeedLine(assembly, "PaymentLine", id: 910, bookId: 900, ("Received", 4m));
        SeedLine(assembly, "PaymentLine", id: 911, bookId: 900, ("Received", 6m));

        var set = LineSetOf(assembly, "PaymentLine", "BookId");
        var editor = EditorOverBookWith(assembly, set);
        var announced = Announcements(editor);

        set.GetType().GetMethod("Remove")!.Invoke(set, [LinesOf(set)[0]]);

        Assert.Contains("Totals", announced);

        announced.Clear();
        set.GetType().GetMethod("Clear")!.Invoke(set, null);

        Assert.Contains("Totals", announced);
    }

    /// <summary>
    /// A value typed into a line announces the totals, and the band shows the NEW sum.
    /// </summary>
    /// <remarks>
    /// The case the collection events cannot cover: a grid writes a typed value straight into the
    /// line object, which is a plain generated model with no change notification, so the set never
    /// sees it. The middle assertion is what proves the announcement comes from
    /// <c>LineEdited</c> — without it, a fix that announced the totals on any activity at all
    /// would pass.
    /// </remarks>
    [TestMethod]
    public void A_value_typed_into_a_line_announces_the_totals_and_the_new_sum()
    {
        var assembly = EmitWithTotals();

        SeedLine(assembly, "PaymentLine", id: 920, bookId: 900, ("Received", 4m));

        var set = LineSetOf(assembly, "PaymentLine", "BookId");
        var editor = EditorOverBookWith(assembly, set);
        var announced = Announcements(editor);

        var line = LinesOf(set)[0];
        line.GetType().GetProperty("Received")!.SetValue(line, 11m);

        Assert.IsEmpty(announced, "a plain model cannot announce its own change");

        set.GetType().GetMethod("LineEdited")!.Invoke(set, null);

        Assert.Contains("Totals", announced);
        Assert.AreEqual(11m, ValueOf(Totals(editor).Single()), "the band recomputed rather than repainting");
    }

    /// <summary>Every property name the editor announces, in the order it announced them.</summary>
    private static List<string> Announcements(object editor)
    {
        var announced = new List<string>();

        ((System.ComponentModel.INotifyPropertyChanged)editor).PropertyChanged +=
            (_, e) => announced.Add(e.PropertyName!);

        return announced;
    }

    private static List<object> LinesOf(object set)
        => ((IEnumerable)set.GetType().GetProperty("Items")!.GetValue(set)!).Cast<object>().ToList();

    /// <summary>Three line types: one with two money columns, and two others each with exactly one
    /// -- of different names, so IsGrand's per-set scoping can be proved without a second compiled
    /// fixture.</summary>
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
    public decimal SupplierId { get; set; }
}

public class PaymentLine
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public decimal Received { get; set; }
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
