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
    {
        var registrations = string.Join("\n",
            entityNames.Select(name =>
                $"MapEntity<{name}>(app, \"{name.ToLowerInvariant()}\", new {name}Repository());"));

        var rootEndpoint = title == projectName
            ? string.Empty
            : $"app.MapGet(\"/\", () => \"{title}\");\n";

        // Triple-$ raw string: interpolations use {{{...}}}, so the double braces the generated
        // route templates need ({{id:int}}) stay literal.
        return $$$"""
using {{{projectName}}}.Data;
using {{{projectName}}}.Models;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

SeedData.EnsureSeeded();

{{{rootEndpoint}}}{{{registrations}}}

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
            ? Results.Created($"/{route}", result.Item)
            : Results.ValidationProblem(result.AsDictionary());
    });

    // NotFound is kept distinct from a validation failure: an unknown Id is 404, an invalid
    // record is 400.
    app.MapPut($"/{route}/{{id:int}}", (int id, T item) =>
    {
        var result = repository.Update(item);
        if (result.NotFound)
            return Results.NotFound();

        return result.Success
            ? Results.NoContent()
            : Results.ValidationProblem(result.AsDictionary());
    });

    app.MapDelete($"/{route}/{{id:int}}", (int id) =>
        repository.Delete(id) ? Results.NoContent() : Results.NotFound());
}
""";
    }
}
