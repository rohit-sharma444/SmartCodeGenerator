using CodeGenerationEngine.Helper;
using DomainDiscovery;

namespace CodeGenerator.BuildFix;

internal interface IBuildFixer
{
    FixOutcome Fix(string projectPath, ProcessResult failedBuild, Func<ProcessResult> rebuild, Action<string> progress);
}

/// <summary>Production wiring: the three rules, and the local model when discovery.json uses Ollama.</summary>
internal sealed class BuildFixer : IBuildFixer
{
    private readonly Func<DiscoveryConfig> _load;
    private readonly Func<DiscoveryConfig, IBuildFixAi> _makeAi;

    public BuildFixer()
        : this(() => DiscoveryConfig.Load(), config => new OllamaBuildFixAi(config))
    {
    }

    /// <summary>The seam for tests: where the config comes from and how the model client is made.</summary>
    internal BuildFixer(Func<DiscoveryConfig> load, Func<DiscoveryConfig, IBuildFixAi> makeAi)
    {
        _load = load;
        _makeAi = makeAi;
    }

    internal static IReadOnlyList<IBuildFixRule> Rules()
        => [new DuplicatePartialRule(), new StaleCustomHookRule(), new NoLoginAuthUsingRule()];

    /// <summary>The model client, or null when discovery uses another source or the config cannot be used.</summary>
    internal IBuildFixAi? CreateAi()
    {
        try
        {
            var config = _load();

            return config is not null
                && string.Equals(config.Source, DiscoveryConfig.OllamaSource, StringComparison.OrdinalIgnoreCase)
                    ? _makeAi(config)
                    : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    public FixOutcome Fix(string projectPath, ProcessResult failedBuild, Func<ProcessResult> rebuild, Action<string> progress)
    {
        var ai = CreateAi();

        try
        {
            return new BuildFixLoop(Rules(), ai).Run(projectPath, failedBuild, rebuild, progress);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new FixOutcome(
                false, 0, [], SafeParse(failedBuild, projectPath),
                [$"The fix stopped ({ex.GetType().Name}). Some files may have been changed; "
                    + $"their originals are under {BuildFixReport.BackupLocation}."],
                failedBuild);
        }
        finally
        {
            (ai as IDisposable)?.Dispose();
        }
    }

    private static IReadOnlyList<BuildDiagnostic> SafeParse(ProcessResult build, string projectPath)
    {
        try
        {
            return BuildDiagnostics.Parse(build, projectPath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return [];
        }
    }
}
