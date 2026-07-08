namespace AppScaffolding;

/// <summary>Builds the console-menu Program.cs for the generated CRUD app.</summary>
public static class ProgramTemplate
{
    public static string Build(IReadOnlyList<string> entityNames)
    {
        var menu = string.Join("\n",
            entityNames.Select((name, i) =>
                $"    Console.WriteLine(\"{i + 1}) {name}\");"));

        var cases = string.Join("\n",
            entityNames.Select((name, i) =>
                $"        case \"{i + 1}\": Crud.Run<{name}>(\"{name}\"); break;"));

        return $$"""
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using GeneratedCode;

while (true)
{
    Console.WriteLine("\n== Menu ==");
{{menu}}
    Console.WriteLine("0) Exit");
    Console.Write("> ");
    var choice = Console.ReadLine();
    if (choice is null or "0")
        break;
    switch (choice)
    {
{{cases}}
        default: Console.WriteLine("Unknown option."); break;
    }
}

static class Crud
{
    public static void Run<T>(string name) where T : new()
    {
        var store = new JsonStore<T>();
        while (true)
        {
            Console.WriteLine($"\n== {name} ==");
            Console.WriteLine("1) Add  2) List  3) Edit  4) Delete  0) Back");
            Console.Write("> ");
            switch (Console.ReadLine())
            {
                case "1": Add(store); break;
                case "2": List(store); break;
                case "3": Edit(store); break;
                case "4": Delete(store); break;
                case "0": case null: return;
            }
        }
    }

    static void Add<T>(JsonStore<T> store) where T : new()
    {
        var item = new T();
        foreach (var p in Editable(typeof(T)))
            PromptInto(item, p);
        store.Add(item);
        Console.WriteLine("Added.");
    }

    static void List<T>(JsonStore<T> store) where T : new()
    {
        var all = store.GetAll();
        if (all.Count == 0)
        {
            Console.WriteLine("(none)");
            return;
        }
        foreach (var item in all)
            Console.WriteLine(Describe(item!));
    }

    static void Edit<T>(JsonStore<T> store) where T : new()
    {
        Console.Write("Id to edit: ");
        if (!int.TryParse(Console.ReadLine(), out var id))
            return;
        var item = store.GetAll()
            .FirstOrDefault(x => (int)IdProperty(typeof(T)).GetValue(x!)! == id);
        if (item is null)
        {
            Console.WriteLine("Not found.");
            return;
        }
        foreach (var p in Editable(typeof(T)))
            PromptInto(item, p, keepOnBlank: true);
        store.Update(item);
        Console.WriteLine("Updated.");
    }

    static void Delete<T>(JsonStore<T> store) where T : new()
    {
        Console.Write("Id to delete: ");
        if (!int.TryParse(Console.ReadLine(), out var id))
            return;
        Console.WriteLine(store.Delete(id) ? "Deleted." : "Not found.");
    }

    static PropertyInfo IdProperty(Type t) =>
        t.GetProperty("Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!;

    static IEnumerable<PropertyInfo> Editable(Type t) =>
        t.GetProperties()
         .Where(p => p.CanWrite &&
                     !string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase));

    static string Describe(object item) =>
        string.Join(", ", item.GetType().GetProperties()
            .Select(p => $"{p.Name}={p.GetValue(item)}"));

    static void PromptInto(object item, PropertyInfo p, bool keepOnBlank = false)
    {
        var current = p.GetValue(item);
        Console.Write(keepOnBlank ? $"{p.Name} [{current}]: " : $"{p.Name}: ");
        var raw = Console.ReadLine();
        if (string.IsNullOrEmpty(raw))
            return;
        try
        {
            var converter = TypeDescriptor.GetConverter(p.PropertyType);
            p.SetValue(item, converter.ConvertFromInvariantString(raw));
        }
        catch
        {
            // Unparseable input: leave the property unchanged.
        }
    }
}
""";
    }
}
