namespace AppScaffolding;

/// <summary>Emits the worker host entry point registering <see cref="Worker"/>.</summary>
public static class WorkerProgramTemplate
{
    public static string Source(string projectName) => $$"""
using {{projectName}};
using {{projectName}}.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

SeedData.EnsureSeeded();

builder.Build().Run();
""";
}
