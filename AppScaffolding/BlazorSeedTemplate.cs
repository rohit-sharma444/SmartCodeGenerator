namespace AppScaffolding;

/// <summary>
/// A one-shot gate in front of <c>SeedData.EnsureSeeded()</c> for the Blazor target.
/// </summary>
/// <remarks>
/// Every other target seeds from its entry point — the desktop targets from the main view model,
/// the Web API and console from their own <c>Program.cs</c>. The Blazor target does not emit
/// <c>Program.cs</c> at all: <c>dotnet new blazor</c> writes a good one and rewriting it would mean
/// re-implementing the host and drifting from the SDK template every time it changes.
/// <para>
/// So the first page to load seeds instead. <c>EnsureSeeded</c> is already idempotent — it skips any
/// store that has rows — but it still reads every store to find that out, so the gate keeps that to
/// once per process rather than once per page view.
/// </para>
/// <para>
/// Locked because Blazor Server serves circuits concurrently: two visitors arriving together would
/// otherwise both see <c>_done</c> false and seed at the same time.
/// </para>
/// </remarks>
public static class BlazorSeedTemplate
{
    /// <summary>What a page calls, or empty when the app has no sample data to seed.</summary>
    public static string OnceCall(bool seed) => seed ? "AppSeed.Once();" : string.Empty;

    /// <summary>Source of <c>Data/AppSeed.cs</c>, or null when there is nothing to seed.</summary>
    public static string? Source(string rootNamespace, bool seed) => seed ? $$"""
namespace {{rootNamespace}}.Data;

/// <summary>Runs the sample-data seeder once, however many pages ask for it.</summary>
internal static class AppSeed
{
    private static readonly object Gate = new();

    private static bool _done;

    /// <summary>
    /// Seeds on the first call and does nothing afterwards.
    /// </summary>
    /// <remarks>
    /// SeedData.EnsureSeeded() is itself idempotent, so a second call would be harmless — but it
    /// reads every store to discover that, and this app has one page per class, each of which would
    /// otherwise pay that cost on every visit.
    /// </remarks>
    internal static void Once()
    {
        if (_done)
        {
            return;
        }

        lock (Gate)
        {
            if (_done)
            {
                return;
            }

            SeedData.EnsureSeeded();
            _done = true;
        }
    }
}
""" : null;
}
