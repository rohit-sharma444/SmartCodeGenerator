namespace AppScaffolding;

/// <summary>
/// Emits <c>Logging/AppLog.cs</c>: one static logger factory the generated app logs through.
/// </summary>
/// <remarks>
/// Deliberately minimal — no configuration file, no level switches. The generated app is a CRUD
/// scaffold, and a logging stack with its own configuration surface would be a second product
/// inside it.
/// </remarks>
public static class LoggingTemplate
{
    /// <summary>The folder the emitted file lives in.</summary>
    public const string FolderName = "Logging";

    /// <summary>The file this template emits.</summary>
    public const string FileName = "AppLog.cs";

    /// <summary>
    /// The packages a logging build needs, with the versions <c>dotnet add package</c> resolved for
    /// net10.0.
    /// </summary>
    /// <remarks>
    /// Pinned literals rather than a floating range: a version that cannot be resolved fails
    /// restore in every app that references these packages explicitly, and the generated project
    /// has no Directory.Packages.props to correct it from. The worker target does not reference
    /// them explicitly at all — it gets all three transitively from Microsoft.Extensions.Hosting,
    /// which pinning a literal alongside would downgrade the moment Hosting resolves ahead of it —
    /// see the exclusion in <c>AppScaffolder</c>.
    /// <c>LoggingTests.Every_declared_package_has_a_real_version</c> rejects anything that is not a
    /// version, and <c>GeneratedProjectBuildTests.ProjectWithLogging_Builds</c> restores them for
    /// real.
    /// </remarks>
    public static IReadOnlyList<(string Id, string Version)> Packages { get; } =
    [
        ("Microsoft.Extensions.Logging", "10.0.11"),
        ("Microsoft.Extensions.Logging.Console", "10.0.11"),
        ("Microsoft.Extensions.Logging.Debug", "10.0.11"),
    ];

    /// <summary>Source of <c>Logging/AppLog.cs</c> in <paramref name="rootNamespace"/>.Logging.</summary>
    public static string Source(string rootNamespace) => $$"""
using Microsoft.Extensions.Logging;

namespace {{rootNamespace}}.Logging;

/// <summary>The application's logger factory. Console and Debug providers only.</summary>
/// <remarks>
/// Static rather than injected: this scaffold has no container of its own, and the alternative —
/// threading an ILoggerFactory through every repository constructor — would change the shape of
/// generated code that has nothing else to do with logging.
/// </remarks>
public static class AppLog
{
    private static readonly ILoggerFactory Factory =
        LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
        });

    /// <summary>A logger categorised by <typeparamref name="T"/>.</summary>
    public static ILogger For<T>() => Factory.CreateLogger<T>();
}
""";
}
