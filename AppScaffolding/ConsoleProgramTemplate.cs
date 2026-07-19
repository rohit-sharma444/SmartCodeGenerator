namespace AppScaffolding;

/// <summary>Emits the console app entry point: a top-level menu registering one
/// <c>EntityMenu&lt;T&gt;</c> per discovered entity.</summary>
public static class ConsoleProgramTemplate
{
    public static string Build(string projectName, IReadOnlyList<string> entityNames)
    {
        var registrations = string.Join(",\n",
            entityNames.Select(name =>
                $"    (\"{name}\", () => new EntityMenu<{name}>(new {name}Repository()).Run())"));

        return $$"""
using {{projectName}};
using {{projectName}}.Data;
using {{projectName}}.Models;

var menus = new (string Name, Action Run)[]
{
{{registrations}}
};

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
