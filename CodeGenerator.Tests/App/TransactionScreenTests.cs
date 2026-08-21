using System.Collections;
using System.Reflection;
using AppScaffolding;
using AppScaffolding.Screens;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>A transaction differs from a master in exactly three ways: a date range, column
/// totals, and its role. Each is proved by running the emitted code.</summary>
[TestClass]
public class TransactionScreenTests
{
    /// <summary>An order with a real date, two money columns, an Id and a foreign key — every
    /// shape the totals have to include or exclude.</summary>
    private const string OrderStub = """
using System;
using System.Collections.Generic;
using System.Linq;

public interface IStore<T> where T : new()
{
    List<T> GetAll();
    bool SaveAll(List<T> all);
    int NextId(IEnumerable<T> existing);
    T NewItem(IEnumerable<T> existing);
}

public sealed class MemoryStore<T> : IStore<T> where T : class, new()
{
    public static readonly List<T> Rows = new();
    public List<T> GetAll() => Rows.ToList();
    public bool SaveAll(List<T> all) { Rows.Clear(); Rows.AddRange(all); return true; }
    public int NextId(IEnumerable<T> existing) => Rows.Count + 1;
    public T NewItem(IEnumerable<T> existing) => new();
}

public static class StoreFactory
{
    public static IStore<T> Create<T>() where T : class, new() => new MemoryStore<T>();
}

public static class Session
{
    public static bool IsAdmin { get; set; } = true;
}

public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public decimal Amount { get; set; }
    public decimal Freight { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime ModifiedOn { get; set; }
}

/// <summary>A model whose only dates are the audit pair.</summary>
public class Note
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedOn { get; set; }
    public DateTime ModifiedOn { get; set; }
}
""";

    private static Assembly Emit()
    {
        var source = EmittedSource.Flatten(
            OrderStub,
            ViewModelTemplates.ObservableObject("Acme").Replace("namespace Acme.ViewModels;", string.Empty),
            ViewModelTemplates.PortableRelayCommand("Acme").Replace("namespace Acme.ViewModels;", string.Empty),
            ScreenTemplates.RoleEnum("Acme").Replace("namespace Acme.Screens;", string.Empty),
            ViewModelTemplates.EntityViewModelInterface("Acme")
                .Replace("namespace Acme.ViewModels;", string.Empty)
                .Replace("using Acme.Screens;", string.Empty),
            RecordEditorTests.Editor(validation: false),
            MasterScreenTemplate.Source("Acme")
                .Replace("namespace Acme.ViewModels;", string.Empty)
                .Replace("using Acme.Auth;", string.Empty)
                .Replace("using Acme.Data;", string.Empty)
                .Replace("using Acme.Screens;", string.Empty),
            TransactionScreenTemplate.Source("Acme")
                .Replace("namespace Acme.ViewModels;", string.Empty)
                .Replace("using Acme.Screens;", string.Empty));

        EmittedSource.AssertCompiles(source);

        return Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
    }

    private static object Screen(Assembly assembly, string model, params (int Day, decimal Amount)[] rows)
    {
        var type = assembly.GetType(model)!;

        var store = (IList)assembly.GetType("MemoryStore`1")!
            .MakeGenericType(type).GetField("Rows")!.GetValue(null)!;

        store.Clear();

        var id = 1;
        foreach (var (day, amount) in rows)
        {
            var row = Activator.CreateInstance(type)!;
            type.GetProperty("Id")!.SetValue(row, id++);

            if (type.GetProperty("OrderDate") is { } date)
                date.SetValue(row, new DateTime(2026, 8, day));

            if (type.GetProperty("Amount") is { } money)
                money.SetValue(row, amount);

            if (type.GetProperty("Freight") is { } freight)
                freight.SetValue(row, 1m);

            store.Add(row);
        }

        var screenType = assembly.GetType("TransactionScreenViewModel`1")!.MakeGenericType(type);

        return Activator.CreateInstance(screenType, [model])!;
    }

    private static IList Items(object screen)
        => ((IEnumerable)screen.GetType().GetProperty("Items")!.GetValue(screen)!)
            .Cast<object>().ToList();

    private static IList Totals(object screen)
        => ((IEnumerable)screen.GetType().GetProperty("Totals")!.GetValue(screen)!)
            .Cast<object>().ToList();

    private static decimal TotalFor(object screen, string column)
    {
        foreach (var total in Totals(screen))
        {
            if ((string)total.GetType().GetProperty("Column")!.GetValue(total)! == column)
                return (decimal)total.GetType().GetProperty("Value")!.GetValue(total)!;
        }

        throw new AssertFailedException($"no total for {column}");
    }

    [TestMethod]
    public void The_role_is_transaction()
    {
        var screen = Screen(Emit(), "Order", (1, 10m));

        Assert.AreEqual(
            "Transaction", screen.GetType().GetProperty("Role")!.GetValue(screen)!.ToString());
    }

    [TestMethod]
    public void A_date_range_narrows_the_list()
    {
        var screen = Screen(Emit(), "Order", (1, 10m), (20, 20m));

        screen.GetType().GetProperty("From")!.SetValue(screen, new DateTime(2026, 8, 10));

        Assert.HasCount(1, Items(screen));
    }

    /// <summary>The audit-field trap again, this time in the running app: every class carries
    /// CreatedOn, so a naive "first DateTime" would filter every screen on a field nobody
    /// chose.</summary>
    [TestMethod]
    public void A_model_whose_only_dates_are_audit_fields_has_no_range()
    {
        var screen = Screen(Emit(), "Note");

        Assert.AreEqual(string.Empty, screen.GetType().GetProperty("DateField")!.GetValue(screen));
        Assert.IsFalse((bool)screen.GetType().GetProperty("HasDateField")!.GetValue(screen)!);
    }

    [TestMethod]
    public void The_range_binds_to_the_entitys_own_date()
    {
        var screen = Screen(Emit(), "Order", (1, 10m));

        Assert.AreEqual("OrderDate", screen.GetType().GetProperty("DateField")!.GetValue(screen));
    }

    [TestMethod]
    public void Every_money_column_is_totalled()
    {
        var screen = Screen(Emit(), "Order", (1, 10m), (2, 20m));

        Assert.HasCount(2, Totals(screen));
        Assert.AreEqual(30m, TotalFor(screen, "Amount"));
        Assert.AreEqual(2m, TotalFor(screen, "Freight"));
    }

    /// <summary>An Id is a number and is never money. Summing a foreign key produces a confident
    /// nonsense figure.</summary>
    [TestMethod]
    public void Ids_and_foreign_keys_are_not_totalled()
    {
        var columns = Totals(Screen(Emit(), "Order", (1, 10m)))
            .Cast<object>()
            .Select(t => (string)t.GetType().GetProperty("Column")!.GetValue(t)!)
            .ToList();

        CollectionAssert.DoesNotContain(columns, "Id");
        CollectionAssert.DoesNotContain(columns, "CustomerId");
    }

    /// <summary>The totals are of what is on screen, not of the whole store — a total that
    /// ignored the filter would contradict the rows above it.</summary>
    [TestMethod]
    public void Totals_follow_the_filter()
    {
        var screen = Screen(Emit(), "Order", (1, 10m), (20, 20m));

        Assert.AreEqual(30m, TotalFor(screen, "Amount"));

        screen.GetType().GetProperty("From")!.SetValue(screen, new DateTime(2026, 8, 10));

        Assert.AreEqual(20m, TotalFor(screen, "Amount"));
    }

    /// <summary>Search and the range compose, rather than one replacing the other.</summary>
    [TestMethod]
    public void The_search_and_the_range_apply_together()
    {
        var assembly = Emit();
        var screen = Screen(assembly, "Order", (1, 10m), (20, 20m));

        screen.GetType().GetProperty("From")!.SetValue(screen, new DateTime(2026, 8, 10));
        screen.GetType().GetProperty("Search")!.SetValue(screen, "nothing-matches-this");

        Assert.IsEmpty(Items(screen));
    }
}
