namespace AppScaffolding;

/// <summary>Emits a generic, reflection-driven CRUD menu over one entity's typed repository.
/// Entity-independent: one file drives every entity's list/add/delete.</summary>
public static class ConsoleMenuTemplate
{
    public static string Source(string rootNamespace) => $$"""
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using {{rootNamespace}}.Data;

namespace {{rootNamespace}};

/// <summary>List / add / delete for one entity type, backed by its typed repository.</summary>
public sealed class EntityMenu<T> where T : class, new()
{
    private readonly Repository<T> _repository;
    private readonly Func<Type, System.Collections.IEnumerable?> _related;

    public EntityMenu(Repository<T> repository, Func<Type, System.Collections.IEnumerable?> related)
    {
        _repository = repository;
        _related = related;
    }

    public void Run()
    {
        while (true)
        {
            // System.Console is fully qualified: this file's namespace ends in ".Console"
            // for the console target, which would otherwise shadow the bare name.
            System.Console.WriteLine($"\n{typeof(T).Name}: [l]ist  [a]dd  [d]elete  [b]ack");
            System.Console.Write("> ");
            switch (System.Console.ReadLine()?.Trim().ToLowerInvariant())
            {
                case "l": List(); break;
                case "a": Add(); break;
                case "d": Delete(); break;
                case "b": case "": case null: return;
                default: System.Console.WriteLine("Unrecognized command."); break;
            }
        }
    }

    private void List()
    {
        var all = _repository.GetAll();
        if (all.Count == 0) { System.Console.WriteLine("(none)"); return; }
        foreach (var item in all)
            System.Console.WriteLine(Describe(item));
    }

    private void Add()
    {
        var item = new T();
        foreach (var prop in Editable())
        {
            if (TryPickForeignKey(prop, out var foreignKey))
            {
                prop.SetValue(item, foreignKey);
                continue;
            }

            // A nullable property ("decimal?") is one whose source data had gaps: it is prompted
            // as its underlying type, and a blank answer leaves it empty rather than 0.
            var underlying = Nullable.GetUnderlyingType(prop.PropertyType);
            var type = underlying ?? prop.PropertyType;

            // Asked again until the answer parses. A value that would not parse used to be
            // dropped silently, leaving 0 or 0001-01-01, and the record was still "Added.".
            while (true)
            {
                System.Console.Write(underlying is null
                    ? $"{prop.Name} ({prop.PropertyType.Name}): "
                    : $"{prop.Name} ({underlying.Name}, blank for none): ");

                var raw = System.Console.ReadLine();

                // The input ended (a piped script ran out): add nothing rather than half a record.
                if (raw is null)
                {
                    System.Console.WriteLine();
                    System.Console.WriteLine("Not added.");
                    return;
                }

                raw = raw.Trim();

                if (raw.Length == 0 && type != typeof(string))
                {
                    if (underlying is not null)
                        break;

                    System.Console.WriteLine($"  {prop.Name} needs a value.");
                    continue;
                }

                if (TryParse(raw, type, out var value))
                {
                    prop.SetValue(item, value);
                    break;
                }

                System.Console.WriteLine($"  '{raw}' is not {Kind(type)}. Try again.");
            }
        }

        var result = _repository.Create(item);
        if (!result.Success)
        {
            foreach (var failure in result.Failures)
            {
                System.Console.WriteLine(failure.Property is null
                    ? "  " + failure.Message
                    : $"  {failure.Property}: {failure.Message}");
            }
            System.Console.WriteLine("Not added.");
            return;
        }

        System.Console.WriteLine("Added.");
    }

    private void Delete()
    {
        System.Console.Write("Id to delete: ");

        if (!int.TryParse(System.Console.ReadLine(), out var id))
        {
            System.Console.WriteLine("That is not a number.");
            return;
        }

        // A delete can be refused as well as missed, and the two read differently: "no such
        // record" for an Id nobody has, the validation pipeline's own sentences for a rule or a
        // permission that said no.
        var result = _repository.Delete(id);

        if (result.Success)
        {
            System.Console.WriteLine("Deleted.");
            return;
        }

        if (result.NotFound)
        {
            System.Console.WriteLine("No record with that Id.");
            return;
        }

        foreach (var error in result.Errors)
            System.Console.WriteLine("  " + error);

        System.Console.WriteLine("Not deleted.");
    }

    // A foreign key is an int <Nav>Id whose sibling <Nav> is one of the app's entity types.
    // When it is, offer a numbered picker over that entity's existing rows instead of a raw id.
    // Returns true when the property IS a foreign key (whether or not a row was picked), so the
    // caller does not also prompt for a bare number.
    private bool TryPickForeignKey(PropertyInfo prop, out int foreignKey)
    {
        foreignKey = 0;

        if (prop.PropertyType != typeof(int)
            || !prop.Name.EndsWith("Id", StringComparison.Ordinal)
            || prop.Name.Length <= 2)
            return false;

        var navName = prop.Name.Substring(0, prop.Name.Length - 2);
        var nav = typeof(T).GetProperty(navName);
        if (nav is null)
            return false;

        var rows = _related(nav.PropertyType);
        if (rows is null)
            return false;   // sibling is not a known entity type -- an ordinary int

        var candidates = rows.Cast<object>().ToList();
        if (candidates.Count == 0)
        {
            System.Console.WriteLine($"{navName}: (no records yet -- leaving unset)");
            return true;
        }

        System.Console.WriteLine($"{navName}:");
        for (var i = 0; i < candidates.Count; i++)
            System.Console.WriteLine($"  {i + 1}. {Label(candidates[i])}");
        System.Console.Write("> ");

        if (int.TryParse(System.Console.ReadLine(), out var choice)
            && choice >= 1 && choice <= candidates.Count)
            foreignKey = IdOf(candidates[choice - 1]);

        return true;
    }

    private static int IdOf(object row)
    {
        var id = row.GetType().GetProperty("Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return id?.GetValue(row) is int value ? value : 0;
    }

    // The same display convention the WPF combo box uses: a Name or Title, else the first
    // string field, else just the Id -- so a picked row reads as more than a bare number.
    private static string Label(object row)
    {
        var type = row.GetType();
        var display = type.GetProperty("Name") ?? type.GetProperty("Title")
            ?? type.GetProperties().FirstOrDefault(p => p.PropertyType == typeof(string));
        var text = display?.GetValue(row)?.ToString();
        return string.IsNullOrWhiteSpace(text) ? $"#{IdOf(row)}" : $"{text} (#{IdOf(row)})";
    }

    // Id is repository-assigned; audit fields are set on save; navigation properties are
    // resolved from foreign keys, not entered by hand.
    private static IEnumerable<PropertyInfo> Editable() =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite
                        && p.Name is not ("Id" or "CreatedOn" or "ModifiedOn")
                        && IsScalar(p.PropertyType));

    private static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        return type == typeof(string) || type == typeof(int) || type == typeof(decimal)
               || type == typeof(bool) || type == typeof(DateTime);
    }

    private static bool TryParse(string raw, Type type, out object? value)
    {
        value = null;

        if (type == typeof(string)) { value = raw; return true; }
        if (type == typeof(int) && int.TryParse(raw, out var i)) { value = i; return true; }
        if (type == typeof(decimal) && decimal.TryParse(raw, out var d)) { value = d; return true; }
        if (type == typeof(bool) && bool.TryParse(raw, out var b)) { value = b; return true; }
        if (type == typeof(DateTime) && DateTime.TryParse(raw, out var dt)) { value = dt; return true; }

        return false;
    }

    /// <summary>What a refused answer should have been, for the message that asks again.</summary>
    private static string Kind(Type type)
    {
        if (type == typeof(int)) return "a whole number";
        if (type == typeof(decimal)) return "a number";
        if (type == typeof(bool)) return "true or false";
        if (type == typeof(DateTime)) return $"a date (for example {DateTime.Today:d})";
        return "a valid " + type.Name;
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
