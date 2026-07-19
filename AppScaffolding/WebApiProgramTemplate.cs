namespace AppScaffolding;

/// <summary>Emits a minimal-API entry point mapping REST CRUD endpoints for each entity.</summary>
public static class WebApiProgramTemplate
{
    public static string Build(string projectName, IReadOnlyList<string> entityNames)
    {
        var registrations = string.Join("\n",
            entityNames.Select(name =>
                $"MapEntity<{name}>(app, \"{name.ToLowerInvariant()}\", new {name}Repository());"));

        // Triple-$ raw string: interpolations use {{{...}}}, so the double braces the generated
        // route templates need ({{id:int}}) stay literal.
        return $$$"""
using {{{projectName}}}.Data;
using {{{projectName}}}.Models;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

{{{registrations}}}

app.Run();

static void MapEntity<T>(WebApplication app, string route, Repository<T> repository)
    where T : class, new()
{
    app.MapGet($"/{route}", () => repository.GetAll());

    app.MapGet($"/{route}/{{id:int}}", (int id) =>
        repository.GetById(id) is { } found ? Results.Ok(found) : Results.NotFound());

    app.MapPost($"/{route}", (T item) =>
    {
        repository.Create(item);
        return Results.Created($"/{route}", item);
    });

    app.MapPut($"/{route}/{{id:int}}", (int id, T item) =>
        repository.Update(item) ? Results.NoContent() : Results.NotFound());

    app.MapDelete($"/{route}/{{id:int}}", (int id) =>
        repository.Delete(id) ? Results.NoContent() : Results.NotFound());
}
""";
    }
}
