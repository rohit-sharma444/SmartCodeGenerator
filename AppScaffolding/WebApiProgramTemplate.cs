namespace AppScaffolding;

/// <summary>Emits a minimal-API entry point mapping REST CRUD endpoints for each entity.</summary>
public static class WebApiProgramTemplate
{
    public static string Build(string projectName, IReadOnlyList<string> entityNames)
        => Build(projectName, entityNames, projectName);

    /// <summary>As <see cref="Build(string, IReadOnlyList{string})"/>, serving
    /// <paramref name="title"/> from the root when it differs from <paramref name="projectName"/>.
    /// The generated Web API has no OpenAPI document to carry a title, so a branded run serves it
    /// from "/" instead; an unbranded run adds no endpoint at all, leaving today's output
    /// untouched.</summary>
    public static string Build(string projectName, IReadOnlyList<string> entityNames, string title)
        => Build(projectName, entityNames, title, seed: true);

    /// <summary>As the three-argument overload, seeding the stores on startup only when
    /// <paramref name="seed"/> — with sample data off there is no SeedData.cs to call.</summary>
    /// <param name="rootNamespace">
    /// The namespace this app's code is under, when it differs from <paramref name="projectName"/>.
    /// Separate from the project name because the root-endpoint gate below compares the title
    /// against the PROJECT name: a prefixed app whose title was never customized must stay without
    /// the endpoint.
    /// </param>
    public static string Build(
        string projectName, IReadOnlyList<string> entityNames, string title, bool seed,
        string? rootNamespace = null)
    {
        var ns = rootNamespace ?? projectName;
        var seedCall = SeedDataTemplate.EnsureSeededCall(seed);

        var registrations = string.Join("\n",
            entityNames.Select(name =>
                $"MapEntity<{name}>(app, \"{name.ToLowerInvariant()}\", new {name}Repository());"));

        var rootEndpoint = title == projectName
            ? string.Empty
            : $"app.MapGet(\"/\", () => \"{title}\");\n";

        // Triple-$ raw string: interpolations use {{{...}}}, so the double braces the generated
        // route templates need ({{id:int}}) stay literal.
        return $$$"""
using {{{ns}}}.Data;
using {{{ns}}}.Models;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

{{{seedCall}}}{{{rootEndpoint}}}{{{registrations}}}

app.Run();

static void MapEntity<T>(WebApplication app, string route, Repository<T> repository)
    where T : class, new()
{
    app.MapGet($"/{route}", () => repository.GetAll());

    app.MapGet($"/{route}/{{id:int}}", (int id) =>
        repository.GetById(id) is { } found ? Results.Ok(found) : Results.NotFound());

    app.MapPost($"/{route}", (T item) =>
    {
        var result = repository.Create(item);
        return result.Success
            ? Results.Created($"/{route}/{IdOf(result.Item!)}", result.Item)
            : Results.ValidationProblem(result.AsDictionary());
    });

    // The address names the record: a body with no id takes it, and a body naming another
    // record is refused rather than overwriting that one. NotFound is kept distinct from a
    // validation failure: an unknown Id is 404, an invalid record is 400.
    app.MapPut($"/{route}/{{id:int}}", (int id, T item) =>
    {
        var bodyId = IdOf(item);
        if (bodyId == 0)
            typeof(T).GetProperty("Id")!.SetValue(item, id);
        else if (bodyId != id)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Id"] = [$"The id in the body ({bodyId}) is not the id in the address ({id})."]
            });

        var result = repository.Update(item);
        if (result.NotFound)
            return Results.NotFound();

        return result.Success
            ? Results.NoContent()
            : Results.ValidationProblem(result.AsDictionary());
    });

    // Three outcomes, not two: an unknown Id is a 404, and a delete the validation pipeline
    // refused is a 400 carrying its reason, exactly as a rejected create or update is.
    app.MapDelete($"/{route}/{{id:int}}", (int id) =>
    {
        var result = repository.Delete(id);

        return result.Success ? Results.NoContent()
            : result.NotFound ? Results.NotFound()
            : Results.ValidationProblem(result.AsDictionary());
    });
}

static int IdOf(object item) => (int)(item.GetType().GetProperty("Id")?.GetValue(item) ?? 0);
""";
    }
}
