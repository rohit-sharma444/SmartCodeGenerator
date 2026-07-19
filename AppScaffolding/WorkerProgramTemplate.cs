namespace AppScaffolding;

/// <summary>Emits the worker host entry point registering <see cref="Worker"/>.</summary>
public static class WorkerProgramTemplate
{
    public static string Source(string projectName) => $$"""
using {{projectName}};
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Build().Run();
""";
}
