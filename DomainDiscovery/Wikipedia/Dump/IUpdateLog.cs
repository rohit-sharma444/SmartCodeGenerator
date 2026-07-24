namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// Where the updater records what it did. Deliberately tiny: the updater has milestones to report,
/// not a logging framework to configure.
/// </summary>
/// <remarks>
/// Implementations <b>must not throw</b>. A service that cannot write to its log must still update
/// the store — being unable to say what you are doing is not a reason to stop doing it.
/// </remarks>
public interface IUpdateLog
{
    void Info(string message);

    void Error(string message, Exception? failure = null);
}
