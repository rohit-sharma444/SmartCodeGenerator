using System.Collections;
using System.Reflection;
using AppScaffolding;
using AppScaffolding.Screens;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The lines of a transaction, edited inside its editor.
/// </summary>
/// <remarks>
/// The emitted code is compiled AND RUN, because everything worth asserting here is behaviour: that
/// a line is cloned rather than edited in place, that a removed line actually leaves the store, and
/// that the parent's id is stamped on save rather than typed in. A substring assertion could show
/// none of it.
/// </remarks>
[TestClass]
public class LineSetTests
{
    /// <summary>A parent, a line that points at it, and a memory store for each.</summary>
    private const string ModelStub = """
using System;
using System.Collections.Generic;
using System.Linq;

public sealed record StoreWrite(bool Success, string? Refusal)
{
    public static StoreWrite Ok { get; } = new(true, null);
}

public interface IStore<T> where T : new()
{
    List<T> GetAll();
    StoreWrite SaveAll(List<T> all);
    int NextId(IEnumerable<T> existing);
    T NewItem(IEnumerable<T> existing);
}

public sealed class MemoryStore<T> : IStore<T> where T : class, new()
{
    public static readonly List<T> Rows = new();
    public List<T> GetAll() => Rows.ToList();
    public StoreWrite SaveAll(List<T> all) { Rows.Clear(); Rows.AddRange(all); return StoreWrite.Ok; }
    public int NextId(IEnumerable<T> existing) => existing.Count() + 1;
    public T NewItem(IEnumerable<T> existing) => new();
}

public static class StoreFactory
{
    public static IStore<T> Create<T>() where T : class, new() => new MemoryStore<T>();
}

public class Order
{
    public int Id { get; set; }
    public string Reference { get; set; } = string.Empty;
}

public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string Item { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTime Due { get; set; }
    public DateTime? Shipped { get; set; }
    public DateTime CreatedOn { get; set; }
}
""";

    private static Assembly Emit()
    {
        var source = EmittedSource.Flatten(
            ModelStub,
            LineSetTemplate.Source("Acme")
                .Replace("namespace Acme.Screens;", string.Empty)
                .Replace("using Acme.Data;", string.Empty));

        EmittedSource.AssertCompiles(source);

        return Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
    }

    private static IList Rows(Assembly assembly)
        => (IList)assembly.GetType("MemoryStore`1")!
            .MakeGenericType(assembly.GetType("OrderLine")!)
            .GetField("Rows")!.GetValue(null)!;

    /// <summary>Seeds the store with lines for two different parents, so a filter that ignores the
    /// foreign key cannot pass.</summary>
    private static (object Set, Assembly Assembly) SetOverTwoParents()
    {
        var assembly = Emit();
        var lineType = assembly.GetType("OrderLine")!;
        var rows = Rows(assembly);

        rows.Clear();

        var id = 1;
        foreach (var (parent, item) in new[] { (1, "First"), (1, "Second"), (2, "Someone else's") })
        {
            var line = Activator.CreateInstance(lineType)!;
            lineType.GetProperty("Id")!.SetValue(line, id++);
            lineType.GetProperty("OrderId")!.SetValue(line, parent);
            lineType.GetProperty("Item")!.SetValue(line, item);
            rows.Add(line);
        }

        var setType = assembly.GetType("LineSet`1")!.MakeGenericType(lineType);

        return (Activator.CreateInstance(setType, ["OrderLine", "OrderId"])!, assembly);
    }

    private static void Load(object set, int parentId)
        => set.GetType().GetMethod("Load")!.Invoke(set, [parentId]);

    private static bool Save(object set, int parentId)
        => (bool)set.GetType().GetMethod("Save")!.Invoke(set, [parentId])!;

    private static List<object> Items(object set)
        => ((IEnumerable)set.GetType().GetProperty("Items")!.GetValue(set)!).Cast<object>().ToList();

    private static bool IsDirty(object set)
        => (bool)set.GetType().GetProperty("IsDirty")!.GetValue(set)!;

    private static string Item(object line)
        => (string)line.GetType().GetProperty("Item")!.GetValue(line)!;

    private static void SetItem(object line, string value)
        => line.GetType().GetProperty("Item")!.SetValue(line, value);

    private static int Parent(object line)
        => (int)line.GetType().GetProperty("OrderId")!.GetValue(line)!;

    [TestMethod]
    public void It_loads_only_the_lines_of_its_own_parent()
    {
        var (set, _) = SetOverTwoParents();

        Load(set, 1);

        CollectionAssert.AreEquivalent(
            new[] { "First", "Second" }, Items(set).Select(Item).ToList());
    }

    /// <summary>The reason the editor can be cancelled at all: nothing typed into a line may reach
    /// the store before Save.</summary>
    [TestMethod]
    public void The_lines_are_a_copy_so_an_edit_does_not_reach_the_store()
    {
        var (set, assembly) = SetOverTwoParents();

        Load(set, 1);
        SetItem(Items(set)[0], "Changed");

        var stored = Rows(assembly).Cast<object>().First(line => Parent(line) == 1);

        Assert.AreEqual("First", Item(stored));
    }

    [TestMethod]
    public void An_untouched_set_is_not_dirty_and_an_edited_one_is()
    {
        var (set, _) = SetOverTwoParents();

        Load(set, 1);

        Assert.IsFalse(IsDirty(set));

        SetItem(Items(set)[0], "Changed");

        Assert.IsTrue(IsDirty(set));
    }

    [TestMethod]
    public void Adding_and_removing_make_it_dirty_too()
    {
        var (set, _) = SetOverTwoParents();

        Load(set, 1);
        set.GetType().GetMethod("Add")!.Invoke(set, null);

        Assert.IsTrue(IsDirty(set));
        Assert.HasCount(3, Items(set));

        Load(set, 1);
        set.GetType().GetMethod("Remove")!.Invoke(set, [Items(set)[0]]);

        Assert.IsTrue(IsDirty(set));
        Assert.HasCount(1, Items(set));
    }

    [TestMethod]
    public void Save_writes_an_edit_through()
    {
        var (set, assembly) = SetOverTwoParents();

        Load(set, 1);
        SetItem(Items(set)[0], "Changed");

        Assert.IsTrue(Save(set, 1));

        var stored = Rows(assembly).Cast<object>()
            .Where(line => Parent(line) == 1)
            .Select(Item)
            .ToList();

        CollectionAssert.Contains(stored, "Changed");
    }

    /// <summary>A removed line has to LEAVE the store. Writing back only what is on screen is how
    /// that happens without matching up ids.</summary>
    [TestMethod]
    public void A_removed_line_is_gone_from_the_store_after_a_save()
    {
        var (set, assembly) = SetOverTwoParents();

        Load(set, 1);
        set.GetType().GetMethod("Remove")!.Invoke(set, [Items(set).First(l => Item(l) == "First")]);

        Assert.IsTrue(Save(set, 1));

        var remaining = Rows(assembly).Cast<object>().Select(Item).ToList();

        CollectionAssert.DoesNotContain(remaining, "First");
        CollectionAssert.Contains(remaining, "Second");
    }

    /// <summary>Another parent's lines are none of this set's business, and a whole-set replacement
    /// is exactly where that could go wrong.</summary>
    [TestMethod]
    public void A_save_leaves_another_parents_lines_alone()
    {
        var (set, assembly) = SetOverTwoParents();

        Load(set, 1);
        set.GetType().GetMethod("Add")!.Invoke(set, null);

        Assert.IsTrue(Save(set, 1));

        var others = Rows(assembly).Cast<object>().Where(line => Parent(line) == 2).ToList();

        Assert.HasCount(1, others);
        Assert.AreEqual("Someone else's", Item(others[0]));
    }

    /// <summary>
    /// A line added in the editor is stamped with the parent's id and given one of its own.
    /// </summary>
    /// <remarks>
    /// The stamping is why the foreign key is not a field anyone types into, and why the lines are
    /// saved AFTER the parent: a new parent has no id until it is written.
    /// </remarks>
    [TestMethod]
    public void A_new_line_gets_the_parents_id_and_an_id_of_its_own()
    {
        var (set, assembly) = SetOverTwoParents();

        Load(set, 7);

        Assert.IsEmpty(Items(set));

        set.GetType().GetMethod("Add")!.Invoke(set, null);
        SetItem(Items(set)[0], "Brand new");

        Assert.IsTrue(Save(set, 7));

        var added = Rows(assembly).Cast<object>().Single(line => Item(line) == "Brand new");

        Assert.AreEqual(7, Parent(added));
        Assert.AreNotEqual(0, added.GetType().GetProperty("Id")!.GetValue(added));
    }

    /// <summary>
    /// A new line's dates start at today, not 01-Jan-0001.
    /// </summary>
    /// <remarks>
    /// Midnight, because a SQL date column's rule refuses a time of day. A nullable date stays
    /// empty, and CreatedOn is left for the store to stamp with the time as well.
    /// </remarks>
    [TestMethod]
    public void A_new_line_starts_its_dates_at_today()
    {
        var (set, _) = SetOverTwoParents();

        Load(set, 7);
        set.GetType().GetMethod("Add")!.Invoke(set, null);

        var line = Items(set).Single();

        Assert.AreEqual(DateTime.Today, line.GetType().GetProperty("Due")!.GetValue(line));
        Assert.IsNull(line.GetType().GetProperty("Shipped")!.GetValue(line));
        Assert.AreEqual(default(DateTime), line.GetType().GetProperty("CreatedOn")!.GetValue(line));
    }

    [TestMethod]
    public void Two_new_lines_do_not_share_an_id()
    {
        var (set, assembly) = SetOverTwoParents();

        Load(set, 7);

        for (var i = 0; i < 2; i++)
        {
            set.GetType().GetMethod("Add")!.Invoke(set, null);
            SetItem(Items(set)[i], "Line " + i);
        }

        Assert.IsTrue(Save(set, 7));

        var ids = Rows(assembly).Cast<object>()
            .Where(line => Parent(line) == 7)
            .Select(line => (int)line.GetType().GetProperty("Id")!.GetValue(line)!)
            .ToList();

        Assert.HasCount(2, ids);
        Assert.HasCount(2, ids.Distinct().ToList());
    }

    /// <summary>Saving clears it, or the shell asks to discard an edit that is already stored.</summary>
    [TestMethod]
    public void Saving_clears_the_dirty_flag()
    {
        var (set, _) = SetOverTwoParents();

        Load(set, 1);
        SetItem(Items(set)[0], "Changed");

        Assert.IsTrue(Save(set, 1));
        Assert.IsFalse(IsDirty(set));
    }

    /// <summary>The views generate columns from this, and hide the foreign key by name.</summary>
    [TestMethod]
    public void It_reports_the_line_type_and_the_foreign_key_it_stamps()
    {
        var (set, assembly) = SetOverTwoParents();

        Assert.AreEqual("OrderLine", set.GetType().GetProperty("Title")!.GetValue(set));
        Assert.AreEqual("OrderId", set.GetType().GetProperty("ForeignKey")!.GetValue(set));
        Assert.AreEqual(
            assembly.GetType("OrderLine"), set.GetType().GetProperty("LineType")!.GetValue(set));
    }

    /// <summary>
    /// The collection has to be one both desktop stacks refresh from.
    /// </summary>
    /// <remarks>
    /// A WinForms DataGridView needs IBindingList to notice a row being added, and WPF wraps
    /// IBindingList in a collection view that tracks the same event. An ObservableCollection would
    /// leave the WinForms grid showing the old rows after Add.
    /// </remarks>
    [TestMethod]
    public void The_lines_are_a_binding_list_so_both_grids_see_an_add()
    {
        var (set, _) = SetOverTwoParents();

        var items = set.GetType().GetProperty("Items")!.GetValue(set)!;

        Assert.IsInstanceOfType<System.ComponentModel.IBindingList>(items);
    }

    [TestMethod]
    public void The_line_set_declares_clear_on_both_the_interface_and_the_class()
    {
        var source = LineSetTemplate.Source("Acme");

        StringAssert.Contains(source, "void Clear();");
        StringAssert.Contains(source, "public void Clear()");
    }

    /// <summary>The same pairing for <c>LineEdited</c>: a view can only call it through the
    /// interface, which is all any of the three front ends holds.</summary>
    [TestMethod]
    public void The_line_set_declares_line_edited_on_both_the_interface_and_the_class()
    {
        var source = LineSetTemplate.Source("Acme");

        StringAssert.Contains(source, "void LineEdited();");
        StringAssert.Contains(source, "public void LineEdited()");
    }

    /// <summary>Clearing empties the set and leaves it dirty, so Cancel can still put the lines
    /// back — nothing has reached the store. It also drops whatever was selected: a bound grid's
    /// Clear All button must not leave Selected pointing at a line that is no longer in the
    /// list.</summary>
    [TestMethod]
    public void Clearing_empties_a_loaded_set_and_marks_it_dirty()
    {
        var (set, _) = SetOverTwoParents();

        Load(set, 1);
        set.GetType().GetProperty("Selected")!.SetValue(set, Items(set)[0]);

        Assert.IsFalse(IsDirty(set));

        set.GetType().GetMethod("Clear")!.Invoke(set, null);

        Assert.IsEmpty(Items(set));
        Assert.IsTrue(IsDirty(set));
        Assert.IsNull(set.GetType().GetProperty("Selected")!.GetValue(set), "a cleared set must not still point Selected at a gone line");
    }

    /// <summary>
    /// A value typed straight into a line is announced on the same channel an add is.
    /// </summary>
    /// <remarks>
    /// A generated model is a plain class with no change notification, so a grid writing a typed
    /// value into a line reaches the object without the set — or anything derived from it, such as
    /// the editor's totals band — hearing about it. <c>LineEdited</c> is how a view says so, and
    /// it raises the set's own list event rather than inventing a second channel: every consumer
    /// already listens to that one, because it is what makes both desktop grids see an add.
    /// </remarks>
    [TestMethod]
    public void LineEdited_announces_a_change_the_set_cannot_see_for_itself()
    {
        var (set, _) = SetOverTwoParents();

        Load(set, 1);

        var raised = 0;
        ((System.ComponentModel.IBindingList)set.GetType().GetProperty("Items")!.GetValue(set)!)
            .ListChanged += (_, _) => raised++;

        SetItem(Items(set)[0], "Typed into");
        Assert.AreEqual(0, raised, "a plain model cannot announce its own change");

        set.GetType().GetMethod("LineEdited")!.Invoke(set, null);

        Assert.IsGreaterThan(0, raised);
    }

    /// <summary>Nothing is written until the editor saves — the same bargain Add and Remove
    /// already make.</summary>
    [TestMethod]
    public void Clearing_writes_nothing_to_the_store()
    {
        var (set, assembly) = SetOverTwoParents();

        Load(set, 1);
        var before = Rows(assembly).Count;

        set.GetType().GetMethod("Clear")!.Invoke(set, null);

        Assert.HasCount(before, Rows(assembly));
    }
}
