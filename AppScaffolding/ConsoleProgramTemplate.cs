namespace AppScaffolding;

/// <summary>Emits the console app entry point: a top-level menu registering one
/// <c>EntityMenu&lt;T&gt;</c> per discovered entity.</summary>
public static class ConsoleProgramTemplate
{
    public static string Build(string projectName, IReadOnlyList<string> entityNames)
        => Build(projectName, entityNames, projectName);

    /// <summary>As <see cref="Build(string, IReadOnlyList{string})"/>, printing
    /// <paramref name="title"/> as a banner before the menu loop starts.</summary>
    public static string Build(string projectName, IReadOnlyList<string> entityNames, string title)
    {
        var mapEntries = string.Join(",\n",
            entityNames.Select(name =>
                $"    [typeof({name})] = () => new {name}Repository().GetAll()"));

        var registrations = string.Join(",\n",
            entityNames.Select(name =>
                $"    (\"{name}\", () => new EntityMenu<{name}>(new {name}Repository(), Related).Run())"));

        return $$"""
using {{projectName}};
using {{projectName}}.Data;
using {{projectName}}.Models;

SeedData.EnsureSeeded();

// typeof(Entity) -> that entity's stored rows, so a menu can offer a picker for a foreign key.
var repositories = new System.Collections.Generic.Dictionary<Type, Func<System.Collections.IEnumerable>>
{
{{mapEntries}}
};

System.Collections.IEnumerable? Related(Type entityType) =>
    repositories.TryGetValue(entityType, out var get) ? get() : null;

var menus = new (string Name, Action Run)[]
{
{{registrations}}
};

Console.WriteLine("{{title}}");

while (true)
{
    Console.WriteLine("\nEntities:");
    for (var i = 0; i < menus.Length; i++)
        Console.WriteLine($"  {i + 1}. {menus[i].Name}");
    Console.WriteLine("  0. Exit");
    Console.Write("> ");

    var input = Console.ReadLine();
    if (input is null || input.Trim() == "0") break;
    if (int.TryParse(input, out var n) && n >= 1 && n <= menus.Length)
        menus[n - 1].Run();
    else
        Console.WriteLine("Unrecognized choice.");
}
""";
    }
}
