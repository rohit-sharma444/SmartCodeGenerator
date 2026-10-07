using System.Reflection;
using AppScaffolding;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The console menu's Add, run for real: the emitted EntityMenu compiled against a stub
/// repository and driven through Console.SetIn. It used to drop a value that would not parse --
/// "abc" for an int, 13/31/2025 for a date -- and say "Added." with OrderNo=0 and
/// Date=0001-01-01. Now it says why and asks again.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ConsoleMenuInputTests
{
    private const string Stubs = """
        using System;
        using System.Collections.Generic;

        namespace Acme.Models
        {
            public class Order
            {
                public int Id { get; set; }
                public int OrderNo { get; set; }
                public DateTime Date { get; set; }
                public decimal? Discount { get; set; }
                public string Note { get; set; } = string.Empty;
            }
        }

        namespace Acme.Data
        {
            public sealed record Failure(string? Property, string Message);

            public sealed class CreateResult
            {
                public bool Success => true;
                public List<Failure> Failures { get; } = new();
            }

            public sealed class DeleteResult
            {
                public bool Success => false;
                public bool NotFound => true;
                public List<string> Errors { get; } = new();
            }

            public class Repository<T>
            {
                public List<T> Items { get; } = new();
                public List<T> GetAll() => Items;
                public CreateResult Create(T item) { Items.Add(item); return new CreateResult(); }
                public DeleteResult Delete(int id) => new DeleteResult();
            }
        }
        """;

    private static (string Output, object? Added) Add(string input)
    {
        var result = Compiler.CompileSources(
        [
            ("Stubs.cs", Stubs),
            ("EntityMenu.cs", ConsoleMenuTemplate.Source("Acme")),
        ]);

        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));

        var assembly = Assembly.Load(result.AssemblyBytes!);
        var order = assembly.GetType("Acme.Models.Order")!;
        var repository = Activator.CreateInstance(assembly.GetType("Acme.Data.Repository`1")!.MakeGenericType(order))!;
        Func<Type, System.Collections.IEnumerable?> related = _ => null;
        var menu = Activator.CreateInstance(
            assembly.GetType("Acme.EntityMenu`1")!.MakeGenericType(order), repository, related)!;

        var oldIn = Console.In;
        var oldOut = Console.Out;
        var output = new StringWriter();

        try
        {
            Console.SetIn(new StringReader(input));
            Console.SetOut(output);
            menu.GetType().GetMethod("Run")!.Invoke(menu, null);
        }
        finally
        {
            Console.SetIn(oldIn);
            Console.SetOut(oldOut);
        }

        var items = (System.Collections.IList)repository.GetType().GetProperty("Items")!.GetValue(repository)!;
        return (output.ToString(), items.Count == 0 ? null : items[0]);
    }

    [TestMethod]
    public void A_value_that_will_not_parse_is_refused_and_asked_for_again()
    {
        var (output, added) = Add("a\nabc\n42\n2025-13-31\n2025-03-31\n\nhello\nb\n");

        StringAssert.Contains(output, "'abc' is not a whole number");
        StringAssert.Contains(output, "'2025-13-31' is not a date");
        StringAssert.Contains(output, "Added.");

        Assert.IsNotNull(added);
        Assert.AreEqual(42, added.GetType().GetProperty("OrderNo")!.GetValue(added));
        Assert.AreEqual(new DateTime(2025, 3, 31), added.GetType().GetProperty("Date")!.GetValue(added));
        Assert.IsNull(added.GetType().GetProperty("Discount")!.GetValue(added));
    }

    /// <summary>A blank number is not 0: only a nullable property may be left empty.</summary>
    [TestMethod]
    public void A_blank_for_a_number_that_cannot_be_empty_is_asked_for_again()
    {
        var (output, added) = Add("a\n\n7\n2025-01-02\n\n\nb\n");

        StringAssert.Contains(output, "OrderNo needs a value");
        Assert.AreEqual(7, added!.GetType().GetProperty("OrderNo")!.GetValue(added));
    }

    /// <summary>Input that ends mid-record adds nothing rather than looping or saving a half-typed one.</summary>
    [TestMethod]
    public void Input_that_ends_mid_record_adds_nothing()
    {
        var (output, added) = Add("a\nabc\n");

        Assert.IsNull(added);
        Assert.DoesNotContain("Added.", output);
    }
}
