namespace AppScaffolding;

/// <summary>Emits a generic, reflection-driven CRUD menu over one entity's typed repository.
/// Entity-independent: one file drives every entity's list/add/delete.</summary>
public static class ConsoleMenuTemplate
{
    public static string Source(string projectName) => $$"""
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using {{projectName}}.Data;

namespace {{projectName}};

/// <summary>List / add / delete for one entity type, backed by its typed repository.</summary>
public sealed class EntityMenu<T> where T : class, new()
{
    private readonly Repository<T> _repository;

    public EntityMenu(Repository<T> repository) => _repository = repository;

    public void Run()
    {
        while (true)
        {
            Console.WriteLine($"\n{typeof(T).Name}: [l]ist  [a]dd  [d]elete  [b]ack");
            Console.Write("> ");
            switch (Console.ReadLine()?.Trim().ToLowerInvariant())
            {
                case "l": List(); break;
                case "a": Add(); break;
                case "d": Delete(); break;
                case "b": case "": case null: return;
                default: Console.WriteLine("Unrecognized command."); break;
            }
        }
    }

    private void List()
    {
        var all = _repository.GetAll();
        if (all.Count == 0) { Console.WriteLine("(none)"); return; }
        foreach (var item in all)
            Console.WriteLine(Describe(item));
    }

    private void Add()
    {
        var item = new T();
        foreach (var prop in Editable())
        {
            Console.Write($"{prop.Name} ({prop.PropertyType.Name}): ");
            var value = Parse(Console.ReadLine() ?? string.Empty, prop.PropertyType);
            if (value is not null) prop.SetValue(item, value);
        }
        _repository.Create(item);
        Console.WriteLine("Added.");
    }

    private void Delete()
    {
        Console.Write("Id to delete: ");
        if (int.TryParse(Console.ReadLine(), out var id) && _repository.Delete(id))
            Console.WriteLine("Deleted.");
        else
            Console.WriteLine("No such record.");
    }

    // Id is repository-assigned; audit fields are set on save; navigation properties are
    // resolved from foreign keys, not entered by hand.
    private static IEnumerable<PropertyInfo> Editable() =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite
                        && p.Name is not ("Id" or "CreatedOn" or "ModifiedOn")
                        && IsScalar(p.PropertyType));

    private static bool IsScalar(Type type) =>
        type == typeof(string) || type == typeof(int) || type == typeof(decimal)
        || type == typeof(bool) || type == typeof(DateTime);

    private static object? Parse(string raw, Type type)
    {
        if (type == typeof(string)) return raw;
        if (raw.Length == 0) return null;
        if (type == typeof(int)) return int.TryParse(raw, out var i) ? i : null;
        if (type == typeof(decimal)) return decimal.TryParse(raw, out var d) ? d : null;
        if (type == typeof(bool)) return bool.TryParse(raw, out var b) ? b : null;
        if (type == typeof(DateTime)) return DateTime.TryParse(raw, out var dt) ? dt : null;
        return null;
    }

    private static string Describe(T item)
    {
        var id = typeof(T).GetProperty("Id");
        var props = (id is null ? Editable() : Editable().Prepend(id)).ToList();
        return string.Join("  ", props.Select(p => $"{p.Name}={p.GetValue(item)}"));
    }
}
""";
}
