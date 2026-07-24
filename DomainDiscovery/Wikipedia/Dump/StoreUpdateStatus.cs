namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>What the updater is doing, as reported to anyone who cares to look.</summary>
public enum StoreUpdateState
{
    /// <summary>Nothing has said otherwise — no status file, or one that could not be read.</summary>
    Unknown,

    /// <summary>Not updating. Either up to date, or waiting for the next tick.</summary>
    Idle,

    /// <summary>Asking Wikimedia what has been published.</summary>
    Checking,

    /// <summary>Downloading and applying pending days.</summary>
    Applying,

    /// <summary>The last cycle broke. <see cref="StoreUpdateStatus.LastError"/> says how.</summary>
    Failed,
}

/// <summary>
/// A snapshot of the updater's progress, published for other processes to read.
/// </summary>
/// <remarks>
/// This is a <b>convenience, never a source of truth</b>. The store's own dump date is the truth,
/// and a reader that cannot get a status must carry on regardless — hence
/// <see cref="Unknown"/> rather than an exception.
/// </remarks>
public sealed record StoreUpdateStatus(
    StoreUpdateState State,
    int Percent,
    int PendingDays,
    int? CurrentDay,
    DateTimeOffset? LastSuccessUtc,
    string? LastError,
    string? ServiceVersion)
{
    /// <summary>Nothing is known: no file, an unreadable one, or one written by a newer version.</summary>
    public static readonly StoreUpdateStatus Unknown =
        new(StoreUpdateState.Unknown, 0, 0, null, null, null, null);
}
