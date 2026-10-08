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
        var loop = new BuildFixLoop(Rules(), ai);

        try
        {
            return loop.Run(projectPath, failedBuild, rebuild, progress);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The loop undoes its own round on any failure; this is the last resort. The edits
            // it made are still reported, and the round in progress is undone when it can be.
            var notes = new List<string>
            {
                $"The fix stopped ({ex.GetType().Name}). Some files may have been changed; "
                    + $"their originals are under {BuildFixReport.BackupLocation}.",
            };
            var session = loop.LastSession;

            if (session is not null)
                foreach (var file in SafeRevert(session))
                    notes.Add($"{file} was not restored; check it by hand.");

            return new FixOutcome(
                false, session?.CurrentRound ?? 0, session?.Edits ?? [], SafeParse(failedBuild, projectPath),
                notes, failedBuild);
        }
        finally
        {
            (ai as IDisposable)?.Dispose();
        }
    }

    private static IReadOnlyList<string> SafeRevert(FixSession session)
    {
        try
        {
            return session.RevertRound(session.CurrentRound);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return [];
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
