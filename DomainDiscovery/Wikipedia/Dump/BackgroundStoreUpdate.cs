namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// Runs the daily-increment update in the background while the app is used, reporting progress
/// through an <see cref="IStoreUpdateProgress"/> instead of blocking the prompt. Whatever happens —
/// offline, interrupted, failed — the app keeps running.
/// </summary>
public sealed class BackgroundStoreUpdate
{
    /// <summary>
    /// Applies the pending days, driving <paramref name="progress"/> as it goes, and returns how it
    /// ended. The day loop is <see cref="StartupDumpUpdate"/>'s, so the resume guarantees hold: only
    /// a day that fully lands moves the store's date.
    /// </summary>
    public StartupUpdateResult Run(
        DumpStaleness staleness, IStoreUpdateProgress progress,
        StartupDumpUpdate.ApplyDay apply, CancellationToken cancellation)
    {
        var count = staleness.PendingDays.Count;
        var index = 0;

        var result = new StartupDumpUpdate().Run(
            staleness,
            (day, token) =>
            {
                progress.BeginDay(index, count);
                apply(day, token);
                index++;
            },
            cancellation);

        progress.Finish(result.Outcome, result.DaysApplied);
        return result;
    }

    /// <summary>
    /// Fire-and-forget: checks freshness and, if the store is stale, applies the pending days on a
    /// background task. Never throws into the caller and never blocks it. Returns the task so the app
    /// can cancel it on exit.
    /// </summary>
    /// <param name="redirected">
    /// Whether output is redirected. Interactive sessions get the percentage in the console title;
    /// piped/scripted ones get a throttled log line on <see cref="Console.Error"/>, since there is no
    /// title or cursor to use. Keeping it off stdout is deliberate: a consumer that parses stdout for
    /// the prompt (a script, or the VS Code extension's REPL panel) must never see this background
    /// chatter interleaved with the "CodeGenerator >> " prompt, or it cannot tell when input is free.
    /// </param>
    public Task Start(string databasePath, bool redirected, CancellationToken cancellation)
        => Task.Run(() =>
        {
            try
            {
                var store = new WikipediaStore(databasePath);
                var staleness = new DumpFreshness().Check(store);
                var progress = BuildProgress(redirected);

                if (!staleness.IsStale)
                {
                    // Up to date, or Wikimedia never answered. Say "up to date" ONLY when we actually
                    // know it is — a check that went unanswered knows nothing.
                    if (staleness.IsCurrent)
                        progress.Finish(StartupUpdateOutcome.Completed, 0);
                    return;
                }

                var working = Path.Combine(Path.GetDirectoryName(databasePath) ?? ".", "incr");
                // stderr, not stdout: see the redirected-param note on Start — stdout must stay clean
                // for prompt-parsing consumers.
                var say = redirected ? (Action<string>)Console.Error.WriteLine : _ => { };
                var updater = new IncrementalUpdater(store, say: say, progress: progress);

                Run(staleness, progress,
                    (day, token) => updater.UpdateDay(day, working, token), cancellation);
            }
            catch
            {
                // Being unable to update is never fatal: the app runs on the store as it stands.
            }
        }, cancellation);

    private static IStoreUpdateProgress BuildProgress(bool redirected)
    {
        if (redirected)
            return new OverallPercentProgress(
                percent => WriteLineSafe($"  {StoreUpdateText.Updating(percent)}"),
                (outcome, days) => WriteLineSafe($"  {StoreUpdateText.Final(outcome, days)}"));

        return new OverallPercentProgress(
            percent => SetTitle(StoreUpdateText.Updating(percent)),
            (outcome, days) => SetTitle(StoreUpdateText.Final(outcome, days)));
    }

    private static void SetTitle(string text)
    {
        // Setting the title can throw when no console is attached; progress is cosmetic, so swallow.
        try { Console.Title = $"CodeGenerator — {text}"; }
        catch { /* no console; nothing to show */ }
    }

    private static void WriteLineSafe(string line)
    {
        // stderr, so stdout stays clean for prompt-parsing consumers (scripts / the VS Code panel).
        // A progress line is cosmetic; a failing write (e.g. a broken pipe when a piped consumer has
        // exited) must never abort the update. Swallow, exactly as SetTitle does.
        try { Console.Error.WriteLine(line); }
        catch { /* nothing to show */ }
    }
}
