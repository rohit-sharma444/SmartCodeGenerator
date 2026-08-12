namespace AppScaffolding;

/// <summary>Emits the console app entry point: a top-level menu registering one
/// <c>EntityMenu&lt;T&gt;</c> per discovered entity.</summary>
public static class ConsoleProgramTemplate
{
    public static string Build(string projectName, IReadOnlyList<string> entityNames)
        => Build(projectName, entityNames, projectName);

    /// <summary>As <see cref="Build(string, IReadOnlyList{string})"/>, printing
    /// <paramref name="title"/> as a banner before the menu loop starts. Gated the same way as
    /// the Web API's root endpoint: emitted only when the title differs from the bare project
    /// name, so an unbranded console app is byte-identical to today's.</summary>
    public static string Build(string projectName, IReadOnlyList<string> entityNames, string title)
        => Build(projectName, entityNames, title, seed: true);

    /// <summary>As the three-argument overload, seeding the stores on startup only when
    /// <paramref name="seed"/> — with sample data off, SeedData.cs is not generated either, so the
    /// call would not compile.</summary>
    /// <param name="rootNamespace">
    /// The namespace this app's code is under, when it differs from <paramref name="projectName"/>.
    /// Separate from the project name because the banner gate below compares the title against the
    /// PROJECT name: a prefixed app whose title was never customized must stay bannerless.
    /// </param>
    public static string Build(
        string projectName, IReadOnlyList<string> entityNames, string title, bool seed,
        string? rootNamespace = null)
    {
        var ns = rootNamespace ?? projectName;
        var seedCall = SeedDataTemplate.EnsureSeededCall(seed);

        var mapEntries = string.Join(",\n",
            entityNames.Select(name =>
                $"    [typeof({name})] = () => new {name}Repository().GetAll()"));

        var registrations = string.Join(",\n",
            entityNames.Select(name =>
                $"    (\"{name}\", () => new EntityMenu<{name}>(new {name}Repository(), Related).Run())"));

        var banner = title == projectName
            ? string.Empty
            : $"Console.WriteLine(\"{title}\");\n\n";

        return $$"""
using {{ns}};
using {{ns}}.Data;
using {{ns}}.Models;

{{seedCall}}// typeof(Entity) -> that entity's stored rows, so a menu can offer a picker for a foreign key.
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

{{banner}}while (true)
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
