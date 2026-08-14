namespace AppScaffolding;

/// <summary>Emits the worker host entry point registering <see cref="Worker"/>.</summary>
public static class WorkerProgramTemplate
{
    public static string Source(string rootNamespace) => Source(rootNamespace, seed: true);

    /// <summary>As <see cref="Source(string)"/>, seeding the stores on startup only when
    /// <paramref name="seed"/>.</summary>
    /// <remarks>
    /// The <c>Data</c> using goes with the call: seeding is the only thing this entry point needs
    /// that namespace for, and the generated app is the one place an unused using would sit in
    /// somebody's editor for good. Same reason <see cref="SeedDataTemplate"/> gates its own.
    /// </remarks>
    public static string Source(string rootNamespace, bool seed)
    {
        var seedCall = SeedDataTemplate.EnsureSeededCall(seed);

        var dataUsing = seed
            ? $"""

              using {rootNamespace}.Data;
              """
            : string.Empty;

        return $$"""
using {{rootNamespace}};{{dataUsing}}
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

{{seedCall}}builder.Build().Run();
""";
    }
}
