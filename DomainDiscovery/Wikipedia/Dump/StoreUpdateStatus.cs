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
/// <param name="UpdatedUtc">
/// When THIS snapshot was written — distinct from <paramref name="LastSuccessUtc"/>, which records
/// the last SUCCESS, not the last write. A process killed mid-apply leaves a status file whose
/// state and percentage are frozen forever; without this, a reader cannot tell that snapshot apart
/// from a live one. Null only for a status predating this field, or <see cref="Unknown"/>.
/// </param>
public sealed record StoreUpdateStatus(
    StoreUpdateState State,
    int Percent,
    int PendingDays,
    int? CurrentDay,
    DateTimeOffset? LastSuccessUtc,
    string? LastError,
    string? ServiceVersion,
    DateTimeOffset? UpdatedUtc = null)
{
    /// <summary>Nothing is known: no file, an unreadable one, or one written by a newer version.</summary>
    public static readonly StoreUpdateStatus Unknown =
        new(StoreUpdateState.Unknown, 0, 0, null, null, null, null, null);
}
