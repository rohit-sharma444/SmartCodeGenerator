namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// Brings the few articles a discovery run is about to read up to date, and nothing else.
/// </summary>
/// <remarks>
/// This is the answer to a store that is a fortnight behind. Catching the WHOLE store up costs ~780
/// MB per day of drift — some 10 GB — and takes hours, and it changes nothing anyone can see,
/// because a run reads perhaps thirty articles out of 7.2 million. Refreshing those thirty costs a
/// couple of API calls and is the ONLY freshness that can alter a generated model.
/// <para>
/// Never throws, and never blocks the run on the network: an unreachable Wikipedia means the run
/// proceeds on the dump, which is exactly what the dump is for.
/// </para>
/// </remarks>
public interface IArticleRefresher
{
    /// <summary>
    /// Updates whichever of <paramref name="titles"/> have been edited since the store's dump was
    /// cut. A no-op for a store that does not know its own age, and offline.
    /// </summary>
    void Refresh(IEnumerable<string> titles);
}

/// <summary>Refreshes nothing — for the HTTP client, which is already reading a live Wikipedia.</summary>
public sealed class NoArticleRefresher : IArticleRefresher
{
    public void Refresh(IEnumerable<string> titles) { }
}
